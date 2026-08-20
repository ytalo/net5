namespace MaritimeVision.Core.Models;

/// <summary>
/// Objeto con identidad temporal: el resultado de asociar detecciones a lo largo
/// de varios fotogramas. Es la vista inmutable que el pipeline expone hacia fuera.
/// </summary>
/// <param name="TrackId">Identificador estable mientras el objeto siga en escena.</param>
/// <param name="Box">Caja del fotograma actual (observada o predicha por Kalman).</param>
/// <param name="Score">Confianza de la ultima deteccion asociada.</param>
/// <param name="ClassId">Indice de clase.</param>
/// <param name="Label">Nombre legible de la clase.</param>
/// <param name="State">Estado en el ciclo de vida del tracker.</param>
/// <param name="Age">Numero de fotogramas desde que el track se creo.</param>
/// <param name="HitStreak">Fotogramas consecutivos con deteccion asociada.</param>
/// <param name="Velocity">Velocidad estimada del centro, en pixeles por fotograma.</param>
public readonly record struct TrackedObject(
    int TrackId,
    BoundingBox Box,
    float Score,
    int ClassId,
    string Label,
    TrackState State,
    int Age,
    int HitStreak,
    (float X, float Y) Velocity)
{
    /// <summary>
    /// <c>true</c> si el track se apoya en una deteccion de este fotograma;
    /// <c>false</c> si su caja proviene solo de la prediccion del filtro de Kalman.
    /// </summary>
    public bool IsConfirmed => State == TrackState.Tracked;

    public override string ToString() => $"#{TrackId} {Label} {Score:P0} {Box}";
}
