namespace MaritimeVision.Core.Models;

/// <summary>Ciclo de vida de un track dentro de ByteTrack.</summary>
public enum TrackState
{
    /// <summary>Recien creado; aun no confirmado por una segunda observacion.</summary>
    New = 0,

    /// <summary>Confirmado y asociado a una deteccion en el fotograma actual.</summary>
    Tracked = 1,

    /// <summary>Sin asociar en este fotograma; se mantiene vivo dentro del buffer.</summary>
    Lost = 2,

    /// <summary>Agotado el buffer; se descarta definitivamente.</summary>
    Removed = 3,
}
