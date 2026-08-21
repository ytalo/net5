using OpenCvSharp;

namespace Video2BIC.Core.TextRegions;

/// <summary>
/// Zona de un recorte donde probablemente hay una linea de texto.
/// </summary>
/// <param name="Box">Rectangulo en coordenadas del recorte.</param>
/// <param name="Score">
/// Densidad de trazo dentro del rectangulo, en [0, 1]. Sirve para priorizar cuando
/// hay mas regiones que presupuesto de OCR.
/// </param>
public readonly record struct TextRegion(Rect Box, float Score)
{
    /// <summary>Relacion ancho/alto. Una linea de texto la tiene claramente mayor que 1.</summary>
    public double AspectRatio => Box.Height > 0 ? (double)Box.Width / Box.Height : 0d;

    /// <inheritdoc />
    public override string ToString()
        => $"[{Box.X},{Box.Y} {Box.Width}x{Box.Height}] {Score:P0}";
}

/// <summary>Localiza las lineas de texto dentro de un recorte.</summary>
public interface ITextRegionDetector
{
    /// <summary>Descripcion legible del metodo, para logs y diagnostico.</summary>
    string Description { get; }

    /// <summary>
    /// Devuelve las regiones candidatas en orden de lectura: de arriba abajo y, a
    /// igual altura, de izquierda a derecha.
    /// </summary>
    /// <param name="image">Recorte BGR o en escala de grises.</param>
    IReadOnlyList<TextRegion> Detect(Mat image);
}
