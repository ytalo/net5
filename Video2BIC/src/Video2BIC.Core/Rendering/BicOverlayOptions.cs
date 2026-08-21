namespace Video2BIC.Core.Rendering;

/// <summary>Opciones de dibujo de <see cref="BicOverlayRenderer"/>.</summary>
public sealed class BicOverlayOptions
{
    /// <summary>Grosor del borde de la caja, en pixeles.</summary>
    public int BoxThickness { get; set; } = 2;

    /// <summary>Escala de la tipografia.</summary>
    public double FontScale { get; set; } = 0.55d;

    /// <summary>Dibujar la etiqueta con la identidad y el codigo.</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>
    /// Dibujar las zonas del recorte donde se busco texto. Es la herramienta para
    /// entender por que un contenedor no se lee: si no hay rectangulos sobre el
    /// codigo, el problema esta en la localizacion, no en el OCR.
    /// </summary>
    public bool ShowTextRegions { get; set; }

    /// <summary>Dibujar el panel superior con el progreso y el recuento.</summary>
    public bool ShowHud { get; set; } = true;

    /// <summary>Codigos confirmados que se listan en el panel.</summary>
    public int HudCodeCount { get; set; } = 5;
}
