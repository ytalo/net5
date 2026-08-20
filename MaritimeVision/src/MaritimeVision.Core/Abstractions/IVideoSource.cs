using OpenCvSharp;

namespace MaritimeVision.Core.Abstractions;

/// <summary>Metadatos de la secuencia de video que se esta leyendo.</summary>
/// <param name="Width">Ancho del fotograma en pixeles.</param>
/// <param name="Height">Alto del fotograma en pixeles.</param>
/// <param name="Fps">Fotogramas por segundo declarados por el contenedor.</param>
/// <param name="FrameCount">Total de fotogramas, o <c>null</c> si es un flujo en vivo.</param>
/// <param name="IsLive">Indica si la fuente es un flujo en vivo (RTSP, camara).</param>
public sealed record VideoStreamInfo(int Width, int Height, double Fps, long? FrameCount, bool IsLive)
{
    /// <summary>Duracion estimada; <c>null</c> para flujos en vivo.</summary>
    public TimeSpan? Duration => FrameCount is > 0 && Fps > 0
        ? TimeSpan.FromSeconds(FrameCount.Value / Fps)
        : null;
}

/// <summary>
/// Fuente de fotogramas: un fichero, una URL RTSP/HTTP o una camara conectada.
/// </summary>
public interface IVideoSource : IDisposable
{
    /// <summary>Metadatos de la secuencia.</summary>
    VideoStreamInfo Info { get; }

    /// <summary>
    /// Lee el siguiente fotograma en <paramref name="frame"/>.
    /// Devuelve <c>false</c> cuando la secuencia termina.
    /// </summary>
    /// <remarks>
    /// El <see cref="Mat"/> es propiedad del llamante y se reutiliza entre
    /// iteraciones para no reservar memoria en cada fotograma.
    /// </remarks>
    bool TryReadFrame(Mat frame);
}
