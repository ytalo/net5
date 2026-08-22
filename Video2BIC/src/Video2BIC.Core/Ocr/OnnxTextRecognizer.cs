using System.Runtime.InteropServices;
using MaritimeVision.Core.Detectors;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Video2BIC.Core.Text;

namespace Video2BIC.Core.Ocr;

/// <summary>
/// Reconocedor de una linea de texto con un modelo CRNN/CTC ejecutado en ONNX
/// Runtime (CRNN, PaddleOCR <c>rec</c>, o cualquier exportacion equivalente).
/// </summary>
/// <remarks>
/// <para>
/// El modelo no localiza texto: espera un recorte de una sola linea, ya
/// enderezado. De localizarlo se ocupa <c>ITextRegionDetector</c>.
/// </para>
/// <para>
/// El numero de canales de entrada, el alto y la disposicion de la salida se
/// deducen de los metadatos del modelo, para que cambiar de red no obligue a
/// tocar codigo. La instancia no es segura para uso concurrente.
/// </para>
/// </remarks>
public sealed class OnnxTextRecognizer : ITextRecognizer
{
    private readonly OnnxTextRecognizerOptions _options;
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly int _channels;
    private readonly int _height;
    private readonly int _fixedWidth;

    private bool _disposed;

    public OnnxTextRecognizer(OnnxTextRecognizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;

        _session = CreateSession(options);

        var input = _session.InputMetadata.First();
        _inputName = input.Key;
        _outputName = _session.OutputMetadata.Keys.First();

        var shape = input.Value.Dimensions;
        _channels = shape.Length == 4 && shape[1] > 0 ? shape[1] : 1;
        _height = shape.Length == 4 && shape[2] > 0 ? shape[2] : options.InputHeight;

        // Un ancho fijo obliga a rellenar siempre hasta el mismo tamano; uno
        // dinamico permite ajustarlo al recorte y ahorrar computo.
        _fixedWidth = shape.Length == 4 && shape[3] > 0 ? shape[3] : 0;

        if (_channels is not (1 or 3))
        {
            throw new InvalidOperationException(
                $"El modelo de OCR declara {_channels} canales de entrada; solo se admiten 1 o 3.");
        }
    }

    /// <inheritdoc />
    public string Description =>
        $"OCR CTC ONNX '{Path.GetFileName(_options.ModelPath)}' entrada {_channels}x{_height}x" +
        $"{(_fixedWidth > 0 ? _fixedWidth.ToString() : "dinamico")}, " +
        $"{_options.Charset.Characters.Length} caracteres, proveedor {_options.Provider}";

    /// <inheritdoc />
    /// <remarks>Un modelo de reconocimiento no analiza la disposicion de la pagina.</remarks>
    public bool PerformsLayoutAnalysis => false;

    /// <inheritdoc />
    public IReadOnlyList<TextFragment> Recognize(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (image.Empty() || image.Width < 2 || image.Height < 2)
        {
            return Array.Empty<TextFragment>();
        }

        var width = ResolveWidth(image);
        var buffer = new float[_channels * _height * width];
        FillInputBuffer(image, width, buffer);

        var tensor = new DenseTensor<float>(buffer, [1, _channels, _height, width]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);

        var output = results.First(value => value.Name == _outputName).AsTensor<float>();
        var decoding = DecodeOutput(output);

        if (decoding.Text.Length == 0 || decoding.Confidence < _options.MinConfidence)
        {
            return Array.Empty<TextFragment>();
        }

        return [new TextFragment(decoding.Text, decoding.Confidence, 0, 0, image.Width, image.Height)];
    }

