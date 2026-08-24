using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Video2BIC.Core.Text;

namespace Photo2BIC.Core.Engines.Cloud;

/// <summary>Configuracion de <see cref="AwsTextractEngine"/>.</summary>
public sealed class AwsTextractOptions
{
    /// <summary>Credenciales.</summary>
    public AwsCredentials Credentials { get; set; }

    /// <summary>Region del servicio.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Extremo; vacio lo construye a partir de la region. Se puede fijar para
    /// apuntar a un extremo de VPC o a un doble local.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Confianza minima por palabra, en [0, 1].</summary>
    public float MinConfidence { get; set; } = 0.2f;

    /// <summary>Tiempo maximo por llamada.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30d);

    /// <summary>URL efectiva del servicio.</summary>
    public string ResolveEndpoint()
        => string.IsNullOrWhiteSpace(Endpoint)
            ? $"https://textract.{Region}.amazonaws.com/"
            : Endpoint;
}

/// <summary>
/// Motor remoto sobre Amazon Textract, operacion <c>DetectDocumentText</c>.
/// </summary>
/// <remarks>
/// <para>
/// Textract viene del mundo de los documentos -facturas, formularios, albaranes-
/// y eso se nota: sobre un rotulo pintado en chapa no gana a Azure ni a Google.
/// Esta aqui por dos razones practicas. La primera es que muchas terminales ya
/// tienen su operativa en AWS y usar el proveedor que ya esta contratado se
/// impone a menudo sobre cual es el mejor motor. La segunda es que un documento
/// de transporte fotografiado -la carta de porte donde tambien figura el codigo-
/// es exactamente su terreno.
/// </para>
/// <para>
/// Se llama al protocolo JSON directamente, con la peticion firmada por
/// <see cref="AwsSignatureV4"/>, en vez de a traves del SDK oficial.
/// </para>
/// </remarks>
public sealed class AwsTextractEngine : RestOcrEngine
{
    private const string Service = "textract";
    private const string Target = "Textract.DetectDocumentText";
    private const string ContentType = "application/x-amz-json-1.1";

    private readonly AwsTextractOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    /// <param name="options">Configuracion.</param>
    /// <param name="handler">Transporte HTTP; <c>null</c> crea uno propio.</param>
    /// <param name="clock">
    /// Reloj de la firma; solo se sustituye en pruebas, donde la firma tiene que
    /// ser reproducible.
    /// </param>
    public AwsTextractEngine(
        AwsTextractOptions options,
        HttpMessageHandler? handler = null,
        Func<DateTimeOffset>? clock = null)
        : base(handler, options?.Timeout)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public override string Name => "textract";

    /// <inheritdoc />
    public override string Family => "aws";

    /// <inheritdoc />
    public override string Description =>
        $"Amazon Textract DetectDocumentText en {_options.Region}" +
        (_options.Credentials.IsComplete ? string.Empty : " (sin configurar)");

    /// <inheritdoc />
    public override bool IsAvailable(out string reason)
    {
        if (!_options.Credentials.IsComplete)
        {
            reason = "faltan credenciales. Exporta AWS_ACCESS_KEY_ID y AWS_SECRET_ACCESS_KEY " +
                     "(y AWS_SESSION_TOKEN si son temporales).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.Region))
        {
            reason = "falta la region. Exporta AWS_REGION, por ejemplo eu-west-1.";
            return false;
        }

        reason = _options.Region;
        return true;
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<TextFragment>> CallAsync(
        byte[] jpeg, int width, int height, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"{{\"Document\":{{\"Bytes\":\"{Convert.ToBase64String(jpeg)}\"}}}}");

        var uri = new Uri(_options.ResolveEndpoint());

        var body = await SendAsync(
            () => BuildRequest(uri, payload),
            cancellationToken).ConfigureAwait(false);

        return Parse(body, width, height, _options.MinConfidence);
    }

    private HttpRequestMessage BuildRequest(Uri uri, byte[] payload)
    {
        var headers = AwsSignatureV4.Sign(
            "POST",
            uri,
            new Dictionary<string, string>
            {
                ["content-type"] = ContentType,
                ["x-amz-target"] = Target,
                ["x-amz-content-sha256"] = AwsSignatureV4.HashPayload(payload),
            },
            payload,
            _options.Credentials,
            _options.Region,
            Service,
            _clock());

        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);

        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };

        foreach (var (name, value) in headers)
        {
            // 'host' lo pone HttpClient con el mismo valor que se firmo, y
            // 'content-type' ya esta en el contenido: anadirlos otra vez a las
            // cabeceras de la peticion lanzaria.
            if (name is "host" or "content-type")
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    /// <summary>
    /// Interpreta la respuesta quedandose con los bloques de tipo <c>WORD</c>.
    /// </summary>
    /// <remarks>
    /// Textract devuelve el mismo texto tres veces, como pagina, como linea y como
    /// palabra. Se toma el nivel de palabra: es el mas fino, trae confianza y no
    /// duplica evidencias, que es lo que pasaria si se contaran tambien las lineas.
    /// </remarks>
    internal static IReadOnlyList<TextFragment> Parse(
        string json, int width, int height, float minConfidence)
    {
        using var document = JsonDocument.Parse(json);

        var fragments = new List<TextFragment>();

        foreach (var block in CloudGeometry.Array(CloudGeometry.Child(document.RootElement, "Blocks")))
        {
            if (CloudGeometry.String(block, "BlockType") != "WORD")
            {
                continue;
            }

            var text = CloudGeometry.String(block, "Text");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Textract expresa la confianza en porcentaje.
            var confidence = (float)((CloudGeometry.Number(block, "Confidence") ?? 100d) / 100d);
            if (confidence < minConfidence)
            {
                continue;
            }

            var (left, top, boxWidth, boxHeight) = CloudGeometry.FromNormalizedBox(
                CloudGeometry.Child(CloudGeometry.Child(block, "Geometry"), "BoundingBox"), width, height);

            fragments.Add(new TextFragment(text, confidence, left, top, boxWidth, boxHeight));
        }

        return fragments;
    }
}
