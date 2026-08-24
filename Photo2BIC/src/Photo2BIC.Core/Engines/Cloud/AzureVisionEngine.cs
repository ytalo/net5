using System.Net.Http.Headers;
using System.Text.Json;
using Video2BIC.Core.Text;

namespace Photo2BIC.Core.Engines.Cloud;

/// <summary>Configuracion de <see cref="AzureVisionEngine"/>.</summary>
public sealed class AzureVisionOptions
{
    /// <summary>
    /// Extremo del recurso, sin ruta: <c>https://mi-recurso.cognitiveservices.azure.com</c>.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Clave del recurso.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Version de la API de Analisis de Imagenes.</summary>
    public string ApiVersion { get; set; } = "2024-02-01";

    /// <summary>Tiempo maximo por llamada.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30d);
}

/// <summary>
/// Motor remoto sobre Azure AI Vision, operacion <c>imageanalysis:analyze</c> con
/// la caracteristica <c>read</c>.
/// </summary>
/// <remarks>
/// <para>
/// Es el servicio que mejor se porta con el caso concreto de este proyecto: su
/// lectura esta entrenada sobre texto «en escena» -senales, matriculas, rotulos
/// pintados- y no sobre documentos escaneados, que es justo la diferencia que se
/// nota al leer un codigo pintado sobre chapa corrugada con sombras duras.
/// </para>
/// <para>
/// Se usa la version sincrona 4.0, que responde en la misma llamada. La antigua
/// <c>Read 3.2</c> obligaba a sondear una operacion con <c>Operation-Location</c>
/// hasta que terminaba; para una sola fotografia no aporta nada y complica el
/// codigo.
/// </para>
/// </remarks>
public sealed class AzureVisionEngine : RestOcrEngine
{
    private readonly AzureVisionOptions _options;

    public AzureVisionEngine(AzureVisionOptions options, HttpMessageHandler? handler = null)
        : base(handler, options?.Timeout)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public override string Name => "azure";

    /// <inheritdoc />
    public override string Family => "azure";

    /// <inheritdoc />
    public override string Description =>
        $"Azure AI Vision {_options.ApiVersion} (imageanalysis:analyze, feature read)" +
        (string.IsNullOrWhiteSpace(_options.Endpoint) ? " (sin configurar)" : $" en {_options.Endpoint}");

    /// <inheritdoc />
    public override bool IsAvailable(out string reason)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.Key))
        {
            reason = "faltan credenciales. Exporta AZURE_VISION_ENDPOINT y AZURE_VISION_KEY.";
            return false;
        }

        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out _))
        {
            reason = $"AZURE_VISION_ENDPOINT no es una URL absoluta: '{_options.Endpoint}'.";
            return false;
        }

        reason = _options.Endpoint;
        return true;
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<TextFragment>> CallAsync(
        byte[] jpeg, int width, int height, CancellationToken cancellationToken)
    {
        var url = $"{_options.Endpoint.TrimEnd('/')}/computervision/imageanalysis:analyze" +
                  $"?api-version={Uri.EscapeDataString(_options.ApiVersion)}&features=read";

        var body = await SendAsync(
            () =>
            {
                var content = new ByteArrayContent(jpeg);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                request.Headers.Add("Ocp-Apim-Subscription-Key", _options.Key);
                return request;
            },
            cancellationToken).ConfigureAwait(false);

        return Parse(body);
    }

    /// <summary>
    /// Interpreta la respuesta: <c>readResult.blocks[].lines[].words[]</c>.
    /// </summary>
    /// <remarks>
    /// Se toma el nivel de palabra, no el de linea, porque es el unico que trae
    /// confianza y porque un codigo repartido en varias lineas llega como palabras
    /// sueltas en orden de lectura, que es lo que el analizador de codigos sabe
    /// recomponer. Si un bloque no trae palabras se cae al nivel de linea, sin
    /// confianza declarada.
    /// </remarks>
    internal static IReadOnlyList<TextFragment> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        var fragments = new List<TextFragment>();
        var readResult = CloudGeometry.Child(document.RootElement, "readResult");

        foreach (var block in CloudGeometry.Array(CloudGeometry.Child(readResult, "blocks")))
        {
            foreach (var line in CloudGeometry.Array(CloudGeometry.Child(block, "lines")))
            {
                var words = CloudGeometry.Child(line, "words");
                var wordCount = 0;

                foreach (var word in CloudGeometry.Array(words))
                {
                    var text = CloudGeometry.String(word, "text");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var (left, top, boxWidth, boxHeight) =
                        CloudGeometry.FromPolygon(CloudGeometry.Child(word, "boundingPolygon"));

                    fragments.Add(new TextFragment(
                        text,
                        (float)(CloudGeometry.Number(word, "confidence") ?? 1d),
                        left,
                        top,
                        boxWidth,
                        boxHeight));

                    wordCount++;
                }

                if (wordCount > 0)
                {
                    continue;
                }

                var lineText = CloudGeometry.String(line, "text");
                if (!string.IsNullOrWhiteSpace(lineText))
                {
                    var (left, top, boxWidth, boxHeight) =
                        CloudGeometry.FromPolygon(CloudGeometry.Child(line, "boundingPolygon"));

                    fragments.Add(new TextFragment(lineText, 1f, left, top, boxWidth, boxHeight));
                }
            }
        }

        return fragments;
    }
}
