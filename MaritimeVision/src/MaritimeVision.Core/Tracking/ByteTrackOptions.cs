namespace MaritimeVision.Core.Tracking;

/// <summary>Parametros de configuracion de <see cref="ByteTracker"/>.</summary>
public sealed class ByteTrackOptions
{
    /// <summary>
    /// Frontera entre detecciones de confianza alta y baja. Las que la superan
    /// entran en la primera asociacion; las que quedan por debajo (pero por encima
    /// de <see cref="LowThreshold"/>) se reservan para la segunda.
    /// </summary>
    public float TrackThreshold { get; set; } = 0.5f;

    /// <summary>
    /// Confianza minima para que una deteccion se tenga en cuenta. Por debajo de
    /// este valor se descarta incluso en la segunda asociacion.
    /// </summary>
    public float LowThreshold { get; set; } = 0.1f;

    /// <summary>
    /// Confianza minima para inaugurar un track nuevo. Por defecto se sigue la
    /// referencia de ByteTrack: <see cref="TrackThreshold"/> + 0.1.
    /// </summary>
    public float? NewTrackThreshold { get; set; }

    /// <summary>Distancia IoU maxima admitida en la primera asociacion.</summary>
    public float MatchThreshold { get; set; } = 0.8f;

    /// <summary>Distancia IoU maxima admitida en la segunda asociacion (confianza baja).</summary>
    public float SecondMatchThreshold { get; set; } = 0.5f;

    /// <summary>Distancia IoU maxima admitida al reconciliar tracks sin confirmar.</summary>
    public float UnconfirmedMatchThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Fotogramas que un track sobrevive sin detecciones antes de descartarse.
    /// Se escala con <see cref="FrameRate"/>, igual que en la implementacion original.
    /// </summary>
    public int TrackBuffer { get; set; } = 30;

    /// <summary>Tasa de fotogramas de la secuencia; escala <see cref="TrackBuffer"/>.</summary>
    public double FrameRate { get; set; } = 30d;

    /// <summary>
    /// Multiplica la similitud IoU por la confianza de la deteccion antes de
    /// asignar. Mejora la precision en escenas densas y es el modo por defecto de
    /// ByteTrack salvo en MOT20.
    /// </summary>
    public bool FuseScore { get; set; } = true;

    /// <summary>
    /// Impide asociar un track con una deteccion de otra clase. ByteTrack original
    /// ignora la clase; en una terminal portuaria conviene activarlo para que un
    /// contenedor no herede la identidad de un camion al cruzarse con el.
    /// </summary>
    public bool ClassAwareMatching { get; set; } = true;

    /// <summary>
    /// Numero de fotogramas maximos que se extrapola la posicion de un track
    /// perdido. Mas alla, deja de publicarse aunque siga vivo en el buffer.
    /// </summary>
    public int MaxPredictedFrames { get; set; } = 0;

    /// <summary>Fotogramas que un track sobrevive perdido, ya escalado por la tasa real.</summary>
    public int MaxTimeLost => Math.Max(1, (int)(FrameRate / 30d * TrackBuffer));

    /// <summary>Umbral efectivo para crear tracks nuevos.</summary>
    public float EffectiveNewTrackThreshold => NewTrackThreshold ?? (TrackThreshold + 0.1f);

    /// <summary>Valida la coherencia de los parametros.</summary>
    /// <exception cref="InvalidOperationException">Si algun parametro esta fuera de rango.</exception>
    public void Validate()
    {
        if (TrackThreshold is < 0f or > 1f)
        {
            throw new InvalidOperationException("TrackThreshold debe estar en [0, 1].");
        }

        if (LowThreshold is < 0f or > 1f)
        {
            throw new InvalidOperationException("LowThreshold debe estar en [0, 1].");
        }

        if (LowThreshold > TrackThreshold)
        {
            throw new InvalidOperationException("LowThreshold no puede superar a TrackThreshold.");
        }

        if (TrackBuffer <= 0)
        {
            throw new InvalidOperationException("TrackBuffer debe ser positivo.");
        }

        if (FrameRate <= 0d)
        {
            throw new InvalidOperationException("FrameRate debe ser positivo.");
        }
    }
}
