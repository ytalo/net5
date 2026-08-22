using MaritimeVision.Core.Models;
using OpenCvSharp;

namespace Video2BIC.Core.Imaging;

/// <summary>
/// Recorta del fotograma la zona de un contenedor detectado y la deja en un
/// tamano donde el OCR tenga algo que leer.
/// </summary>
public static class RoiExtractor
{
    /// <summary>
    /// Convierte una caja del detector en un rectangulo de pixeles valido dentro
    /// del fotograma, con un margen relativo.
    /// </summary>
    /// <param name="box">Caja en coordenadas del fotograma.</param>
    /// <param name="frameWidth">Ancho del fotograma.</param>
    /// <param name="frameHeight">Alto del fotograma.</param>
    /// <param name="margin">
    /// Margen anadido a cada lado, en proporcion al tamano de la caja. Un poco de
    /// margen evita cortar el ultimo digito cuando la caja del detector se queda
    /// justa.
    /// </param>
    public static Rect ToRect(in BoundingBox box, int frameWidth, int frameHeight, double margin = 0.03d)
    {
        var padX = box.Width * margin;
        var padY = box.Height * margin;

        var left = (int)Math.Floor(Math.Max(0d, box.Left - padX));
        var top = (int)Math.Floor(Math.Max(0d, box.Top - padY));
        var right = (int)Math.Ceiling(Math.Min(frameWidth, box.Right + padX));
        var bottom = (int)Math.Ceiling(Math.Min(frameHeight, box.Bottom + padY));

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// Extrae el recorte y, si es pequeno, lo amplia hasta
    /// <paramref name="targetHeight"/>.
    /// </summary>
    /// <remarks>
    /// Ampliar no anade informacion, pero si la necesitan tanto la busqueda de
    /// regiones (los nucleos morfologicos tienen un tamano minimo de tres pixeles)
    /// como el reconocedor, entrenado sobre lineas de 32 pixeles de alto. Un
    /// contenedor al fondo del muelle ocupa 60 pixeles y sin ampliar no se lee nada.
    /// </remarks>
    /// <param name="frame">Fotograma completo.</param>
    /// <param name="region">Rectangulo a recortar; debe caber en el fotograma.</param>
    /// <param name="targetHeight">Alto deseado del recorte, en pixeles.</param>
    /// <param name="maxScale">Ampliacion maxima, para no generar imagenes enormes.</param>
    /// <param name="scale">
    /// Factor que se aplico. Hay que conservarlo para poder devolver a coordenadas
    /// del fotograma cualquier cosa que se encuentre dentro del recorte.
    /// </param>
    /// <returns>Un <see cref="Mat"/> nuevo, propiedad del llamante.</returns>
    public static Mat Extract(Mat frame, Rect region, int targetHeight, double maxScale, out double scale)
    {
        ArgumentNullException.ThrowIfNull(frame);
        scale = 1d;

        if (region.Width <= 0 || region.Height <= 0)
        {
            return new Mat();
        }

        var clamped = region & new Rect(0, 0, frame.Width, frame.Height);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            return new Mat();
        }

        using var view = new Mat(frame, clamped);

        var requested = Math.Min(maxScale, (double)targetHeight / clamped.Height);
        if (requested <= 1.05d)
        {
            return view.Clone();
        }

        var enlarged = new Mat();
        Cv2.Resize(
            view,
            enlarged,
            new Size((int)Math.Round(clamped.Width * requested), (int)Math.Round(clamped.Height * requested)),
            interpolation: InterpolationFlags.Cubic);

        scale = requested;
        return enlarged;
    }

    /// <inheritdoc cref="Extract(Mat, Rect, int, double, out double)" />
    public static Mat Extract(Mat frame, Rect region, int targetHeight = 480, double maxScale = 4d)
        => Extract(frame, region, targetHeight, maxScale, out _);
}
