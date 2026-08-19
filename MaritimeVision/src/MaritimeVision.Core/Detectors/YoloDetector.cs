using System.Runtime.InteropServices;
using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Configuration;
using MaritimeVision.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace MaritimeVision.Core.Detectors;

/// <summary>
/// Detector YOLO ejecutado con ONNX Runtime.
/// </summary>
/// <remarks>
/// Acepta indistintamente modelos YOLOv5/v7 (con objectness), YOLOv8/v9/v11 (sin
/// objectness) y exportaciones con NMS incorporada: el formato se deduce de la
/// forma del tensor de salida, de modo que cambiar de modelo no obliga a tocar
/// codigo. La instancia no es segura para uso concurrente.
/// </remarks>
public sealed class YoloDetector : IObjectDetector
{
    private readonly YoloDetectorOptions _options;
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly LabelSet _labels;
    private readonly IReadOnlySet<int> _targetClasses;
    private readonly float[] _inputBuffer;

    private YoloOutputSpec? _outputSpec;
    private bool _disposed;

    public YoloDetector(YoloDetectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;

        _session = CreateSession(options);

        var input = _session.InputMetadata.First();
        _inputName = input.Key;
        _outputName = _session.OutputMetadata.Keys.First();

        // Un modelo exportado con ejes dinamicos declara -1 en alto y ancho; en ese
        // caso manda la configuracion.
        var inputShape = input.Value.Dimensions;
        _inputWidth = inputShape.Length == 4 && inputShape[3] > 0 ? inputShape[3] : options.InputSize;
        _inputHeight = inputShape.Length == 4 && inputShape[2] > 0 ? inputShape[2] : options.InputSize;

        _labels = options.Labels ?? InferLabels();
        _targetClasses = options.TargetClasses.Count > 0
            ? _labels.Resolve(options.TargetClasses)
            : new HashSet<int>();

        _inputBuffer = new float[3 * _inputHeight * _inputWidth];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Labels => _labels.Labels;

    /// <inheritdoc />
    public string Description =>
        $"YOLO ONNX '{Path.GetFileName(_options.ModelPath)}' entrada {_inputWidth}x{_inputHeight}, " +
        $"{_labels.Count} clases, proveedor {_options.Provider}";

    /// <summary>Interpretacion del tensor de salida; se resuelve en la primera inferencia.</summary>
    public YoloOutputSpec? OutputSpec => _outputSpec;

    /// <summary>Tamano de entrada efectivo del modelo.</summary>
    public (int Width, int Height) InputSize => (_inputWidth, _inputHeight);

    /// <inheritdoc />
    public IReadOnlyList<Detection> Detect(Mat frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (frame.Empty())
        {
            return Array.Empty<Detection>();
        }

        var letterbox = Letterbox.Compute(frame.Width, frame.Height, _inputWidth, _inputHeight);
        FillInputBuffer(frame, letterbox);

        var tensor = new DenseTensor<float>(_inputBuffer, [1, 3, _inputHeight, _inputWidth]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);

        var output = results.First(value => value.Name == _outputName).AsTensor<float>();
        if (_outputSpec is null)
        {
            _outputSpec = YoloOutputSpec.Infer(output.Dimensions.ToArray(), _labels.Count, _options.OutputFormat);
            WarnIfLabelsDoNotMatchModel(_outputSpec);
        }

        var raw = YoloOutputParser.Parse(
            output.ToArray(),
            _outputSpec,
            _labels,
            _options.ConfidenceThreshold,
            _targetClasses);

        // Las cajas salen en el espacio del lienzo del modelo; hay que deshacer el
        // relleno y el escalado antes de suprimir no maximos, para que el umbral de
        // IoU se aplique sobre geometria real.
        for (var i = 0; i < raw.Count; i++)
        {
            raw[i] = raw[i] with { Box = letterbox.Invert(raw[i].Box) };
        }

        return _outputSpec.IsEndToEnd
            ? raw.Take(_options.MaxDetections).ToList()
            : NonMaxSuppression.Apply(
                raw, _options.IouThreshold, _options.ClassAgnosticNms, _options.MaxDetections);
    }

    /// <summary>
    /// Convierte el fotograma BGR en el tensor NCHW en RGB normalizado a [0, 1]
    /// que espera YOLO, reutilizando el mismo buffer en cada fotograma.
    /// </summary>
    private void FillInputBuffer(Mat frame, in Letterbox letterbox)
    {
        using var padded = letterbox.Apply(frame, _inputWidth, _inputHeight);
        using var rgb = new Mat();
        Cv2.CvtColor(padded, rgb, ColorConversionCodes.BGR2RGB);

        using var normalized = new Mat();
        rgb.ConvertTo(normalized, MatType.CV_32FC3, 1d / 255d);

        var plane = _inputHeight * _inputWidth;
        var channels = normalized.Split();
        try
        {
            for (var c = 0; c < 3; c++)
            {
                // Split devuelve planos contiguos, asi que basta con una copia por canal.
                Marshal.Copy(channels[c].Data, _inputBuffer, c * plane, plane);
            }
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    /// <summary>
    /// Ejecuta una inferencia en vacio para resolver el formato de salida y
    /// calentar ONNX Runtime.
    /// </summary>
    /// <remarks>
    /// Conviene llamarlo al arrancar: adelanta cualquier error de configuracion
    /// (un fichero de etiquetas que no corresponde al modelo, una salida que no se
    /// sabe interpretar) al momento del arranque en vez de a mitad de la
    /// reproduccion, y evita que el primer fotograma real cargue con el coste de
    /// inicializacion del motor.
    /// </remarks>
    public void Warmup()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var blank = new Mat(_inputHeight, _inputWidth, MatType.CV_8UC3, Scalar.All(0));
        Detect(blank);
    }

    /// <summary>
    /// Avisa cuando el fichero de etiquetas declara un numero de clases distinto
    /// del que emite el modelo: es casi siempre un fichero equivocado, y provoca
    /// que las detecciones salgan con la etiqueta de otra clase.
    /// </summary>
    private void WarnIfLabelsDoNotMatchModel(YoloOutputSpec spec)
    {
        if (spec.IsEndToEnd || _options.Labels is null || spec.ClassCount == _labels.Count)
        {
            return;
        }

        Console.Error.WriteLine(
            $"[aviso] El fichero de etiquetas declara {_labels.Count} clases pero " +
            $"'{Path.GetFileName(_options.ModelPath)}' emite {spec.ClassCount}. " +
            "Las detecciones pueden salir con la etiqueta equivocada; revisa --labels.");
    }

    private static InferenceSession CreateSession(YoloDetectorOptions options)
    {
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };

        if (options.IntraOpThreads > 0)
        {
            sessionOptions.IntraOpNumThreads = options.IntraOpThreads;
        }

        // Los proveedores de GPU viven en paquetes NuGet aparte. Si no estan
        // instalados se sigue en CPU en lugar de reventar el arranque.
        try
        {
            switch (options.Provider)
            {
                case ExecutionProvider.Cuda:
                    sessionOptions.AppendExecutionProvider_CUDA(options.DeviceId);
                    break;
                case ExecutionProvider.DirectML:
                    sessionOptions.AppendExecutionProvider_DML(options.DeviceId);
                    break;
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException
                                       or DllNotFoundException
                                       or OnnxRuntimeException
                                       or TypeInitializationException)
        {
            Console.Error.WriteLine(
                $"[aviso] No se pudo activar el proveedor {options.Provider} ({ex.GetType().Name}); " +
                "se continua en CPU. Instala Microsoft.ML.OnnxRuntime.Gpu o .DirectML para usar GPU.");
        }

        try
        {
            return new InferenceSession(options.ModelPath, sessionOptions);
        }
        catch
        {
            sessionOptions.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Deduce las etiquetas cuando no se han indicado: si el modelo declara 80
    /// clases se asume COCO, y si no se generan nombres genericos.
    /// </summary>
    private LabelSet InferLabels()
    {
        var dimensions = _session.OutputMetadata[_outputName].Dimensions;

        if (dimensions.Length >= 2)
        {
            var candidates = dimensions.Skip(dimensions.Length - 2).ToArray();

            // Sin conocer el numero de clases no hay forma exacta de saber cual de
            // las dos dimensiones son los features, asi que se asume la convencion
            // habitual (las anclas son muchas mas) y solo se recurre a la otra si
            // la menor resulta demasiado pequena para contener siquiera una caja.
            var features = Math.Min(candidates[0], candidates[1]);
            if (features <= 4)
            {
                features = Math.Max(candidates[0], candidates[1]);
            }

            if (features > 4)
            {
                var classCount = features - 4;
                return classCount == 80 ? LabelSet.Coco : LabelSet.Generic(classCount);
            }
        }

        return LabelSet.Coco;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
    }
}
