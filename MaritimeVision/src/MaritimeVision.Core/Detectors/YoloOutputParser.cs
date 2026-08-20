using MaritimeVision.Core.Configuration;
using MaritimeVision.Core.Models;

namespace MaritimeVision.Core.Detectors;

/// <summary>
/// Traduce el tensor crudo de un modelo YOLO a detecciones, en el espacio de
/// coordenadas de la entrada del modelo.
/// </summary>
/// <remarks>
/// Esta separado del detector para poder verificarlo con tensores sinteticos, sin
/// necesidad de cargar un modelo ONNX.
/// </remarks>
public static class YoloOutputParser
{
    /// <summary>
    /// Recorre el tensor y emite una deteccion por cada ancla que supere el umbral.
    /// </summary>
    /// <param name="data">Contenido del tensor de salida, en orden plano.</param>
    /// <param name="spec">Interpretacion del tensor.</param>
    /// <param name="labels">Etiquetas del modelo.</param>
    /// <param name="confidenceThreshold">Confianza minima para conservar una deteccion.</param>
    /// <param name="targetClasses">
    /// Clases de interes; <c>null</c> o conjunto vacio significa "todas".
    /// </param>
    public static List<Detection> Parse(
        ReadOnlySpan<float> data,
        YoloOutputSpec spec,
        LabelSet labels,
        float confidenceThreshold,
        IReadOnlySet<int>? targetClasses = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(labels);

        var expected = spec.Rows * spec.Features;
        if (data.Length < expected)
        {
            throw new ArgumentException(
                $"El tensor tiene {data.Length} valores pero la forma declarada exige {expected}.", nameof(data));
        }

        return spec.IsEndToEnd
            ? ParseEndToEnd(data, spec, labels, confidenceThreshold, targetClasses)
            : ParseAnchors(data, spec, labels, confidenceThreshold, targetClasses);
    }

    private static List<Detection> ParseAnchors(
        ReadOnlySpan<float> data,
        YoloOutputSpec spec,
        LabelSet labels,
        float confidenceThreshold,
        IReadOnlySet<int>? targetClasses)
    {
        var detections = new List<Detection>();
        var classOffset = spec.HasObjectness ? 5 : 4;
        var classCount = Math.Min(spec.ClassCount, spec.Features - classOffset);

        for (var row = 0; row < spec.Rows; row++)
        {
            // En YOLOv5 la confianza final es objectness * probabilidad de clase.
            // En YOLOv8 la objectness desaparecio y la puntuacion de clase ya es final.
            var objectness = spec.HasObjectness ? data[spec.Offset(row, 4)] : 1f;
            if (objectness < confidenceThreshold)
            {
                continue;
            }

            var bestClass = -1;
            var bestScore = 0f;

            for (var c = 0; c < classCount; c++)
            {
                var score = data[spec.Offset(row, classOffset + c)];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClass = c;
                }
            }

            if (bestClass < 0)
            {
                continue;
            }

            var confidence = objectness * bestScore;
            if (confidence < confidenceThreshold)
            {
                continue;
            }

            if (targetClasses is { Count: > 0 } && !targetClasses.Contains(bestClass))
            {
                continue;
            }

            var box = BoundingBox.FromCenter(
                data[spec.Offset(row, 0)],
                data[spec.Offset(row, 1)],
                data[spec.Offset(row, 2)],
                data[spec.Offset(row, 3)]);

            if (box.Width <= 0f || box.Height <= 0f)
            {
                continue;
            }

            detections.Add(new Detection(box, confidence, bestClass, labels.GetLabel(bestClass)));
        }

        return detections;
    }

    private static List<Detection> ParseEndToEnd(
        ReadOnlySpan<float> data,
        YoloOutputSpec spec,
        LabelSet labels,
        float confidenceThreshold,
        IReadOnlySet<int>? targetClasses)
    {
        var detections = new List<Detection>();

        for (var row = 0; row < spec.Rows; row++)
        {
            var confidence = data[spec.Offset(row, 4)];
            if (confidence < confidenceThreshold)
            {
                continue;
            }

            var classId = (int)MathF.Round(data[spec.Offset(row, 5)]);
            if (targetClasses is { Count: > 0 } && !targetClasses.Contains(classId))
            {
                continue;
            }

            var box = BoundingBox.FromCorners(
                data[spec.Offset(row, 0)],
                data[spec.Offset(row, 1)],
                data[spec.Offset(row, 2)],
                data[spec.Offset(row, 3)]);

            if (box.Width <= 0f || box.Height <= 0f)
            {
                continue;
            }

            detections.Add(new Detection(box, confidence, classId, labels.GetLabel(classId)));
        }

        return detections;
    }
}
