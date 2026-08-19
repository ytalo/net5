using MaritimeVision.Core.Models;

namespace MaritimeVision.Core.Tracking;

/// <summary>
/// Trayectoria individual gestionada por <see cref="ByteTracker"/>: equivale al
/// <c>STrack</c> de la implementacion de referencia.
/// </summary>
public sealed class Track
{
    private readonly KalmanFilter _kalmanFilter;
    private KalmanState _state;

    private Track(KalmanFilter kalmanFilter, in Detection detection)
    {
        _kalmanFilter = kalmanFilter;
        _state = kalmanFilter.Initiate(detection.Box);
        Score = detection.Score;
        ClassId = detection.ClassId;
        Label = detection.Label;
    }

    /// <summary>Identificador estable del track. Vale 0 hasta que se activa.</summary>
    public int TrackId { get; private set; }

    /// <summary>Estado en el ciclo de vida.</summary>
    public TrackState State { get; private set; } = TrackState.New;

    /// <summary>
    /// <c>true</c> cuando el track se ha confirmado con al menos dos observaciones
    /// (o nacio en el primer fotograma). Solo los confirmados se publican.
    /// </summary>
    public bool IsActivated { get; private set; }

    /// <summary>Confianza de la ultima deteccion asociada.</summary>
    public float Score { get; private set; }

    /// <summary>Clase de la ultima deteccion asociada.</summary>
    public int ClassId { get; private set; }

    /// <summary>Etiqueta legible de la clase.</summary>
    public string Label { get; private set; }

    /// <summary>Ultimo fotograma en el que el track recibio una deteccion.</summary>
    public int FrameId { get; private set; }

    /// <summary>Fotograma en el que el track nacio.</summary>
    public int StartFrame { get; private set; }

    /// <summary>Observaciones consecutivas acumuladas desde la ultima reactivacion.</summary>
    public int TrackletLength { get; private set; }

    /// <summary>Caja actual, derivada del estado del filtro de Kalman.</summary>
    public BoundingBox Box => KalmanFilter.ToBoundingBox(_state);

    /// <summary>Velocidad estimada del centro, en pixeles por fotograma.</summary>
    public (float X, float Y) Velocity => ((float)_state.Mean[4, 0], (float)_state.Mean[5, 0]);

    /// <summary>Estado interno del filtro; expuesto para diagnostico y pruebas.</summary>
    public KalmanState KalmanState => _state;

    /// <summary>
    /// Crea un track todavia sin activar a partir de una deteccion. El
    /// identificador se asigna en <see cref="Activate"/>.
    /// </summary>
    public static Track FromDetection(KalmanFilter kalmanFilter, in Detection detection)
        => new(kalmanFilter, detection);

    /// <summary>Propaga el estado un fotograma hacia delante.</summary>
    public void Predict()
    {
        // Un track que no esta siendo observado no tiene evidencia para seguir
        // creciendo o encogiendo: se congela la velocidad de la altura para que la
        // caja extrapolada no se deforme durante una oclusion larga.
        if (State != TrackState.Tracked)
        {
            _state.Mean[7, 0] = 0d;
        }

        _state = _kalmanFilter.Predict(_state);
    }

    /// <summary>Da de alta el track y le asigna identificador.</summary>
    /// <param name="trackId">Identificador nuevo.</param>
    /// <param name="frameId">Fotograma actual.</param>
    /// <param name="isFirstFrame">
    /// Si el track nace en el primer fotograma se confirma de inmediato; en
    /// cualquier otro caso debe ratificarse con una segunda observacion.
    /// </param>
    public void Activate(int trackId, int frameId, bool isFirstFrame)
    {
        TrackId = trackId;
        TrackletLength = 0;
        State = TrackState.Tracked;
        IsActivated = isFirstFrame;
        FrameId = frameId;
        StartFrame = frameId;
    }

    /// <summary>Recupera un track perdido asociandolo a una deteccion nueva.</summary>
    public void ReActivate(in Detection detection, int frameId, int? newTrackId = null)
    {
        _state = _kalmanFilter.Update(_state, detection.Box);
        TrackletLength = 0;
        State = TrackState.Tracked;
        IsActivated = true;
        FrameId = frameId;
        Score = detection.Score;
        ClassId = detection.ClassId;
        Label = detection.Label;

        if (newTrackId.HasValue)
        {
            TrackId = newTrackId.Value;
        }
    }

    /// <summary>Corrige el track con la deteccion asociada en este fotograma.</summary>
    public void Update(in Detection detection, int frameId)
    {
        FrameId = frameId;
        TrackletLength++;
        _state = _kalmanFilter.Update(_state, detection.Box);
        State = TrackState.Tracked;
        IsActivated = true;
        Score = detection.Score;
        ClassId = detection.ClassId;
        Label = detection.Label;
    }

    /// <summary>Marca el track como perdido; sigue vivo dentro del buffer.</summary>
    public void MarkLost() => State = TrackState.Lost;

    /// <summary>Marca el track como descartado definitivamente.</summary>
    public void MarkRemoved() => State = TrackState.Removed;

    /// <summary>Proyecta el track a la vista inmutable que consume el pipeline.</summary>
    public TrackedObject ToTrackedObject(int currentFrameId) => new(
        TrackId,
        Box,
        Score,
        ClassId,
        Label,
        State,
        Math.Max(0, currentFrameId - StartFrame),
        TrackletLength,
        Velocity);

    public override string ToString() => $"Track#{TrackId} {Label} {State} {Box}";
}
