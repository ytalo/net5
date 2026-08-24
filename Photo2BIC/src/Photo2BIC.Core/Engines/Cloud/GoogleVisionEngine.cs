using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Video2BIC.Core.Text;

namespace Photo2BIC.Core.Engines.Cloud;

/// <summary>Configuracion de <see cref="GoogleVisionEngine"/>.</summary>
public sealed class GoogleVisionOptions
{
    /// <summary>Clave de API. Alternativa a <see cref="AccessToken"/>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Token OAuth 2.0 (<c>gcloud auth print-access-token</c>). Alternativa a
    /// <see cref="ApiKey"/>, y la unica via si el proyecto tiene restringidas las
    /// claves de API.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Extremo del servicio.</summary>
    public string Endpoint { get; set; } = "https://vision.googleapis.com/v1/images:annotate";

    /// <summary>Confianza que se asume cuando la respuesta no la declara.</summary>
    public float AssumedConfidence { get; set; } = 0.8f;

    /// <summary>Tiempo maximo por llamada.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30d);
}

/// <summary>
/// Motor remoto sobre Google Cloud Vision, operacion <c>images:annotate</c> con
/// la caracteristica <c>TEXT_DETECTION</c>.
/// </summary>
/// <remarks>
/// <para>
/// Se pide <c>TEXT_DETECTION</c> y no <c>DOCUMENT_TEXT_DETECTION</c>: la segunda
/// esta pensada para paginas densas de texto y asume una estructura de parrafos
/// que la puerta de un contenedor no tiene. Sobre rotulos sueltos la primera
/// acierta bastante mas.
/// </para>
/// <para>
/// Aporta algo que ningun otro motor de este proyecto: esta entrenada sobre una
/// coleccion de fotografias del mundo real enorme y muy variada, asi que se
/// equivoca de otra manera. Para el consenso eso vale mas que ser el mejor motor,
/// porque un error que nadie mas comete es un error que el resto puede corregir.
/// </para>
/// </remarks>
public sealed class GoogleVisionEngine : RestOcrEngine
{
    private readonly GoogleVisionOptions _options;

    public GoogleVisionEngine(GoogleVisionOptions options, HttpMessageHandler? handler = null)
        : base(handler, options?.Timeout)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public override string Name => "google";

    /// <inheritdoc />
    public override string Family => "google";

    /// <inheritdoc />
    public override string Description =>
        "Google Cloud Vision v1 (images:annotate, TEXT_DETECTION)" +
        (IsConfigured ? string.Empty : " (sin configurar)");

    private bool IsConfigured
        => !string.IsNullOrWhiteSpace(_options.ApiKey) || !string.IsNullOrWhiteSpace(_options.AccessToken);

    /// <inheritdoc />
    public override bool IsAvailable(out string reason)
    {
        if (!IsConfigured)
        {
            reason = "faltan credenciales. Exporta GOOGLE_VISION_API_KEY, " +
                     "o GOOGLE_VISION_ACCESS_TOKEN con la salida de 'gcloud auth print-access-token'.";
            return false;
        }

        reason = string.IsNullOrWhiteSpace(_options.AccessToken) ? "clave de API" : "token OAuth";
        return true;
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<TextFragment>> CallAsync(
        byte[] jpeg, int width, int height, CancellationToken cancellationToken)
    {
        var payload = BuildRequest(jpeg);

        var url = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.Endpoint
            : $"{_options.Endpoint}?key={Uri.EscapeDataString(_options.ApiKey)}";

        var body = await SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };

                if (!string.IsNullOrWhiteSpace(_options.AccessToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
                }

                return request;
            },
            cancellationToken).ConfigureAwait(false);

