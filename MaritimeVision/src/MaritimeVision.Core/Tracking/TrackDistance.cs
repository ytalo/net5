using MaritimeVision.Core.Models;

namespace MaritimeVision.Core.Tracking;

/// <summary>Matrices de coste que alimentan la asignacion lineal del tracker.</summary>
public static class TrackDistance
{
    /// <summary>
    /// Coste prohibitivo para pares que no deben emparejarse nunca. No se usa
    /// <see cref="double.PositiveInfinity"/> porque el algoritmo de asignacion
    /// necesita aritmetica finita para propagar los potenciales duales.
    /// </summary>
    public const double Forbidden = 1e6;

    /// <summary>
    /// Distancia IoU: <c>1 - IoU</c>. Vale 0 para cajas identicas y 1 para cajas
    /// disjuntas.
    /// </summary>
    /// <param name="tracks">Tracks candidatos (filas).</param>
    /// <param name="detections">Detecciones candidatas (columnas).</param>
    /// <param name="classAware">
    /// Si es <c>true</c>, los pares de clases distintas reciben coste
    /// <see cref="Forbidden"/> y nunca se asocian.
    /// </param>
    public static double[,] IouDistance(
        IReadOnlyList<Track> tracks,
        IReadOnlyList<Detection> detections,
        bool classAware)
    {
        var cost = new double[tracks.Count, detections.Count];

        for (var i = 0; i < tracks.Count; i++)
        {
            var trackBox = tracks[i].Box;
            var trackClass = tracks[i].ClassId;

            for (var j = 0; j < detections.Count; j++)
            {
                if (classAware && trackClass != detections[j].ClassId)
                {
                    cost[i, j] = Forbidden;
                    continue;
                }

                cost[i, j] = 1d - trackBox.IntersectionOverUnion(detections[j].Box);
            }
        }

        return cost;
    }

    /// <summary>
    /// Pondera la similitud IoU con la confianza de la deteccion
    /// (<c>coste = 1 - IoU * score</c>).
    /// </summary>
    /// <remarks>
    /// Es el <c>fuse_score</c> de ByteTrack: ante dos candidatos con solape
    /// parecido, prefiere el que el detector considera mas fiable.
    /// </remarks>
    public static void FuseScore(double[,] cost, IReadOnlyList<Detection> detections)
    {
        ArgumentNullException.ThrowIfNull(cost);

        var rows = cost.GetLength(0);
        var columns = cost.GetLength(1);

        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < columns; j++)
            {
                if (cost[i, j] >= Forbidden)
                {
                    continue;
                }

                var similarity = (1d - cost[i, j]) * detections[j].Score;
                cost[i, j] = 1d - similarity;
            }
        }
    }
}
