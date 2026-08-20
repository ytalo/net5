using MaritimeVision.Core.Models;

namespace MaritimeVision.Core.Detectors;

/// <summary>Supresion de no maximos.</summary>
public static class NonMaxSuppression
{
    /// <summary>
    /// Descarta detecciones redundantes quedandose, para cada grupo de cajas
    /// solapadas, con la de mayor confianza.
    /// </summary>
    /// <param name="detections">Detecciones candidatas.</param>
    /// <param name="iouThreshold">Solape a partir del cual dos cajas se consideran la misma.</param>
    /// <param name="classAgnostic">
    /// Si es <c>false</c> (por defecto) la supresion se aplica dentro de cada clase,
    /// de modo que un contenedor y el camion que lo transporta pueden coexistir
    /// aunque sus cajas se solapen.
    /// </param>
    /// <param name="maxDetections">Numero maximo de detecciones a devolver.</param>
    public static List<Detection> Apply(
        IReadOnlyList<Detection> detections,
        float iouThreshold,
        bool classAgnostic = false,
        int maxDetections = 300)
    {
        ArgumentNullException.ThrowIfNull(detections);

        var kept = new List<Detection>(Math.Min(detections.Count, maxDetections));
        if (detections.Count == 0)
        {
            return kept;
        }

        var order = Enumerable.Range(0, detections.Count).ToArray();
        Array.Sort(order, (a, b) => detections[b].Score.CompareTo(detections[a].Score));

        var suppressed = new bool[detections.Count];

        for (var i = 0; i < order.Length && kept.Count < maxDetections; i++)
        {
            var currentIndex = order[i];
            if (suppressed[currentIndex])
            {
                continue;
            }

            var current = detections[currentIndex];
            kept.Add(current);

            for (var j = i + 1; j < order.Length; j++)
            {
                var candidateIndex = order[j];
                if (suppressed[candidateIndex])
                {
                    continue;
                }

                var candidate = detections[candidateIndex];
                if (!classAgnostic && candidate.ClassId != current.ClassId)
                {
                    continue;
                }

                if (current.Box.IntersectionOverUnion(candidate.Box) > iouThreshold)
                {
                    suppressed[candidateIndex] = true;
                }
            }
        }

        return kept;
    }
}
