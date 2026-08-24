using Video2BIC.Core.Imaging;
using Video2BIC.Core.Ocr;

namespace Photo2BIC.Core.Engines.Local;

/// <summary>Como se le entrega la fotografia a Tesseract.</summary>
public enum TesseractLayout
{
    /// <summary>
    /// Se localizan las lineas antes y se le pasa cada una recortada
    /// (<c>--psm 7</c>).
    /// </summary>
    Lines = 0,

    /// <summary>
    /// Se le entrega la fotografia entera y busca el solo el texto disperso
    /// (<c>--psm 11</c>).
    /// </summary>
    Sparse = 1,
}

/// <summary>
/// Motor local apoyado en el binario de Tesseract.
/// </summary>
/// <remarks>
/// <para>
/// Se registra dos veces con configuraciones distintas -<c>tesseract-lineas</c> y
/// <c>tesseract-disperso</c>- porque las dos formas de plantearle el problema
/// aciertan en casos distintos. Recortando las lineas antes se le da a Tesseract
/// justo lo que mejor se le da, pero se depende de que MSER encuentre el rotulo;
/// entregandole el panel entero se salta esa dependencia, a costa de que el
/// corrugado le genere lineas fantasma.
/// </para>
/// <para>
/// Comparten familia a proposito. Son el mismo motor con dos ajustes: cuando los
/// dos leen <c>MSKU1234565</c> eso no son dos opiniones independientes, y el
/// consenso tiene que saberlo para no inflar la confianza.
/// </para>
/// </remarks>
public sealed class TesseractPhotoEngine : LocalOcrEngine
{
    private readonly TesseractOptions _options;
    private readonly TesseractLayout _layout;

    /// <param name="layout">Como plantearle el problema.</param>
    /// <param name="options">
    /// Configuracion; <c>null</c> usa la adecuada para <paramref name="layout"/>.
    /// </param>
    public TesseractPhotoEngine(TesseractLayout layout = TesseractLayout.Lines, TesseractOptions? options = null)
        : base(preprocess: PreprocessFor(layout))
    {
        _layout = layout;
        _options = options ?? new TesseractOptions
        {
            // 7 es «una sola linea»; 11 es «texto disperso, buscalo tu».
            PageSegmentationMode = layout == TesseractLayout.Lines ? 7 : 11,

            // Sobre un rotulo grande Tesseract acierta con holgura, pero sobre chapa
            // corrugada devuelve tambien basura con confianza baja. El umbral se deja
            // permisivo porque el digito de control ya es un filtro mucho mas duro
            // que cualquier umbral de confianza, y descartar pronto solo pierde
            // lecturas recuperables.
            MinConfidence = 0.25f,
        };
    }

    /// <inheritdoc />
    public override string Name => _layout == TesseractLayout.Lines ? "tesseract-lineas" : "tesseract-disperso";

    /// <inheritdoc />
    public override string Family => "tesseract";

    /// <inheritdoc />
    public override string Description => _layout == TesseractLayout.Lines
        ? $"Tesseract '{_options.Executable}' psm {_options.PageSegmentationMode} sobre lineas localizadas con MSER"
        : $"Tesseract '{_options.Executable}' psm {_options.PageSegmentationMode} sobre la fotografia completa";

    /// <inheritdoc />
    public override bool IsAvailable(out string reason)
    {
        if (TesseractTextRecognizer.IsAvailable(_options.Executable, out var version))
        {
            reason = version;
            return true;
        }

        reason = $"no se encontro el ejecutable '{_options.Executable}'. " +
                 "Instalalo con 'apt install tesseract-ocr', 'brew install tesseract' " +
                 "o 'winget install UB-Mannheim.TesseractOCR'.";
        return false;
    }

    /// <inheritdoc />
    protected override ITextRecognizer CreateRecognizer() => new TesseractTextRecognizer(_options);

    /// <summary>
    /// Preparacion de los recortes segun el modo.
    /// </summary>
    /// <remarks>
    /// Con lineas recortadas se binariza: Tesseract trabaja internamente sobre
    /// imagen binaria y darsela ya umbralizada con el contexto de la linea entera
    /// da mejor resultado que dejarle aplicar su propio umbral a un recorte
    /// pequeno. En modo disperso no se toca la imagen, porque el «recorte» es la
    /// fotografia completa y ahi si conviene su propio analisis.
    /// </remarks>
    private static TextPreprocessOptions PreprocessFor(TesseractLayout layout) => new()
    {
        Binarize = layout == TesseractLayout.Lines,
        EnhanceContrast = true,
        NormalizePolarity = true,
        Deskew = true,
        MinHeight = 64,
    };
}
