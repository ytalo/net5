using OpenCvSharp;

namespace Photo2BIC.Core.Imaging;

/// <summary>
/// Dibuja la puerta de un contenedor con su codigo pintado encima.
/// </summary>
/// <remarks>
/// <para>
/// Existe por un problema practico: las fotografias de contenedores no se pueden
/// versionar -pesan, y las de una terminal real tienen dueno- asi que sin esto la
/// aplicacion no se puede probar el primer dia. Con <c>photo2bic sample</c> se
/// genera una escena con un codigo conocido y se comprueba el recorrido completo
/// sin conseguir ninguna imagen.
/// </para>
/// <para>
/// Incluye el corrugado del panel a proposito. Las crestas verticales son la
/// principal fuente de falsos positivos al buscar texto, y una escena de prueba
/// sin ellas daria una impresion falsa de lo facil que es el problema. Lo que no
/// reproduce es lo que de verdad hace dificil una fotografia real: la suciedad,
/// el reflejo del sol, la pintura descascarillada y el angulo. Sirve para
/// comprobar que la aplicacion funciona, no para medir lo bien que lee.
/// </para>
/// </remarks>
public static class SyntheticDoor
{
    private const HersheyFonts Font = HersheyFonts.HersheySimplex;

    /// <summary>
    /// Dibuja la escena.
    /// </summary>
    /// <param name="code">Codigo BIC a pintar.</param>
    /// <param name="sizeType">Codigo de tamano y tipo, o <c>null</c> para omitirlo.</param>
    /// <param name="width">Ancho de la imagen.</param>
    /// <param name="height">Alto de la imagen.</param>
    /// <param name="lightTextOnDarkPanel">
    /// Codigo en blanco sobre panel oscuro, que es como va pintado mas o menos la
    /// mitad de las veces.
    /// </param>
    /// <returns>Un <see cref="Mat"/> BGR, propiedad del llamante.</returns>
    public static Mat Draw(
        string code,
        string? sizeType = "22G1",
        int width = 900,
        int height = 600,
        bool lightTextOnDarkPanel = true)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (width < 64 || height < 64)
        {
            throw new ArgumentException("La escena necesita al menos 64x64 pixeles.", nameof(width));
        }

        var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(96, 104, 112));

        try
        {
            var panel = new Rect(
                (int)(width * 0.08d), (int)(height * 0.15d), (int)(width * 0.84d), (int)(height * 0.7d));

            var body = lightTextOnDarkPanel ? new Scalar(52, 74, 168) : new Scalar(226, 228, 232);
            var ink = lightTextOnDarkPanel ? new Scalar(245, 245, 245) : new Scalar(28, 30, 34);

            Cv2.Rectangle(image, panel, body, -1);
            Cv2.Rectangle(image, panel, Shade(body, 0.6d), 3);

            for (var x = panel.X + 10; x < panel.Right - 10; x += 18)
            {
                Cv2.Line(image, new Point(x, panel.Y + 6), new Point(x, panel.Bottom - 6), Shade(body, 0.8d), 2);
            }

            if (code.Length > 0)
            {
                DrawFitted(image, code, panel, 0.78d, 0.2d, 0.18d, ink);
            }

            if (!string.IsNullOrEmpty(sizeType))
            {
                DrawFitted(image, sizeType, panel, 0.3d, 0.14d, 0.6d, ink);
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>Dibuja la escena y la escribe en disco.</summary>
    /// <param name="path">Ruta del fichero; la extension decide el formato.</param>
    /// <param name="code">Codigo BIC a pintar.</param>
    /// <param name="sizeType">Codigo de tamano y tipo, o <c>null</c> para omitirlo.</param>
    /// <param name="width">Ancho de la imagen.</param>
    /// <param name="height">Alto de la imagen.</param>
    /// <param name="lightTextOnDarkPanel">Codigo claro sobre panel oscuro.</param>
    /// <returns>La ruta escrita.</returns>
    /// <exception cref="InvalidOperationException">Si no se pudo escribir el fichero.</exception>
    public static string Write(
        string path,
        string code,
        string? sizeType = "22G1",
        int width = 900,
        int height = 600,
        bool lightTextOnDarkPanel = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var image = Draw(code, sizeType, width, height, lightTextOnDarkPanel);

        if (!Cv2.ImWrite(path, image))
        {
            throw new InvalidOperationException(
                $"No se pudo escribir '{path}'. Usa una extension de imagen conocida (.jpg, .png).");
        }

        return path;
    }

    /// <summary>Oscurece un color, para el canto del panel y las crestas del corrugado.</summary>
    private static Scalar Shade(Scalar color, double factor)
        => new(color.Val0 * factor, color.Val1 * factor, color.Val2 * factor);

    /// <summary>
    /// Escribe un texto escalado para ocupar la fraccion indicada del panel, con su
    /// linea base a la altura relativa indicada.
    /// </summary>
    private static void DrawFitted(
        Mat image,
        string text,
        Rect panel,
        double widthFraction,
        double heightFraction,
        double topFraction,
        Scalar color)
    {
        var reference = Cv2.GetTextSize(text, Font, 1d, 2, out _);

        var scale = Math.Min(
            panel.Width * widthFraction / reference.Width,
            panel.Height * heightFraction / reference.Height);

        var thickness = Math.Max(2, (int)Math.Round(scale * 2.5d));
        var size = Cv2.GetTextSize(text, Font, scale, thickness, out _);

        var origin = new Point(
            panel.X + ((panel.Width - size.Width) / 2),
            panel.Y + (int)(panel.Height * topFraction) + size.Height);

        Cv2.PutText(image, text, origin, Font, scale, color, thickness, LineTypes.AntiAlias);
    }
}