        return Parse(body, _options.AssumedConfidence);
    }

    private static string BuildRequest(byte[] jpeg)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("requests");
            writer.WriteStartObject();

            writer.WriteStartObject("image");
            writer.WriteBase64String("content", jpeg);
            writer.WriteEndObject();

            writer.WriteStartArray("features");
            writer.WriteStartObject();
            writer.WriteString("type", "TEXT_DETECTION");
            writer.WriteEndObject();
            writer.WriteEndArray();

            // El codigo BIC es alfanumerico latino. La pista de idioma evita que el
            // servicio proponga transcripciones en otros alfabetos para caracteres
            // ambiguos, que es un error que aqui no interesa cometer.
            writer.WriteStartObject("imageContext");
            writer.WriteStartArray("languageHints");
            writer.WriteStringValue("en");
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Interpreta la respuesta.
    /// </summary>
    /// <remarks>
    /// Se prefiere <c>fullTextAnnotation</c>, que es la unica rama que trae
    /// confianza por palabra. Si no viene se cae a <c>textAnnotations</c>, cuyo
    /// primer elemento es el texto completo de la imagen y el resto las palabras
    /// sueltas: por eso se salta el primero, que si se incluyera duplicaria todo el
    /// texto y falsearia el recuento de evidencias.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Si la respuesta trae un error del servicio.</exception>
    internal static IReadOnlyList<TextFragment> Parse(string json, float assumedConfidence)
    {
        using var document = JsonDocument.Parse(json);

        var responses = CloudGeometry.Array(CloudGeometry.Child(document.RootElement, "responses")).ToList();
        if (responses.Count == 0)
        {
            return [];
        }

        var response = responses[0];

        // Cloud Vision devuelve 200 con el error dentro del cuerpo, asi que el codigo
        // de estado HTTP no basta para detectarlo.
        var error = CloudGeometry.Child(response, "error");
        if (error.ValueKind == JsonValueKind.Object)
        {
            var message = CloudGeometry.String(error, "message") ?? "error sin descripcion";
            throw new InvalidOperationException($"Cloud Vision devolvio un error: {message}");
        }

        var fragments = FromFullText(response, assumedConfidence);
        return fragments.Count > 0 ? fragments : FromAnnotations(response, assumedConfidence);
    }

    private static List<TextFragment> FromFullText(JsonElement response, float assumedConfidence)
    {
        var fragments = new List<TextFragment>();
        var fullText = CloudGeometry.Child(response, "fullTextAnnotation");

        foreach (var page in CloudGeometry.Array(CloudGeometry.Child(fullText, "pages")))
        {
            foreach (var block in CloudGeometry.Array(CloudGeometry.Child(page, "blocks")))
            {
                foreach (var paragraph in CloudGeometry.Array(CloudGeometry.Child(block, "paragraphs")))
                {
                    foreach (var word in CloudGeometry.Array(CloudGeometry.Child(paragraph, "words")))
                    {
                        var text = ReadSymbols(word);
                        if (text.Length == 0)
                        {
                            continue;
                        }

                        var (left, top, width, height) = CloudGeometry.FromPolygon(
                            CloudGeometry.Child(CloudGeometry.Child(word, "boundingBox"), "vertices"));

                        fragments.Add(new TextFragment(
                            text,
                            (float)(CloudGeometry.Number(word, "confidence") ?? assumedConfidence),
                            left,
                            top,
                            width,
                            height));
                    }
                }
            }
        }

        return fragments;
    }

    private static string ReadSymbols(JsonElement word)
    {
        var builder = new StringBuilder();

        foreach (var symbol in CloudGeometry.Array(CloudGeometry.Child(word, "symbols")))
        {
            var text = CloudGeometry.String(symbol, "text");
            if (!string.IsNullOrEmpty(text))
            {
                builder.Append(text);
            }
        }

        return builder.ToString();
    }

    private static List<TextFragment> FromAnnotations(JsonElement response, float assumedConfidence)
    {
        var fragments = new List<TextFragment>();
        var annotations = CloudGeometry.Array(CloudGeometry.Child(response, "textAnnotations")).ToList();

        // El elemento 0 es la transcripcion completa de la imagen, no una palabra.
        for (var index = 1; index < annotations.Count; index++)
        {
            var annotation = annotations[index];
            var text = CloudGeometry.String(annotation, "description");

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var (left, top, width, height) = CloudGeometry.FromPolygon(
                CloudGeometry.Child(CloudGeometry.Child(annotation, "boundingPoly"), "vertices"));

            fragments.Add(new TextFragment(text, assumedConfidence, left, top, width, height));
        }

        // Con una sola anotacion, esa unica es el texto completo y es todo lo que hay.
        if (fragments.Count == 0 && annotations.Count == 1)
        {
            var text = CloudGeometry.String(annotations[0], "description");
            if (!string.IsNullOrWhiteSpace(text))
            {
                fragments.Add(new TextFragment(text, assumedConfidence));
            }
        }

        return fragments;
    }
}
