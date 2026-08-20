using MaritimeVision.Core.Abstractions;
using OpenCvSharp;

namespace MaritimeVision.Core.Video;

/// <summary>Escribe los fotogramas anotados en un fichero de video.</summary>
public sealed class VideoFileSink : IVideoSink
{
    private readonly VideoWriter _writer;
    private bool _disposed;

    private VideoFileSink(VideoWriter writer, string path)
    {
        _writer = writer;
        Path = path;
    }

    /// <summary>Ruta del fichero que se esta escribiendo.</summary>
    public string Path { get; }

    /// <summary>Fotogramas escritos hasta el momento.</summary>
    public int FramesWritten { get; private set; }

    /// <summary>
    /// Crea el fichero de salida.
    /// </summary>
    /// <param name="path">Ruta destino. Se crea el directorio si no existe.</param>
    /// <param name="width">Ancho de los fotogramas.</param>
    /// <param name="height">Alto de los fotogramas.</param>
    /// <param name="fps">Tasa de fotogramas.</param>
    /// <param name="fourcc">Codec FOURCC; por defecto <c>mp4v</c>, el mas portable.</param>
    /// <exception cref="InvalidOperationException">Si el codec no esta disponible.</exception>
    public static VideoFileSink Create(string path, int width, int height, double fps, string fourcc = "mp4v")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (fourcc.Length != 4)
        {
            throw new ArgumentException("El codigo FOURCC debe tener exactamente 4 caracteres.", nameof(fourcc));
        }

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var code = VideoWriter.FourCC(fourcc[0], fourcc[1], fourcc[2], fourcc[3]);
        var writer = new VideoWriter(path, code, fps <= 0d ? 25d : fps, new Size(width, height));

        if (!writer.IsOpened())
        {
            writer.Dispose();
            throw new InvalidOperationException(
                $"No se pudo crear '{path}' con el codec '{fourcc}'. " +
                "Prueba con otro codec (por ejemplo 'avc1', 'XVID' o 'MJPG') o con otra extension.");
        }

        return new VideoFileSink(writer, path);
    }

    /// <inheritdoc />
    public bool Write(Mat frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _writer.Write(frame);
        FramesWritten++;
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
        _writer.Release();
        _writer.Dispose();
    }
}
