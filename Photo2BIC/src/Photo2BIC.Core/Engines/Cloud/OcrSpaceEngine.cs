using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Video2BIC.Core.Text;

namespace Photo2BIC.Core.Engines.Cloud;

/// <summary>Configuracion de <see cref="OcrSpaceEngine"/>.</summary>
public sealed class OcrSpaceOptions
{
    /// <summary>
    /// Clave de API. La clave publica de pruebas <c>helloworld</c> funciona con un
    /// limite muy bajo de peticiones y sirve para comprobar el recorrido completo
    /// sin darse de alta en nada.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Extremo del servicio.</summary>
    public string Endpoint { get; set; } = "https://api.ocr.space/parse/image";

    /// <summary>
    /// Motor del servicio: 1 es el clasico, 2 esta mejor con texto suelto y
    /// alfanumerico, que es el caso de un codigo BIC.
    /// </summary>
    public int Engine { get; set; } = 2;

    /// <summary>Confianza que se asume: el servicio no la declara.</summary>
    public float AssumedConfidence { get; set; } = 0.7f;

    /// <summary>Tiempo maximo por llamada.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(40d);
}

/// <summary>
/// Motor remoto sobre OCR.space.
/// </summary>
/// <remarks>
/// <para>
/// Es el servicio que se puede probar sin cuenta, sin tarjeta y sin consola de
/// nube: con la clave publica <c>helloworld</c> se comprueba que el recorrido
/// completo -codificar, enviar, interpretar, votar- funciona de verdad contra un
/// servicio real. Para eso esta aqui.
/// </para>
/// <para>
/// No declara confianza por palabra, asi que se le asigna una fija y
/// deliberadamente moderada. Eso hace que en el consenso pese menos que un motor
/// que si sabe decir cuanto se fia de lo que ha leido, que es exactamente lo que
/// corresponde.
/// </para>
/// </remarks>
public sealed class OcrSpaceEngine : RestOcrEngine
{
    private readonly OcrSpaceOptions _options;

    public OcrSpaceEngine(OcrSpaceOptions options, HttpMessageHandler? handler = null)
        : base(handler, options?.Timeout)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public override string Name => "ocrspace";

    /// <inheritdoc />
    public override string Family => "ocrspace";

    /// <inheritdoc />
    public override string Description =>
        $"OCR.space motor {_options.Engine}" +
        (string.IsNullOrWhiteSpace(_options.ApiKey) ? " (sin configurar)" : string.Empty);

    /// <inheritdoc />
    public override bool IsAvailable(out string reason)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            reason = "falta la clave. Exporta OCRSPACE_API_KEY " +
                     "(la clave publica de pruebas es 'helloworld').";
            return false;
        }

        reason = _options.ApiKey == "helloworld" ? "clave publica de pruebas" : "clave propia";
        return true;
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<TextFragment>> CallAsync(
        byte[] jpeg, int width, int height, CancellationToken cancellationToken)
    {
        var body = await SendAsync(
            () =>
            {
                var form = new MultipartFormDataContent
                {
                    { new StringContent(_options.ApiKey), "apikey" },
                    { new StringContent("eng"), "language" },
                    { new StringContent(_options.Engine.ToString(CultureInfo.InvariantCulture)), "OCREngine" },

                    // La superposicion es la que trae las posiciones de cada palabra,
                    // imprescindibles para saber en que orden se leen. El escalado
                    // ayuda con las fotos en las que el rotulo sale pequeno.
                    { new StringContent("true"), "isOverlayRequired" },
                    { new StringContent("true"), "scale" },
                };

                var image = new ByteArrayContent(jpeg);
                image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                form.Add(image, "file", "container.jpg");

                return new HttpRequestMessage(HttpMethod.Post, _options.Endpoint) { Content = form };
            },
            cancellationToken).ConfigureAwait(false);

        return Parse(body, _options.AssumedConfidence);
    }

    /// <summary>
    /// Interpreta la respuesta, prefiriendo la superposicion de palabras al texto
    /// plano porque solo la primera trae posiciones.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si el servicio informa de un error.</exception>
    internal static IReadOnlyList<TextFragment> Parse(string json, float assumedConfidence)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // El servicio responde 200 con el error dentro del cuerpo.
        if (root.TryGetProperty("IsErroredOnProcessing", out var errored)
            && errored.ValueKind == JsonValueKind.True)
        {
            throw new InvalidOperationException($"OCR.space devolvio un error: {ErrorMessage(root)}");
        }

        var fragments = new List<TextFragment>();

        foreach (var result in CloudGeometry.Array(CloudGeometry.Child(root, "ParsedResults")))
        {
            var overlay = CloudGeometry.Child(result, "TextOverlay");
            var before = fragments.Count;

            foreach (var line in CloudGeometry.Array(CloudGeometry.Child(overlay, "Lines")))
            {
                foreach (var word in CloudGeometry.Array(CloudGeometry.Child(line, "Words")))
                {
                    var text = CloudGeometry.String(word, "WordText");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    fragments.Add(new TextFragment(
                        text,
                        assumedConfidence,
                        (int)(CloudGeometry.Number(word, "Left") ?? 0d),
                        (int)(CloudGeometry.Number(word, "Top") ?? 0d),
                        (int)(CloudGeometry.Number(word, "Width") ?? 0d),
                        (int)(CloudGeometry.Number(word, "Height") ?? 0d)));
                }
            }

            if (fragments.Count > before)
            {
                continue;
            }

            // Sin superposicion queda el texto plano. Se trocea por lineas para
            // conservar al menos el orden de lectura, que es lo que hace falta para
            // recomponer un codigo repartido en varias.
            var parsed = CloudGeometry.String(result, "ParsedText");
            if (string.IsNullOrWhiteSpace(parsed))
            {
                continue;
            }

            foreach (var line in parsed.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                fragments.Add(new TextFragment(line.Trim(), assumedConfidence));
            }
        }

        return fragments;
    }

    /// <summary>
    /// Mensaje de error del servicio, que llega unas veces como cadena y otras como
    /// lista de cadenas.
    /// </summary>
    private static string ErrorMessage(JsonElement root)
    {
        var builder = new StringBuilder();

        void Append(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    var text = element.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        builder.Append(builder.Length > 0 ? "; " : string.Empty).Append(text.Trim());
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Append(item);
                    }

                    break;
            }
        }

        if (root.TryGetProperty("ErrorMessage", out var message))
        {
            Append(message);
        }

        foreach (var result in CloudGeometry.Array(CloudGeometry.Child(root, "ParsedResults")))
        {
            if (result.TryGetProperty("ErrorMessage", out var resultMessage))
            {
                Append(resultMessage);
            }
        }

        return builder.Length > 0 ? builder.ToString() : "error sin descripcion";
    }
}
