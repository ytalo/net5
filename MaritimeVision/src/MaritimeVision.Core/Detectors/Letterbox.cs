using MaritimeVision.Core.Models;
using OpenCvSharp;

namespace MaritimeVision.Core.Detectors;

/// <summary>
/// Redimensionado con relacion de aspecto preservada y relleno simetrico, tal y
/// como lo hace el preprocesado de YOLO.
/// </summary>
/// <remarks>
/// Deformar la imagen para encajarla en un cuadrado degrada notablemente la
/// precision: un contenedor de 40 pies dejaria de tener la proporcion con la que
/// se entreno el modelo. En su lugar se escala por el factor menor y se rellena
/// el resto con gris neutro.
/// </remarks>
/// <param name="Scale">Factor aplicado a la imagen original.</param>
/// <param name="PadX">Relleno horizontal anadido a la izquierda.</param>
/// <param name="PadY">Relleno vertical anadido arriba.</param>
/// <param name="SourceWidth">Ancho de la imagen original.</param>
/// <param name="SourceHeight">Alto de la imagen original.</param>
public readonly record struct Letterbox(
    float Scale,
    float PadX,
    float PadY,
    int SourceWidth,
    int SourceHeight)
{
    /// <summary>Color de relleno estandar de YOLO.</summary>
    public static readonly Scalar PadColor = new(114, 114, 114);

    /// <summary>
    /// Calcula la transformacion para llevar una imagen de
    /// <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> a un lienzo
    /// de <paramref name="targetWidth"/> x <paramref name="targetHeight"/>.
    /// </summary>
    public static Letterbox Compute(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var scale = Math.Min((float)targetWidth / sourceWidth, (float)targetHeight / sourceHeight);
        var scaledWidth = (int)Math.Round(sourceWidth * scale);
        var scaledHeight = (int)Math.Round(sourceHeight * scale);
        var padX = (targetWidth - scaledWidth) * 0.5f;
        var padY = (targetHeight - scaledHeight) * 0.5f;

        return new Letterbox(scale, padX, padY, sourceWidth, sourceHeight);
    }

    /// <summary>Aplica la transformacion y devuelve el lienzo resultante.</summary>
    /// <remarks>El <see cref="Mat"/> devuelto es propiedad del llamante.</remarks>
    public Mat Apply(Mat source, int targetWidth, int targetHeight)
    {
        var scaledWidth = (int)Math.Round(SourceWidth * Scale);
        var scaledHeight = (int)Math.Round(SourceHeight * Scale);

        var canvas = new Mat(targetHeight, targetWidth, source.Type(), PadColor);
        try
        {
            using var resized = new Mat();
            // INTER_AREA al reducir y INTER_LINEAR al ampliar: es lo que hace el
            // preprocesado de referencia y evita el aliasing en objetos lejanos.
            var interpolation = Scale < 1f ? InterpolationFlags.Area : InterpolationFlags.Linear;
            Cv2.Resize(source, resized, new Size(scaledWidth, scaledHeight), 0, 0, interpolation);

            var target = new Rect((int)Math.Round(PadX), (int)Math.Round(PadY), scaledWidth, scaledHeight);
            using var region = new Mat(canvas, target);
            resized.CopyTo(region);
        }
        catch
        {
            canvas.Dispose();
            throw;
        }

        return canvas;
    }

    /// <summary>
    /// Lleva una caja del espacio del modelo de vuelta al de la imagen original,
    /// deshaciendo relleno y escalado, y recortando al marco.
    /// </summary>
    public BoundingBox Invert(in BoundingBox box)
    {
        if (Scale <= 0f)
        {
            return box;
        }

        var left = (box.Left - PadX) / Scale;
        var top = (box.Top - PadY) / Scale;
        var right = (box.Right - PadX) / Scale;
        var bottom = (box.Bottom - PadY) / Scale;

        return BoundingBox
            .FromCorners(left, top, right, bottom)
            .ClampTo(SourceWidth, SourceHeight);
    }
}
