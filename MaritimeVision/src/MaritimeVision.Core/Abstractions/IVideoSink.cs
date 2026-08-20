using OpenCvSharp;

namespace MaritimeVision.Core.Abstractions;

/// <summary>
/// Destino de los fotogramas ya anotados: un fichero de video, una ventana de
/// reproduccion o un flujo MJPEG hacia el navegador.
/// </summary>
public interface IVideoSink : IDisposable
{
    /// <summary>
    /// Consume un fotograma anotado. Devuelve <c>false</c> para pedir al pipeline
    /// que se detenga (por ejemplo, si el usuario cierra la ventana).
    /// </summary>
    bool Write(Mat frame);
}