    /// <summary>
    /// Ejecuta una inferencia en vacio: adelanta al arranque cualquier error de
    /// forma de la salida y paga alli el coste de inicializacion del motor.
    /// </summary>
    public void Warmup()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var blank = new Mat(_height, Math.Max(_options.MinInputWidth, _height * 4), MatType.CV_8UC1, Scalar.All(255));
        Recognize(blank);
    }

    private int ResolveWidth(Mat image)
    {
        if (_fixedWidth > 0)
        {
            return _fixedWidth;
        }

        var scaled = (int)Math.Round((double)image.Width * _height / image.Height);
        return Math.Clamp(scaled, _options.MinInputWidth, _options.MaxInputWidth);
    }

    /// <summary>
    /// Escala el recorte conservando la proporcion, lo rellena por la derecha hasta
    /// el ancho de entrada y lo normaliza al tensor NCHW que espera el modelo.
    /// </summary>
    private void FillInputBuffer(Mat image, int width, float[] buffer)
    {
        using var converted = new Mat();
        if (_channels == 1 && image.Channels() != 1)
        {
            Cv2.CvtColor(image, converted, ColorConversionCodes.BGR2GRAY);
        }
        else if (_channels == 3 && image.Channels() == 1)
        {
            Cv2.CvtColor(image, converted, ColorConversionCodes.GRAY2BGR);
        }
        else
        {
            image.CopyTo(converted);
        }

        // Se escala al alto del modelo y se rellena por la derecha en vez de
        // deformar: un caracter estirado es un caracter que el modelo no vio nunca
        // durante el entrenamiento.
        var scaledWidth = Math.Clamp(
            (int)Math.Round((double)converted.Width * _height / converted.Height), 1, width);

        using var resized = new Mat();
        Cv2.Resize(converted, resized, new Size(scaledWidth, _height), interpolation: InterpolationFlags.Cubic);

        using var padded = new Mat(
            _height,
            width,
            resized.Type(),
            _channels == 1 ? Scalar.All(0) : new Scalar(0, 0, 0));

        resized.CopyTo(padded[new Rect(0, 0, scaledWidth, _height)]);

        using var normalized = new Mat();
        var scale = 1d / (255d * _options.StdDev);
        var shift = -_options.Mean / _options.StdDev;
        padded.ConvertTo(normalized, _channels == 1 ? MatType.CV_32FC1 : MatType.CV_32FC3, scale, shift);

        var plane = _height * width;
        if (_channels == 1)
        {
            Marshal.Copy(normalized.Data, buffer, 0, plane);
            return;
        }

        var channels = normalized.Split();
        try
        {
            for (var index = 0; index < 3; index++)
            {
                Marshal.Copy(channels[index].Data, buffer, index * plane, plane);
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
    /// Decodifica la salida deduciendo si viene como <c>[1, franjas, clases]</c> o
    /// como <c>[1, clases, franjas]</c>.
    /// </summary>
    private CtcDecoding DecodeOutput(Tensor<float> output)
    {
        var dimensions = output.Dimensions.ToArray();
        var values = output.ToArray();
        var classCount = _options.Charset.ClassCount;

        // La dimension de clases es la que coincide con el alfabeto. Si ninguna
        // coincide, el alfabeto no corresponde al modelo y hay que decirlo.
        var trailing = dimensions.Length >= 1 ? dimensions[^1] : 0;
        var middle = dimensions.Length >= 2 ? dimensions[^2] : 0;

        if (trailing == classCount)
        {
            return CtcDecoder.Decode(values, classCount, _options.Charset, _options.ApplySoftmax);
        }

        if (middle == classCount)
        {
            return CtcDecoder.DecodeTransposed(values, classCount, _options.Charset, _options.ApplySoftmax);
        }

        throw new InvalidOperationException(
            $"El modelo '{Path.GetFileName(_options.ModelPath)}' emite una salida " +
            $"[{string.Join(", ", dimensions)}] y ninguna dimension vale {classCount}, " +
            $"que es el numero de clases del alfabeto ({_options.Charset.Characters.Length} caracteres " +
            "mas el blanco). Revisa el fichero de alfabeto o la posicion del blanco.");
    }

    private static InferenceSession CreateSession(OnnxTextRecognizerOptions options)
    {
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };

        if (options.IntraOpThreads > 0)
        {
            sessionOptions.IntraOpNumThreads = options.IntraOpThreads;
        }

        // Los proveedores de GPU viven en paquetes NuGet aparte; si no estan, se
        // sigue en CPU en vez de reventar el arranque.
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
                $"[aviso] No se pudo activar el proveedor {options.Provider} para el OCR " +
                $"({ex.GetType().Name}); se continua en CPU.");
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
