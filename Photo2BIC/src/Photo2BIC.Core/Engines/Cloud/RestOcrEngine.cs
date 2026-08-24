using System.Diagnostics;
using System.Net;
using System.Text.Json;
using OpenCvSharp;
using Photo2BIC.Core.Imaging;
using Video2BIC.Core.Text;

namespace Photo2BIC.Core.Engines.Cloud;

/// <summary>
/// Base de los motores que delegan el reconocimiento en un servicio remoto.
/// </summary>
/// <remarks>
/// <para>
/// Todos siguen el mismo guion: codificar la fotografia, mandarla, interpretar
/// la respuesta y devolver trozos de texto con sus posiciones. Lo que cambia es
/// la forma de autenticarse y el formato del JSON, y eso es lo unico que
/// implementa cada motor concreto.
/// </para>
/// <para>
/// El <c>HttpMessageHandler</c> se puede inyectar. Es lo que permite probar el
/// analisis de cada respuesta con cuerpos JSON reales y sin credenciales ni red,
/// que es donde estan casi todos los errores de una integracion de este tipo.
/// </para>
/// <para>
/// Estos motores declaran <see cref="MaxVariants"/> igual a 1: se factura por
/// llamada y ademas ya hacen su propio preprocesado, asi que mandarles la misma
/// fotografia binarizada de cuatro maneras seria pagar cuatro veces por un
/// resultado peor.
/// </para>
/// </remarks>
public abstract class RestOcrEngine : IPhotoOcrEngine
{
    private readonly HttpClient _http;
    private bool _disposed;

    /// <param name="handler">
    /// Transporte HTTP; <c>null</c> crea uno propio. Inyectarlo es la via de
    /// prueba.
    /// </param>
    /// <param name="timeout">Tiempo maximo por llamada.</param>
    protected RestOcrEngine(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        // El cliente es siempre nuestro y se libera con el motor. El transporte
        // inyectado no: lo creo el llamante -una prueba- y lo libera el.
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(30d);
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Family { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public OcrEngineKind Kind => OcrEngineKind.Cloud;

    /// <inheritdoc />
    public virtual int MaxVariants => 1;

    /// <summary>Reintentos ante un fallo transitorio (429 o 5xx).</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Espera inicial entre reintentos; se duplica en cada uno.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1d);

    /// <summary>Calidad JPEG con la que se envia la fotografia.</summary>
    public int JpegQuality { get; set; } = 92;

    /// <summary>Cliente HTTP compartido por las llamadas del motor.</summary>
    protected HttpClient Http => _http;

    /// <inheritdoc />
    public abstract bool IsAvailable(out string reason);

    /// <summary>
    /// Llama al servicio y traduce su respuesta a trozos de texto.
    /// </summary>
    /// <param name="jpeg">Fotografia ya codificada.</param>
    /// <param name="width">Ancho en pixeles, para desnormalizar coordenadas relativas.</param>
    /// <param name="height">Alto en pixeles.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    protected abstract Task<IReadOnlyList<TextFragment>> CallAsync(
        byte[] jpeg, int width, int height, CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<OcrEngineResult> RecognizeAsync(
        Mat photo, string variant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsAvailable(out var reason))
        {
            return OcrEngineResult.Unavailable(this, variant, reason);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var jpeg = PhotoLoader.EncodeJpeg(photo, JpegQuality);
            var fragments = await CallAsync(jpeg, photo.Width, photo.Height, cancellationToken)
                .ConfigureAwait(false);

            return OcrEngineResult.Success(this, variant, fragments, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or InvalidOperationException or OpenCVException)
        {
            // Un servicio caido, sin cuota o mal configurado resta una evidencia; el
            // resto de motores sigue su curso.
            return OcrEngineResult.Failed(this, variant, Describe(ex), stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Envia la peticion reintentando los fallos transitorios.
    /// </summary>
    /// <param name="request">
    /// Fabrica de peticiones: se llama una vez por intento, porque un
    /// <c>HttpRequestMessage</c> no se puede reenviar.
    /// </param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El cuerpo de la respuesta.</returns>
    /// <exception cref="HttpRequestException">Si la respuesta no es correcta.</exception>
    protected async Task<string> SendAsync(
        Func<HttpRequestMessage> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var delay = RetryDelay;

        for (var attempt = 0; ; attempt++)
        {
            using var message = request();
            using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            if (attempt >= MaxRetries || !IsTransient(response.StatusCode))
            {
                throw new HttpRequestException(
                    $"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body)}",
                    inner: null,
                    response.StatusCode);
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay *= 2;
        }
    }

    /// <summary>
    /// Fallos que merecen otro intento: limite de peticiones por segundo o
    /// indisponibilidad momentanea del servicio. Un 401 o un 400 no se reintentan,
    /// porque el segundo intento fallaria igual.
    /// </summary>
    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "el servicio no respondio a tiempo",
        _ => ex.Message,
    };

    private static string Truncate(string body)
        => body.Length <= 400 ? body.Trim() : string.Concat(body.AsSpan(0, 400).Trim(), "...");

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Libera el cliente HTTP.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing)
        {
            _http.Dispose();
        }
    }
}
