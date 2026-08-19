namespace MaritimeVision.Core.Rendering;

/// <summary>Criterio de coloreado de las cajas.</summary>
public enum ColorMode
{
    /// <summary>Un color por identidad. Hace evidente cualquier cambio de ID.</summary>
    ByTrackId = 0,

    /// <summary>Un color por clase. Util cuando importa mas el tipo que la identidad.</summary>
    ByClass = 1,
}

/// <summary>Opciones de dibujo de <see cref="BoxRenderer"/>.</summary>
public sealed class RenderOptions
{
    /// <summary>Criterio de color.</summary>
    public ColorMode ColorMode { get; set; } = ColorMode.ByTrackId;

    /// <summary>Grosor del borde de la caja, en pixeles.</summary>
    public int BoxThickness { get; set; } = 2;

    /// <summary>Dibujar la etiqueta con identidad, clase y confianza.</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>Incluir la confianza en la etiqueta.</summary>
    public bool ShowConfidence { get; set; } = true;

    /// <summary>Dibujar la estela del centro de cada objeto.</summary>
    public bool ShowTrails { get; set; } = true;

    /// <summary>Longitud maxima de la estela, en fotogramas.</summary>
    public int TrailLength { get; set; } = 30;

    /// <summary>Dibujar el panel superior con fotograma, FPS y recuento.</summary>
    public bool ShowHud { get; set; } = true;

    /// <summary>
    /// Dibujar tambien las detecciones crudas, en gris y linea fina, ademas de los
    /// tracks. Ayuda a diagnosticar el comportamiento del tracker.
    /// </summary>
    public bool ShowRawDetections { get; set; }

    /// <summary>Escala de la tipografia.</summary>
    public double FontScale { get; set; } = 0.5d;
}
