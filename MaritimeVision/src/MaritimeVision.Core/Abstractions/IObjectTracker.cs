using MaritimeVision.Core.Models;

namespace MaritimeVision.Core.Abstractions;

/// <summary>Asocia detecciones entre fotogramas para darles identidad estable.</summary>
public interface IObjectTracker
{
    /// <summary>
    /// Incorpora las detecciones de un fotograma y devuelve los tracks activos.
    /// Debe llamarse una vez por fotograma y en orden.
    /// </summary>
    IReadOnlyList<TrackedObject> Update(IReadOnlyList<Detection> detections);

    /// <summary>Descarta todos los tracks y reinicia el contador de identificadores.</summary>
    void Reset();
}
