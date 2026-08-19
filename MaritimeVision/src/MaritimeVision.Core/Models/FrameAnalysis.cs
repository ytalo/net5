namespace MaritimeVision.Core.Models;

/// <summary>Resultado del analisis de un unico fotograma.</summary>
/// <param name="FrameIndex">Indice del fotograma, empezando en 0.</param>
/// <param name="Timestamp">Posicion temporal dentro de la secuencia.</param>
/// <param name="Detections">Detecciones crudas tras NMS y filtrado de clases.</param>
/// <param name="Tracks">Objetos con identidad devueltos por ByteTrack.</param>
/// <param name="InferenceTime">Tiempo empleado por el detector.</param>
/// <param name="TrackingTime">Tiempo empleado por el tracker.</param>
public sealed record FrameAnalysis(
    int FrameIndex,
    TimeSpan Timestamp,
    IReadOnlyList<Detection> Detections,
    IReadOnlyList<TrackedObject> Tracks,
    TimeSpan InferenceTime,
    TimeSpan TrackingTime)
{
    /// <summary>Analisis vacio, util como valor por defecto.</summary>
    public static FrameAnalysis Empty { get; } = new(
        0, TimeSpan.Zero, Array.Empty<Detection>(), Array.Empty<TrackedObject>(), TimeSpan.Zero, TimeSpan.Zero);
}
