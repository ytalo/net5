using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Models;
using OpenCvSharp;
using Video2BIC.Core.Ocr;
using Video2BIC.Core.Text;

namespace Video2BIC.Tests;

/// <summary>
/// Genera escenas sinteticas de contenedor: un panel de color con su codigo BIC
/// pintado encima, sobre un fondo de muelle.
/// </summary>
/// <remarks>
/// Permite ejercitar el recorrido completo (video, deteccion, seguimiento,
/// localizacion de texto, OCR y votacion) sin depender de metraje real ni de un
/// modelo entrenado.
/// </remarks>
public static class SyntheticContainer
{
    private const HersheyFonts Font = HersheyFonts.HersheySimplex;

    /// <summary>Dibuja un contenedor con su codigo dentro del rectangulo indicado.</summary>
    /// <param name="image">Imagen sobre la que dibujar.</param>
    /// <param name="box">Rectangulo del contenedor.</param>
    /// <param name="code">Codigo BIC a pintar.</param>
    /// <param name="sizeType">Codigo de tamano y tipo, o <c>null</c> para omitirlo.</param>
    /// <param name="body">Color del panel.</param>
    public static void Draw(
        Mat image,
        Rect box,
        string code,
        string? sizeType = "22G1",
        Scalar? body = null)
    {
        var panel = body ?? new Scalar(52, 74, 168);
        Cv2.Rectangle(image, box, panel, -1);
        Cv2.Rectangle(image, box, new Scalar(30, 40, 90), 2);

        // Corrugado: las crestas verticales son la principal fuente de falsos
        // positivos al buscar texto, asi que la escena de prueba las incluye.
        for (var x = box.X + 8; x < box.Right - 8; x += 14)
        {
            Cv2.Line(image, new Point(x, box.Y + 4), new Point(x, box.Bottom - 4), new Scalar(40, 58, 140), 2);
        }

        var white = new Scalar(245, 245, 245);

        // El codigo se dimensiona para caber en el panel, como en un contenedor real:
        // ocupa el 70 % del ancho y una cuarta parte del alto.
        if (!string.IsNullOrEmpty(code))
        {
            DrawFitted(image, code, box, 0.7d, 0.25d, 0.25d, white);
        }

        if (!string.IsNullOrEmpty(sizeType))
        {
            DrawFitted(image, sizeType, box, 0.25d, 0.18d, 0.65d, white);
        }
    }

    /// <summary>
    /// Escribe un texto escalado para ocupar la fraccion indicada del panel, con su
    /// linea base a la altura relativa indicada.
    /// </summary>
    private static void DrawFitted(
        Mat image,
        string text,
        Rect box,
        double widthFraction,
        double heightFraction,
        double topFraction,
        Scalar color)
    {
        var reference = Cv2.GetTextSize(text, Font, 1d, 2, out _);

        var scale = Math.Min(
            box.Width * widthFraction / reference.Width,
            box.Height * heightFraction / reference.Height);

        var thickness = Math.Max(1, (int)Math.Round(scale * 2.5d));
        var size = Cv2.GetTextSize(text, Font, scale, thickness, out _);

        var origin = new Point(
            box.X + (int)(box.Width * 0.05d),
            box.Y + (int)(box.Height * topFraction) + size.Height);

        Cv2.PutText(image, text, origin, Font, scale, color, thickness, LineTypes.AntiAlias);
    }

    /// <summary>Crea una escena completa con un contenedor.</summary>
    public static Mat Scene(int width, int height, Rect box, string code, string? sizeType = "22G1")
    {
        var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(96, 104, 112));
        Draw(image, box, code, sizeType);
        return image;
    }

    /// <summary>
    /// Escribe un video en el que un contenedor cruza la escena de izquierda a
    /// derecha, y devuelve su ruta.
    /// </summary>
    public static string WriteVideo(
        string path,
        string code,
        int frames = 40,
        int width = 800,
        int height = 450,
        double fps = 25d,
        string? sizeType = "22G1")
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
            using var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(96, 104, 112));
            Draw(image, BoxAt(frame), code, sizeType);
            writer.Write(image);
        }

        return path;
    }

    /// <summary>Caja verdadera del contenedor en el fotograma indicado.</summary>
    public static Rect BoxAt(int frame) => new(40 + (frame * 6), 150, 320, 160);

    /// <summary>Caja verdadera como <see cref="BoundingBox"/>.</summary>
    public static BoundingBox BoundingBoxAt(int frame)
    {
        var box = BoxAt(frame);
        return new BoundingBox(box.X, box.Y, box.Width, box.Height);
    }
}

/// <summary>
/// Detector simulado que devuelve la caja verdadera del contenedor sintetico.
/// </summary>
public sealed class ScriptedContainerDetector(string label = "container", float score = 0.9f) : IObjectDetector
{
    private int _frame;

    /// <inheritdoc />
    public IReadOnlyList<string> Labels => [label];

    /// <inheritdoc />
    public string Description => "Detector simulado sobre contenedor sintetico";

    /// <summary>Fotogramas procesados.</summary>
    public int FramesSeen => _frame;

    /// <inheritdoc />
    public IReadOnlyList<Detection> Detect(Mat frame)
        => [new Detection(SyntheticContainer.BoundingBoxAt(_frame++), score, 0, label)];

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>
/// Reconocedor de texto simulado: devuelve una secuencia de respuestas fijada de
/// antemano, incluidas las equivocadas.
/// </summary>
/// <remarks>
/// Es lo que permite probar la votacion y el pipeline con un OCR de calidad
/// controlada, sin depender de que Tesseract acierte.
/// </remarks>
public sealed class ScriptedTextRecognizer(IReadOnlyList<string> responses, float confidence = 0.9f)
    : ITextRecognizer
{
    private int _call;

    /// <inheritdoc />
    public string Description => "Reconocedor simulado";

    /// <inheritdoc />
    public bool PerformsLayoutAnalysis => true;

    /// <summary>Llamadas recibidas.</summary>
    public int Calls => _call;

    /// <inheritdoc />
    public IReadOnlyList<TextFragment> Recognize(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var response = responses[Math.Min(_call++, responses.Count - 1)];
        return response.Length == 0
            ? Array.Empty<TextFragment>()
            : [new TextFragment(response, confidence, 0, 0, image.Width, image.Height)];
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
