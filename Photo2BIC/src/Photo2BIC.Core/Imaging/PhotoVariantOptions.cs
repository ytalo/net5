namespace Photo2BIC.Core.Imaging;

/// <summary>Configuracion de <see cref="PhotoVariantFactory"/>.</summary>
public sealed class PhotoVariantOptions
{
    /// <summary>Igualar el contraste por bloques (CLAHE).</summary>
    public bool Clahe { get; set; } = true;

    /// <summary>Binarizar con umbral global de Otsu.</summary>
    public bool Otsu { get; set; } = true;

    /// <summary>Binarizar con umbral adaptativo por vecindario.</summary>
    public bool Adaptive { get; set; } = true;

    /// <summary>Invertir la polaridad, para el codigo pintado en blanco sobre panel oscuro.</summary>
    public bool Inverted { get; set; } = true;

    /// <summary>Realzar los bordes con una mascara de enfoque.</summary>
    public bool Sharpen { get; set; } = true;

    /// <summary>
    /// Ampliar la fotografia al doble cuando es pequena, hasta
    /// <see cref="UpscaleBelowWidth"/> pixeles de ancho.
    /// </summary>
    public bool Upscale { get; set; } = true;

    /// <summary>Ancho por debajo del cual se genera la variante ampliada.</summary>
    public int UpscaleBelowWidth { get; set; } = 1400;

    /// <summary>Limite de clip de CLAHE. Mas alto realza mas y tambien mas ruido.</summary>
    public double ClaheClipLimit { get; set; } = 3d;

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun valor es imposible.</exception>
    public void Validate()
    {
        if (UpscaleBelowWidth < 0)
        {
            throw new InvalidOperationException("UpscaleBelowWidth no puede ser negativo.");
        }

        if (ClaheClipLimit <= 0d)
        {
            throw new InvalidOperationException("ClaheClipLimit debe ser positivo.");
        }
    }

    /// <summary>Solo la fotografia original, sin variantes.</summary>
    public static PhotoVariantOptions OriginalOnly() => new()
    {
        Clahe = false,
        Otsu = false,
        Adaptive = false,
        Inverted = false,
        Sharpen = false,
        Upscale = false,
    };
}
