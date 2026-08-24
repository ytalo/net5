using Video2BIC.Core.Imaging;
using Video2BIC.Core.Ocr;

namespace Photo2BIC.Core.Engines.Local;

/// <summary>
/// Motor local con un modelo CRNN/CTC ejecutado en ONNX Runtime.
/// </summary>
/// <remarks>
/// <para>
/// Es la alternativa a Tesseract que no depende de un binario del sistema y que,
/// con un modelo entrenado sobre fotografias de escena -PaddleOCR <c>rec</c>,
/// CRNN, o el que se exporte-, lee mucho mejor un rotulo degradado, sucio o con
/// sombra. Tesseract viene de documentos escaneados y se le nota.
/// </para>
/// <para>
/// A cambio hay que conseguir el modelo: son pesos entrenados, no codigo, y no
/// se versionan en el repositorio. Sin <c>--onnx-model</c> el motor se declara no
/// disponible y el resto sigue funcionando.
/// </para>
/// </remarks>
public sealed class OnnxPhotoEngine : LocalOcrEngine
{
    private readonly OnnxTextRecognizerOptions _options;

    public OnnxPhotoEngine(OnnxTextRecognizerOptions options)
        : base(preprocess: new TextPreprocessOptions
        {
            // Un modelo entrenado sobre fotografias espera fotografias. Binarizarlas
            // le entrega una imagen que no se parece a nada de lo que vio entrenando
            // y hunde el acierto, justo al reves que con Tesseract.
            Binarize = false,
            EnhanceContrast = true,
            NormalizePolarity = true,
            Deskew = true,
            MinHeight = 48,
        })
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public override string Name => "onnx";

    /// <inheritdoc />
    public override string Family => "onnx";

    /// <inheritdoc />
    public override string Description => string.IsNullOrWhiteSpace(_options.ModelPath)
        ? "Modelo CRNN/CTC en ONNX Runtime (sin configurar)"
        : $"Modelo CRNN/CTC '{Path.GetFileName(_options.ModelPath)}' en ONNX Runtime, " +
          $"proveedor {_options.Provider}";

    /// <inheritdoc />
    public override bool IsAvailable(out string reason)
    {
        if (string.IsNullOrWhiteSpace(_options.ModelPath))
        {
            reason = "falta el modelo. Indicalo con --onnx-model ruta/al/modelo.onnx " +
                     "(ver Photo2BIC/models/README.md).";
            return false;
        }

        if (!File.Exists(_options.ModelPath))
        {
            reason = $"no se encontro el modelo '{_options.ModelPath}'.";
            return false;
        }

        reason = Path.GetFileName(_options.ModelPath);
        return true;
    }

    /// <inheritdoc />
    protected override ITextRecognizer CreateRecognizer() => new OnnxTextRecognizer(_options);
}
