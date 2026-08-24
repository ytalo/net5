using Photo2BIC.Core.Configuration;
using Photo2BIC.Core.Engines.Cloud;
using Photo2BIC.Core.Engines.Local;
using Video2BIC.Core.Ocr;

namespace Photo2BIC.Core.Engines;

/// <summary>
/// Construye los motores conocidos a partir de la configuracion.
/// </summary>
/// <remarks>
/// Se instancian todos, tambien los que no estan configurados. Un motor sin
/// credenciales responde <c>Unavailable</c> con el motivo, y eso es informacion
/// util -«Azure esta ahi, solo te falta exportar la clave»- que se pierde si el
/// motor directamente no existe.
/// </remarks>
public static class EngineRegistry
{
    /// <summary>Nombres de todos los motores, en el orden en que se construyen.</summary>
    public static readonly IReadOnlyList<string> KnownEngines =
        ["tesseract-lineas", "tesseract-disperso", "onnx", "azure", "google", "textract", "ocrspace"];

    /// <summary>
    /// Construye los motores.
    /// </summary>
    /// <param name="settings">Configuracion.</param>
    /// <param name="requested">
    /// Nombres a construir; vacio construye todos. Un nombre desconocido es un
    /// error de uso, no algo que ignorar en silencio.
    /// </param>
    /// <param name="handler">
    /// Transporte HTTP de los motores remotos; <c>null</c> deja que cada uno cree
    /// el suyo. Se inyecta en las pruebas.
    /// </param>
    /// <exception cref="ArgumentException">Si algun nombre pedido no existe.</exception>
    public static IReadOnlyList<IPhotoOcrEngine> Build(
        EngineSettings settings,
        IReadOnlyList<string>? requested = null,
        HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var wanted = Normalize(requested);
        var engines = new List<IPhotoOcrEngine>();

        try
        {
            foreach (var name in KnownEngines)
            {
                if (wanted.Count > 0 && !wanted.Contains(name))
                {
                    continue;
                }

                engines.Add(Create(name, settings, handler));
            }

            return engines;
        }
        catch
        {
            foreach (var engine in engines)
            {
                engine.Dispose();
            }

            throw;
        }
    }

    private static IPhotoOcrEngine Create(string name, EngineSettings settings, HttpMessageHandler? handler)
        => name switch
        {
            "tesseract-lineas" => new TesseractPhotoEngine(
                TesseractLayout.Lines, TesseractOptionsFor(settings, 7)),

            "tesseract-disperso" => new TesseractPhotoEngine(
                TesseractLayout.Sparse, TesseractOptionsFor(settings, 11)),

            "onnx" => new OnnxPhotoEngine(settings.BuildOnnxOptions()),

            "azure" => new AzureVisionEngine(settings.Azure, handler),

            "google" => new GoogleVisionEngine(settings.Google, handler),

            "textract" => new AwsTextractEngine(settings.Aws, handler),

            "ocrspace" => new OcrSpaceEngine(settings.OcrSpace, handler),

            _ => throw new ArgumentException($"Motor desconocido: '{name}'.", nameof(name)),
        };

    private static TesseractOptions TesseractOptionsFor(EngineSettings settings, int pageSegmentationMode) => new()
    {
        Executable = settings.TesseractExecutable,
        PageSegmentationMode = pageSegmentationMode,
        MinConfidence = 0.25f,
    };

    /// <summary>
    /// Normaliza y valida los nombres pedidos, admitiendo <c>local</c> y
    /// <c>nube</c> como atajos.
    /// </summary>
    private static HashSet<string> Normalize(IReadOnlyList<string>? requested)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (requested is null || requested.Count == 0)
        {
            return wanted;
        }

        foreach (var raw in requested)
        {
            var name = raw.Trim().ToLowerInvariant();

            switch (name)
            {
                case "todos":
                case "all":
                    return [];

                case "local":
                case "locales":
                    wanted.UnionWith(["tesseract-lineas", "tesseract-disperso", "onnx"]);
                    break;

                case "nube":
                case "cloud":
                    wanted.UnionWith(["azure", "google", "textract", "ocrspace"]);
                    break;

                case "tesseract":
                    wanted.UnionWith(["tesseract-lineas", "tesseract-disperso"]);
                    break;

                default:
                    if (!KnownEngines.Contains(name))
                    {
                        throw new ArgumentException(
                            $"Motor desconocido: '{raw}'. Los que hay son: " +
                            $"{string.Join(", ", KnownEngines)} (o los atajos local, nube, tesseract, todos).",
                            nameof(requested));
                    }

                    wanted.Add(name);
                    break;
            }
        }

        return wanted;
    }
}
