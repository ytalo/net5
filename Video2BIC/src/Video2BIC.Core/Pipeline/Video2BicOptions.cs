namespace Video2BIC.Core.Pipeline;

/// <summary>Opciones de ejecucion de <see cref="Video2BicPipeline"/>.</summary>
public sealed class Video2BicOptions
{
    /// <summary>
    /// Etiquetas que se consideran contenedor. Vacio acepta cualquier clase que
    /// emita el detector.
    /// </summary>
    /// <remarks>
    /// Es un filtro aparte del <c>--classes</c> del detector: permite que el modelo
    /// siga detectando camiones o gruas (util para el seguimiento, porque evita que
    /// un contenedor sobre un camion robe identidades) y que solo se intente leer el
    /// codigo de los contenedores.
    /// </remarks>
    public IList<string> ContainerLabels { get; } = new List<string> { "container" };

    /// <summary>
    /// Fotogramas que se dejan pasar entre dos intentos de lectura del mismo
    /// contenedor.
    /// </summary>
    /// <remarks>
    /// El OCR es, con diferencia, la parte cara. Leer cada fotograma no aporta:
    /// dos fotogramas consecutivos son casi la misma imagen. Espaciar los intentos
    /// da ademas variedad de angulo, que es justo lo que necesita la votacion.
    /// </remarks>
    public int ReadIntervalFrames { get; set; } = 5;

    /// <summary>Fotogramas consecutivos con deteccion antes de empezar a leer.</summary>
    public int MinHitStreak { get; set; } = 2;

    /// <summary>
    /// Contenedores que se leen como maximo en un mismo fotograma. Acota el peor
    /// caso cuando entra un tren entero en escena.
    /// </summary>
    public int MaxReadsPerFrame { get; set; } = 2;

    /// <summary>
    /// Intentos maximos por contenedor; 0 no impone limite. Evita gastar el
    /// presupuesto en un contenedor cuyo codigo no da la cara.
    /// </summary>
    public int MaxReadsPerTrack { get; set; }

    /// <summary>
    /// Dejar de leer un contenedor en cuanto su codigo queda confirmado. Se puede
    /// desactivar para seguir acumulando evidencia y medir la estabilidad.
    /// </summary>
    public bool StopWhenConfirmed { get; set; } = true;

    /// <summary>Dibujar las anotaciones sobre el fotograma.</summary>
    public bool Annotate { get; set; } = true;

    /// <summary>Fotogramas maximos a procesar; 0 procesa la secuencia entera.</summary>
    public int MaxFrames { get; set; }

    /// <summary>Escribir el progreso por consola.</summary>
    public bool ReportProgress { get; set; }

    /// <summary>Cada cuantos fotogramas se informa del progreso.</summary>
    public int ProgressInterval { get; set; } = 30;

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun valor es imposible.</exception>
    public void Validate()
    {
        if (ReadIntervalFrames < 1)
        {
            throw new InvalidOperationException("ReadIntervalFrames debe ser positivo.");
        }

        if (MinHitStreak < 1)
        {
            throw new InvalidOperationException("MinHitStreak debe ser positivo.");
        }

        if (MaxReadsPerFrame < 1)
        {
            throw new InvalidOperationException("MaxReadsPerFrame debe ser positivo.");
        }

        if (MaxReadsPerTrack < 0)
        {
            throw new InvalidOperationException("MaxReadsPerTrack no puede ser negativo.");
        }

        if (ProgressInterval < 1)
        {
            throw new InvalidOperationException("ProgressInterval debe ser positivo.");
        }
    }
}
