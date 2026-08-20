using OpenCvSharp;

namespace MaritimeVision.Web;

/// <summary>
/// Emite fotogramas como un flujo <c>multipart/x-mixed-replace</c>.
/// </summary>
/// <remarks>
/// MJPEG es el camino mas corto para ver video anotado en un navegador: basta un
/// <c>&lt;img&gt;</c>, sin WebRTC, sin MSE y sin JavaScript de reproduccion. A
/// cambio no hay audio ni control de posicion, que aqui no hacen falta.
/// </remarks>
public sealed class MjpegWriter(Stream output, int jpegQuality)
{
    private const string Boundary = "maritimevisionframe";

    /// <summary>Cabecera <c>Content-Type</c> que debe declarar la respuesta.</summary>
    public static string ContentType => $"multipart/x-mixed-replace; boundary={Boundary}";

    private readonly ImageEncodingParam[] _encodingParams =
        [new(ImwriteFlags.JpegQuality, Math.Clamp(jpegQuality, 1, 100))];

    /// <summary>Codifica y envia un fotograma.</summary>
    public async Task WriteFrameAsync(Mat frame, CancellationToken cancellationToken)
    {
        Cv2.ImEncode(".jpg", frame, out var jpeg, _encodingParams);

        var header = System.Text.Encoding.ASCII.GetBytes(
            $"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");

        await output.WriteAsync(header, cancellationToken);
        await output.WriteAsync(jpeg, cancellationToken);
        await output.WriteAsync("\r\n"u8.ToArray(), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    /// <summary>Cierra el flujo multiparte.</summary>
    public async Task WriteEndAsync(CancellationToken cancellationToken)
    {
        await output.WriteAsync(System.Text.Encoding.ASCII.GetBytes($"--{Boundary}--\r\n"), cancellationToken);
        await output.FlushAsync(cancellationToken);
    }
}
