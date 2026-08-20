namespace MaritimeVision.Core.Pipeline;

/// <summary>Opciones de ejecucion de <see cref="VideoAnalyticsPipeline"/>.</summary>
public sealed class PipelineOptions
{
    /// <summary>Dibujar las anotaciones sobre el fotograma.</summary>
    public bool Annotate { get; set; } = true;

    /// <summary>
    /// Numero maximo de fotogramas a procesar; 0 procesa la secuencia entera.
    /// Util para generar vistas previas rapidas de un video largo.
    /// </summary>
    public int MaxFrames { get; set; }

    /// <summary>Escribir el progreso por consola.</summary>
    public bool ReportProgress { get; set; }

    /// <summary>Cada cuantos fotogramas se informa del progreso.</summary>
    public int ProgressInterval { get; set; } = 30;
}
