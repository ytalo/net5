namespace MaritimeVision.Core.Models;

/// <summary>
/// Una deteccion cruda emitida por el detector para un unico fotograma, antes de
/// pasar por el tracker. No tiene identidad temporal.
/// </summary>
/// <param name="Box">Caja en coordenadas del fotograma original.</param>
/// <param name="Score">Confianza en [0, 1].</param>
/// <param name="ClassId">Indice de clase dentro del conjunto de etiquetas del modelo.</param>
/// <param name="Label">Nombre legible de la clase.</param>
public readonly record struct Detection(BoundingBox Box, float Score, int ClassId, string Label)
{
    public override string ToString() => $"{Label}({ClassId}) {Score:P0} {Box}";
}
