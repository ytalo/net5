using MaritimeVision.Core.Abstractions;
using OpenCvSharp;

namespace MaritimeVision.Core.Video;

/// <summary>
/// Reproduce los fotogramas anotados en una ventana de escritorio.
/// </summary>
/// <remarks>
/// HighGui necesita un entorno grafico (GTK en Linux, Cocoa en macOS). En un
/// servidor o contenedor sin display, <see cref="TryCreate"/> devuelve
/// <c>false</c> en lugar de propagar la excepcion, para que quien lo use pueda
/// recurrir al visor web o a la exportacion a fichero.
/// </remarks>
public sealed class WindowSink : IVideoSink
{
    private readonly string _title;
    private readonly int _frameDelayMs;
    private bool _disposed;
    private bool _paused;

    private WindowSink(string title, double fps)
    {
        _title = title;
        _frameDelayMs = Math.Max(1, (int)Math.Round(1000d / (fps <= 0d ? 25d : fps)));
    }

    /// <summary><c>true</c> si el usuario pidio salir (tecla <c>q</c> o <c>Esc</c>).</summary>
    public bool QuitRequested { get; private set; }

    /// <summary>
    /// Intenta abrir la ventana de reproduccion.
    /// </summary>
    /// <returns><c>false</c> si el entorno no tiene interfaz grafica.</returns>
    public static bool TryCreate(string title, double fps, out WindowSink? sink, out string? error)
    {
        try
        {
            Cv2.NamedWindow(title, WindowFlags.Normal | WindowFlags.KeepRatio);
            sink = new WindowSink(title, fps);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            sink = null;
            error = ex.Message.Split('\n')[0];
            return false;
        }
    }

    /// <inheritdoc />
    public bool Write(Mat frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Cv2.ImShow(_title, frame);

        do
        {
            var key = Cv2.WaitKey(_paused ? 50 : _frameDelayMs);
            switch (key)
            {
                case 'q':
                case 'Q':
                case 27: // Esc
                    QuitRequested = true;
                    return false;
                case ' ':
                    _paused = !_paused;
                    break;
            }
        }
        while (_paused && !QuitRequested);

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            Cv2.DestroyWindow(_title);
        }
        catch (OpenCVException)
        {
            // La ventana ya podia estar cerrada por el usuario.
        }
    }
}
