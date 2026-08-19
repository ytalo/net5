using OpenCvSharp;

namespace MaritimeVision.Core.Rendering;

/// <summary>Colores estables y bien contrastados para cajas y etiquetas.</summary>
public static class Palette
{
    /// <summary>
    /// Paleta de 20 tonos en BGR (el orden de canales de OpenCV), elegidos para
    /// distinguirse entre si y del gris del asfalto y el azul del mar.
    /// </summary>
    private static readonly Scalar[] Colors =
    [
        new(56, 56, 255), new(151, 157, 255), new(31, 112, 255), new(29, 178, 255),
        new(49, 210, 207), new(10, 249, 72), new(23, 204, 146), new(134, 219, 61),
        new(52, 147, 26), new(187, 212, 0), new(168, 153, 44), new(255, 194, 0),
        new(147, 69, 52), new(255, 115, 100), new(236, 24, 0), new(255, 56, 132),
        new(133, 0, 82), new(255, 56, 203), new(200, 149, 255), new(199, 55, 255),
    ];

    /// <summary>Color estable asociado a un indice.</summary>
    public static Scalar ForIndex(int index)
        => Colors[Math.Abs(index) % Colors.Length];

    /// <summary>
    /// Color de texto legible sobre <paramref name="background"/>, elegido segun
    /// la luminancia percibida del fondo.
    /// </summary>
    public static Scalar TextOn(Scalar background)
    {
        var luminance = (0.114d * background.Val0) + (0.587d * background.Val1) + (0.299d * background.Val2);
        return luminance > 140d ? new Scalar(0, 0, 0) : new Scalar(255, 255, 255);
    }
}
