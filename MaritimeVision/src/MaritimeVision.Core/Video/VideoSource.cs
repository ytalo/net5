using MaritimeVision.Core.Abstractions;
using OpenCvSharp;

namespace MaritimeVision.Core.Video;

/// <summary>
/// Fuente de video basada en <see cref="VideoCapture"/>. Admite ficheros locales,
/// URLs RTSP/HTTP y camaras conectadas.
/// </summary>
public sealed class VideoSource : IVideoSource
{
    private readonly VideoCapture _capture;
    private bool _disposed;

    private VideoSource(VideoCapture capture, VideoStreamInfo info)
    {
        _capture = capture;
        Info = info;
    }

    /// <inheritdoc />
    public VideoStreamInfo Info { get; }

    /// <summary>
    /// Abre una secuencia de video.
    /// </summary>
    /// <param name="source">
    /// Ruta de un fichero, URL <c>rtsp://</c> / <c>http(s)://</c>, o el indice
    /// numerico de una camara conectada.
    /// </param>
    /// <exception cref="FileNotFoundException">Si la ruta apunta a un fichero inexistente.</exception>
    /// <exception cref="InvalidOperationException">Si la secuencia no se puede abrir o leer.</exception>
    public static VideoSource Open(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var isLive = false;
        VideoCapture capture;

        if (int.TryParse(source, out var deviceIndex))
        {
            capture = new VideoCapture(deviceIndex);
            isLive = true;
        }
        else if (IsStreamUrl(source))
        {
            capture = new VideoCapture(source);
            isLive = true;
        }
        else
        {
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"No se encontro el video '{source}'.", source);
            }

            capture = new VideoCapture(source);
        }

        try
        {
            if (!capture.IsOpened())
            {
                throw new InvalidOperationException(
                    $"No se pudo abrir la secuencia '{source}'. " +
                    "Comprueba el codec, la URL o los permisos de la camara.");
            }

            if (isLive)
            {
                // En vivo interesa el fotograma mas reciente, no el mas antiguo:
                // con un buffer grande el analisis se va retrasando indefinidamente.
                capture.Set(VideoCaptureProperties.BufferSize, 1);
            }

            var width = (int)capture.Get(VideoCaptureProperties.FrameWidth);
            var height = (int)capture.Get(VideoCaptureProperties.FrameHeight);
            var fps = capture.Get(VideoCaptureProperties.Fps);
            var frameCount = (long)capture.Get(VideoCaptureProperties.FrameCount);

            // Algunos contenedores y practicamente todas las camaras mienten sobre
            // estos metadatos; se usan valores por defecto sensatos.
            if (fps is <= 0d or > 480d)
            {
                fps = 25d;
            }

            if (width <= 0 || height <= 0)
            {
                using var probe = new Mat();
                if (!capture.Read(probe) || probe.Empty())
                {
                    throw new InvalidOperationException(
                        $"La secuencia '{source}' se abrio pero no devuelve fotogramas.");
                }

                width = probe.Width;
                height = probe.Height;
                capture.Set(VideoCaptureProperties.PosFrames, 0);
            }

            var info = new VideoStreamInfo(width, height, fps, isLive || frameCount <= 0 ? null : frameCount, isLive);
            return new VideoSource(capture, info);
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public bool TryReadFrame(Mat frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _capture.Read(frame) && !frame.Empty();
    }

    private static bool IsStreamUrl(string source)
        => source.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
           || source.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase)
           || source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _capture.Dispose();
    }
}
