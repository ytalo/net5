using System.Diagnostics;
using MaritimeVision.Core.Models;
using OpenCvSharp;
using Video2BIC.Core.Bic;
using Video2BIC.Core.Imaging;
using Video2BIC.Core.Ocr;
using Video2BIC.Core.Text;
using Video2BIC.Core.TextRegions;

namespace Video2BIC.Core.Recognition;

/// <summary>
/// Encadena recorte, localizacion de texto, OCR e interpretacion ISO 6346 para
/// sacar el codigo BIC de un contenedor ya detectado.
/// </summary>
/// <remarks>
/// <para>
/// El orden importa: recortar primero reduce el problema de «leer un puerto» a
/// «leer un panel», que es donde un OCR generico se defiende. Ampliar despues
/// compensa que un contenedor a media distancia ocupe pocos pixeles. Y buscar
/// regiones antes de reconocer evita pedirle al motor que interprete a la vez el
/// codigo, la marca del naviero y los pictogramas de mercancia peligrosa.
/// </para>
/// <para>
/// La instancia no es segura para uso concurrente: reutiliza el analizador y el
/// motor de OCR, que tampoco lo son.
/// </para>
/// </remarks>
public sealed class ContainerBicRecognizer : IBicRecognizer
{
    private readonly ITextRecognizer _recognizer;
    private readonly ITextRegionDetector _regionDetector;
    private readonly BicCodeParser _parser;
    private readonly BicRecognizerOptions _options;
    private readonly bool _ownsRecognizer;

    private bool _disposed;

    /// <summary>Construye la cadena de reconocimiento.</summary>
    /// <param name="recognizer">Motor de OCR.</param>
    /// <param name="regionDetector">
    /// Localizador de lineas de texto; <c>null</c> usa el basado en MSER.
    /// </param>
    /// <param name="parser">Analizador de codigos; <c>null</c> usa el de por defecto.</param>
    /// <param name="options">Configuracion; <c>null</c> usa la de por defecto.</param>
    /// <param name="ownsRecognizer">
    /// Si <c>true</c>, <see cref="Dispose"/> libera tambien el motor de OCR.
    /// </param>
    public ContainerBicRecognizer(
        ITextRecognizer recognizer,
        ITextRegionDetector? regionDetector = null,
        BicCodeParser? parser = null,
        BicRecognizerOptions? options = null,
        bool ownsRecognizer = true)
    {
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _regionDetector = regionDetector ?? new MserTextRegionDetector();
        _parser = parser ?? new BicCodeParser();
        _options = options ?? new BicRecognizerOptions();
        _options.Validate();
        _ownsRecognizer = ownsRecognizer;
    }

    /// <inheritdoc />
    public string Description => $"{_recognizer.Description}; {_regionDetector.Description}";

    /// <summary>
    /// <c>true</c> si la caja es lo bastante grande como para intentar leerla.
    /// </summary>
    /// <remarks>
    /// El pipeline lo consulta antes de encolar una lectura: descartar aqui es
    /// gratis y ahorra la parte cara del fotograma.
    /// </remarks>
    public bool IsWorthReading(BoundingBox box)
        => box.Width >= _options.MinBoxWidth && box.Height >= _options.MinBoxHeight;

    /// <inheritdoc />
    public BicReadingResult Read(Mat frame, BoundingBox box)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (frame.Empty() || !IsWorthReading(box))
        {
            return BicReadingResult.Empty;
        }

        var stopwatch = Stopwatch.StartNew();

        var rect = RoiExtractor.ToRect(box, frame.Width, frame.Height, _options.BoxMargin);
        using var roi = RoiExtractor.Extract(frame, rect, _options.RoiHeight, _options.MaxRoiScale, out var scale);

        if (roi.Empty())
        {
            return BicReadingResult.Empty;
        }

        var (fragments, regions) = ReadFragments(roi);
        if (fragments.Count == 0)
        {
            return new BicReadingResult(
                Array.Empty<BicCandidate>(), fragments, regions, null, rect, scale, stopwatch.Elapsed);
        }

        var candidates = _parser.Parse(fragments);

        var best = candidates.Count > 0 ? candidates[0].Code.Value : null;
        var sizeType = SizeTypeCode.TryFind(fragments, best, out var found) ? found : null;

        return new BicReadingResult(candidates, fragments, regions, sizeType, rect, scale, stopwatch.Elapsed);
    }

    private (IReadOnlyList<TextFragment> Fragments, IReadOnlyList<Rect> Regions) ReadFragments(Mat roi)
    {
        // Un motor con analisis de disposicion propio (Tesseract en modo texto
        // disperso) ya devuelve las palabras colocadas; pedirle ademas recortes por
        // region solo compensa si se pide explicitamente.
        if (_recognizer.PerformsLayoutAnalysis && !_options.AlwaysUseRegionDetector)
        {
            using var prepared = TextImagePreprocessor.Prepare(roi, WholeRoiPreprocess());
            var recognized = _recognizer.Recognize(prepared);
            return (SortForReading(recognized), Array.Empty<Rect>());
        }

        var regions = _regionDetector.Detect(roi);
        var fragments = new List<TextFragment>();
        var boxes = new List<Rect>(regions.Count);

        foreach (var region in regions.Take(_options.MaxRegionsPerRead))
        {
            var clamped = region.Box & new Rect(0, 0, roi.Width, roi.Height);
            if (clamped.Width < 8 || clamped.Height < 6)
            {
                continue;
            }

            boxes.Add(clamped);

            using var view = new Mat(roi, clamped);
            using var prepared = TextImagePreprocessor.Prepare(view, _options.Preprocess);

            foreach (var fragment in _recognizer.Recognize(prepared))
            {
                // Las posiciones que devuelve el motor son relativas al recorte de
                // linea; se trasladan al recorte del contenedor para poder ordenar
                // todos los trozos entre si.
                fragments.Add(fragment with
                {
                    Left = clamped.X + fragment.Left,
                    Top = clamped.Y + fragment.Top,
                });
            }
        }

        return (SortForReading(fragments), boxes);
    }

    /// <summary>
    /// Preprocesado del recorte completo: sin binarizar ni enderezar, porque a
    /// escala de panel la inclinacion no es uniforme y el umbral adaptativo se come
    /// los rotulos pequenos.
    /// </summary>
    private TextPreprocessOptions WholeRoiPreprocess() => new()
    {
        MinHeight = 0,
        MaxHeight = 0,
        EnhanceContrast = _options.Preprocess.EnhanceContrast,
        NormalizePolarity = false,
        Binarize = false,
        Deskew = false,
    };

    /// <summary>
    /// Ordena los trozos como se leen. Dos trozos cuya diferencia vertical es menor
    /// que su propia altura se consideran de la misma linea y se ordenan de
    /// izquierda a derecha.
    /// </summary>
    internal static IReadOnlyList<TextFragment> SortForReading(IReadOnlyList<TextFragment> fragments)
    {
        if (fragments.Count <= 1)
        {
            return fragments;
        }

        var lineHeight = Math.Max(1, (int)fragments.Average(fragment => Math.Max(1, fragment.Height)));

        return fragments
            .OrderBy(fragment => fragment.Top / lineHeight)
            .ThenBy(fragment => fragment.Left)
            .ToList();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsRecognizer)
        {
            _recognizer.Dispose();
        }
    }
}
