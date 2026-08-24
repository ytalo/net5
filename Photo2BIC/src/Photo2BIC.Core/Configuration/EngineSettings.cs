using MaritimeVision.Core.Detectors;
using Photo2BIC.Core.Engines.Cloud;
using Video2BIC.Core.Ocr;

namespace Photo2BIC.Core.Configuration;

/// <summary>
/// Todo lo que hace falta para construir los motores: rutas, claves y extremos.
/// </summary>
/// <remarks>
/// Las credenciales se leen de variables de entorno y nunca de la linea de
/// comandos. Es deliberado: un argumento queda en el historial del interprete y
/// en la lista de procesos de la maquina, donde lo ve cualquiera.
/// </remarks>
public sealed class EngineSettings
{
    /// <summary>Ejecutable de Tesseract.</summary>
    public string TesseractExecutable { get; set; } = "tesseract";

    /// <summary>Ruta del modelo CRNN/CTC en ONNX. Vacia deja el motor no disponible.</summary>
    public string OnnxModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Fichero con el alfabeto del modelo, un caracter por linea. Vacio usa los 36
    /// alfanumericos en mayusculas, que es lo unico que aparece en un codigo BIC.
    /// </summary>
    public string OnnxCharsetPath { get; set; } = string.Empty;

    /// <summary>Proveedor de ejecucion del modelo ONNX.</summary>
    public ExecutionProvider OnnxProvider { get; set; } = ExecutionProvider.Cpu;

    /// <summary>Azure AI Vision.</summary>
    public AzureVisionOptions Azure { get; } = new();

    /// <summary>Google Cloud Vision.</summary>
    public GoogleVisionOptions Google { get; } = new();

    /// <summary>Amazon Textract.</summary>
    public AwsTextractOptions Aws { get; } = new();

    /// <summary>OCR.space.</summary>
    public OcrSpaceOptions OcrSpace { get; } = new();

    /// <summary>Tiempo maximo por llamada a un servicio remoto.</summary>
    public TimeSpan CloudTimeout
    {
        set
        {
            Azure.Timeout = value;
            Google.Timeout = value;
            Aws.Timeout = value;
            OcrSpace.Timeout = value;
        }
    }

    /// <summary>Nombres de las variables de entorno que se consultan, para el diagnostico.</summary>
    public static readonly IReadOnlyList<(string Variable, string Purpose)> EnvironmentVariables =
    [
        ("AZURE_VISION_ENDPOINT", "extremo del recurso de Azure AI Vision"),
        ("AZURE_VISION_KEY", "clave del recurso de Azure AI Vision"),
        ("GOOGLE_VISION_API_KEY", "clave de API de Google Cloud Vision"),
        ("GOOGLE_VISION_ACCESS_TOKEN", "token OAuth de Google Cloud Vision, alternativa a la clave"),
        ("AWS_ACCESS_KEY_ID", "identificador de clave de AWS"),
        ("AWS_SECRET_ACCESS_KEY", "clave secreta de AWS"),
        ("AWS_SESSION_TOKEN", "token de sesion de AWS, si las credenciales son temporales"),
        ("AWS_REGION", "region de Amazon Textract (o AWS_DEFAULT_REGION)"),
        ("OCRSPACE_API_KEY", "clave de OCR.space ('helloworld' para probar)"),
        ("PHOTO2BIC_ONNX_MODEL", "ruta del modelo CRNN/CTC en ONNX"),
        ("PHOTO2BIC_ONNX_CHARSET", "ruta del alfabeto del modelo ONNX"),
        ("PHOTO2BIC_TESSERACT", "ejecutable de Tesseract, si no esta en el PATH"),
    ];

    /// <summary>Lee la configuracion del entorno.</summary>
    /// <param name="read">
    /// Lector de variables; <c>null</c> usa el del proceso. Se sustituye en las
    /// pruebas para no depender del entorno de quien las ejecute.
    /// </param>
    public static EngineSettings FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;

        var settings = new EngineSettings
        {
            TesseractExecutable = Value(read, "PHOTO2BIC_TESSERACT") ?? "tesseract",
            OnnxModelPath = Value(read, "PHOTO2BIC_ONNX_MODEL") ?? string.Empty,
            OnnxCharsetPath = Value(read, "PHOTO2BIC_ONNX_CHARSET") ?? string.Empty,
        };

        settings.Azure.Endpoint = Value(read, "AZURE_VISION_ENDPOINT") ?? string.Empty;
        settings.Azure.Key = Value(read, "AZURE_VISION_KEY") ?? string.Empty;

        settings.Google.ApiKey = Value(read, "GOOGLE_VISION_API_KEY") ?? string.Empty;
        settings.Google.AccessToken = Value(read, "GOOGLE_VISION_ACCESS_TOKEN") ?? string.Empty;

        settings.Aws.Credentials = new AwsCredentials(
            Value(read, "AWS_ACCESS_KEY_ID") ?? string.Empty,
            Value(read, "AWS_SECRET_ACCESS_KEY") ?? string.Empty,
            Value(read, "AWS_SESSION_TOKEN"));

        settings.Aws.Region = Value(read, "AWS_REGION")
                              ?? Value(read, "AWS_DEFAULT_REGION")
                              ?? "us-east-1";

        settings.OcrSpace.ApiKey = Value(read, "OCRSPACE_API_KEY") ?? string.Empty;

        return settings;
    }

    /// <summary>Opciones del reconocedor ONNX segun esta configuracion.</summary>
    public OnnxTextRecognizerOptions BuildOnnxOptions()
    {
        var options = new OnnxTextRecognizerOptions
        {
            ModelPath = OnnxModelPath,
            Provider = OnnxProvider,
        };

        if (!string.IsNullOrWhiteSpace(OnnxCharsetPath) && File.Exists(OnnxCharsetPath))
        {
            options.Charset = CtcCharset.FromFile(OnnxCharsetPath);
        }

        return options;
    }

    private static string? Value(Func<string, string?> read, string name)
    {
        var value = read(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
