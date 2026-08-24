using System.Net;
using System.Text;
using OpenCvSharp;
using Photo2BIC.Core.Engines;
using Video2BIC.Core.Text;

namespace Photo2BIC.Tests;

/// <summary>
/// Motor simulado que devuelve respuestas fijadas de antemano.
/// </summary>
/// <remarks>
/// Es lo que permite probar el consenso y el pipeline con motores de calidad
/// controlada -incluido uno que siempre se equivoca, o uno que se cae- sin
/// depender de que haya un OCR instalado ni de que acierte.
/// </remarks>
public sealed class ScriptedEngine(
    string name,
    string family,
    IReadOnlyList<string> responses,
    float confidence = 0.9f) : IPhotoOcrEngine
{
    private int _call;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string Family { get; } = family;

    /// <inheritdoc />
    public string Description => $"Motor simulado '{Name}'";

    /// <inheritdoc />
    public OcrEngineKind Kind { get; init; } = OcrEngineKind.Local;

    /// <inheritdoc />
    public int MaxVariants { get; init; } = int.MaxValue;

    /// <summary>Motivo por el que no esta disponible; <c>null</c> si lo esta.</summary>
    public string? UnavailableReason { get; init; }

    /// <summary>Lanzar en vez de responder, para probar que un motor roto no tumba el analisis.</summary>
    public bool Throws { get; init; }

    /// <summary>Llamadas recibidas.</summary>
    public int Calls => _call;

    /// <summary>Variantes sobre las que se le llamo, en orden.</summary>
    public List<string> Variants { get; } = [];

    /// <inheritdoc />
    public bool IsAvailable(out string reason)
    {
        reason = UnavailableReason ?? "simulado";
        return UnavailableReason is null;
    }

    /// <inheritdoc />
    public Task<OcrEngineResult> RecognizeAsync(
        Mat photo, string variant, CancellationToken cancellationToken = default)
    {
        Variants.Add(variant);

        if (Throws)
        {
            return Task.FromResult(OcrEngineResult.Failed(this, variant, "fallo simulado", TimeSpan.Zero));
        }

        var response = responses.Count == 0
            ? string.Empty
            : responses[Math.Min(_call, responses.Count - 1)];

        _call++;

        IReadOnlyList<TextFragment> fragments = response.Length == 0
            ? []
            : [new TextFragment(response, confidence, 0, 0, photo.Width, photo.Height)];

        return Task.FromResult(OcrEngineResult.Success(this, variant, fragments, TimeSpan.FromMilliseconds(1d)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>
/// Transporte HTTP simulado: responde con cuerpos fijados y anota las peticiones.
/// </summary>
/// <remarks>
/// Los motores remotos son, en su mayor parte, codigo que interpreta JSON ajeno.
/// Probar ese codigo contra respuestas reales es donde esta casi todo el valor, y
/// no requiere ni credenciales ni red.
/// </remarks>
public sealed class StubHandler(params StubHandler.Reply[] replies) : HttpMessageHandler
{
    private int _call;

    /// <summary>Una respuesta programada.</summary>
    /// <param name="Status">Codigo de estado.</param>
    /// <param name="Body">Cuerpo.</param>
    public readonly record struct Reply(HttpStatusCode Status, string Body)
    {
        /// <summary>Respuesta correcta con el cuerpo indicado.</summary>
        public static Reply Ok(string body) => new(HttpStatusCode.OK, body);

        /// <summary>Respuesta de error.</summary>
        public static Reply Error(HttpStatusCode status, string body = "") => new(status, body);
    }

    /// <summary>Peticiones recibidas, en orden.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Cuerpos recibidos, en orden.</summary>
    public List<string> Bodies { get; } = [];

    /// <summary>Llamadas recibidas.</summary>
    public int Calls => _call;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        Bodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var reply = replies.Length == 0
            ? Reply.Ok("{}")
            : replies[Math.Min(_call, replies.Length - 1)];

        _call++;

        return new HttpResponseMessage(reply.Status)
        {
            Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
        };
    }
}
