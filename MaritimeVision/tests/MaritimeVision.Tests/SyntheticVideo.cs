using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Models;
using OpenCvSharp;

namespace MaritimeVision.Tests;

/// <summary>Un objeto sintetico que se desplaza en linea recta por la escena.</summary>
/// <param name="ClassId">Clase que emitira el detector simulado.</param>
/// <param name="Label">Etiqueta legible.</param>
/// <param name="StartX">Posicion horizontal inicial.</param>
/// <param name="Y">Posicion vertical, constante.</param>
/// <param name="SpeedX">Desplazamiento horizontal por fotograma.</param>
/// <param name="Width">Ancho de la caja.</param>
/// <param name="Height">Alto de la caja.</param>
public sealed record SyntheticObject(
    int ClassId,
    string Label,
    float StartX,
    float Y,
    float SpeedX,
    float Width,
    float Height)
{
    /// <summary>Caja verdadera en el fotograma indicado.</summary>
    public BoundingBox BoxAt(int frame)
        => new(StartX + (SpeedX * frame), Y, Width, Height);
}

/// <summary>
/// Genera un video sintetico con rectangulos en movimiento y produce la verdad
/// de referencia correspondiente.
/// </summary>
/// <remarks>
/// Permite ejercitar el pipeline completo (lectura, seguimiento, dibujo,
/// escritura) sin depender de un modelo entrenado ni de metraje real.
/// </remarks>
public static class SyntheticVideo
{
    /// <summary>Escribe un video con los objetos indicados y devuelve su ruta.</summary>
    public static string Write(
        string path,
        IReadOnlyList<SyntheticObject> objects,
        int frames,
        int width = 640,
        int height = 360,
        double fps = 25d)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var fourcc = VideoWriter.FourCC('m', 'p', '4', 'v');
        using var writer = new VideoWriter(path, fourcc, fps, new Size(width, height));
        if (!writer.IsOpened())
        {
            throw new InvalidOperationException($"No se pudo crear el video sintetico '{path}'.");
        }

        for (var frame = 0; frame < frames; frame++)
        {
            using var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(60, 45, 35));

            for (var index = 0; index < objects.Count; index++)
            {
                var box = objects[index].BoxAt(frame);
                var rect = new Rect((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height);
                var color = new Scalar(40 + (index * 60) % 200, 120, 200 - ((index * 50) % 150));
                Cv2.Rectangle(image, rect, color, -1);
            }

            writer.Write(image);
        }

        return path;
    }
}

/// <summary>
/// Detector simulado que devuelve la verdad de referencia del video sintetico,
/// con la posibilidad de omitir fotogramas o rebajar la confianza.
/// </summary>
public sealed class ScriptedDetector(
    IReadOnlyList<SyntheticObject> objects,
    Func<int, int, float>? scoreForFrame = null) : IObjectDetector
{
    private int _frame;

    /// <inheritdoc />
    public IReadOnlyList<string> Labels => objects.Select(o => o.Label).Distinct().ToList();

    /// <inheritdoc />
    public string Description => "Detector simulado sobre video sintetico";

    /// <summary>Fotogramas procesados.</summary>
    public int FramesSeen => _frame;

    /// <inheritdoc />
    public IReadOnlyList<Detection> Detect(Mat frame)
    {
        var detections = new List<Detection>();

        for (var index = 0; index < objects.Count; index++)
        {
            // Un score de 0 significa "el detector no vio nada": asi se simula una
            // oclusion total sin tocar el pipeline.
            var score = scoreForFrame?.Invoke(_frame, index) ?? 0.9f;
            if (score <= 0f)
            {
                continue;
            }

            var target = objects[index];
            detections.Add(new Detection(target.BoxAt(_frame), score, target.ClassId, target.Label));
        }

        _frame++;
        return detections;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
