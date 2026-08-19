using MaritimeVision.Core.Configuration;
using MaritimeVision.Core.Detectors;
using MaritimeVision.Core.Models;
using OpenCvSharp;
using Xunit;

namespace MaritimeVision.Tests;

public class LetterboxTests
{
    [Fact]
    public void ScalesByTheSmallerFactorAndCentersThePadding()
    {
        // 1920x1080 en un lienzo de 640: escala 1/3, altura util 360, relleno 140 arriba y abajo.
        var letterbox = Letterbox.Compute(1920, 1080, 640, 640);

        Assert.Equal(640f / 1920f, letterbox.Scale, 5);
        Assert.Equal(0f, letterbox.PadX, 3);
        Assert.Equal(140f, letterbox.PadY, 3);
    }

    [Fact]
    public void InvertUndoesTheForwardTransform()
    {
        var letterbox = Letterbox.Compute(1280, 720, 640, 640);
        var original = new BoundingBox(300, 200, 160, 90);

        // Transformacion directa: escalar y desplazar por el relleno.
        var projected = BoundingBox.FromCorners(
            (original.Left * letterbox.Scale) + letterbox.PadX,
            (original.Top * letterbox.Scale) + letterbox.PadY,
            (original.Right * letterbox.Scale) + letterbox.PadX,
            (original.Bottom * letterbox.Scale) + letterbox.PadY);

        var restored = letterbox.Invert(projected);

        Assert.Equal(original.X, restored.X, 2);
        Assert.Equal(original.Y, restored.Y, 2);
        Assert.Equal(original.Width, restored.Width, 2);
        Assert.Equal(original.Height, restored.Height, 2);
    }

    [Fact]
    public void InvertClampsBoxesThatSpillOutsideTheFrame()
    {
        var letterbox = Letterbox.Compute(640, 360, 640, 640);
        var restored = letterbox.Invert(new BoundingBox(-50, -50, 900, 900));

        Assert.True(restored.Left >= 0f);
        Assert.True(restored.Top >= 0f);
        Assert.True(restored.Right <= 640f);
        Assert.True(restored.Bottom <= 360f);
    }

    [Fact]
    public void ApplyProducesACanvasOfTheRequestedSizeWithoutDistortingContent()
    {
        using var source = new Mat(360, 640, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var letterbox = Letterbox.Compute(source.Width, source.Height, 640, 640);

        using var canvas = letterbox.Apply(source, 640, 640);

        Assert.Equal(640, canvas.Width);
        Assert.Equal(640, canvas.Height);

        // El centro conserva el color original; las bandas superior e inferior son el relleno gris.
        var center = canvas.At<Vec3b>(320, 320);
        Assert.Equal(10, center.Item0);
        Assert.Equal(20, center.Item1);
        Assert.Equal(30, center.Item2);

        var padding = canvas.At<Vec3b>(5, 320);
        Assert.Equal(114, padding.Item0);
        Assert.Equal(114, padding.Item1);
        Assert.Equal(114, padding.Item2);
    }
}

public class NonMaxSuppressionTests
{
    [Fact]
    public void KeepsTheHighestScoringBoxAmongOverlappingDuplicates()
    {
        var detections = new List<Detection>
        {
            Box(100, 100, 0.60f),
            Box(104, 102, 0.90f),
            Box(98, 99, 0.75f),
        };

        var kept = NonMaxSuppression.Apply(detections, iouThreshold: 0.45f);

        var single = Assert.Single(kept);
        Assert.Equal(0.90f, single.Score, 3);
    }

    [Fact]
    public void KeepsBoxesThatDoNotOverlap()
    {
        var detections = new List<Detection>
        {
            Box(0, 0, 0.9f),
            Box(500, 400, 0.8f),
        };

        Assert.Equal(2, NonMaxSuppression.Apply(detections, iouThreshold: 0.45f).Count);
    }

    [Fact]
    public void OverlappingBoxesOfDifferentClassesBothSurviveByDefault()
    {
        var detections = new List<Detection>
        {
            new(new BoundingBox(100, 100, 80, 60), 0.9f, 0, "container"),
            new(new BoundingBox(102, 101, 80, 60), 0.8f, 1, "container-truck"),
        };

        Assert.Equal(2, NonMaxSuppression.Apply(detections, iouThreshold: 0.45f).Count);
    }

    [Fact]
    public void ClassAgnosticModeSuppressesAcrossClasses()
    {
        var detections = new List<Detection>
        {
            new(new BoundingBox(100, 100, 80, 60), 0.9f, 0, "container"),
            new(new BoundingBox(102, 101, 80, 60), 0.8f, 1, "container-truck"),
        };

        var kept = NonMaxSuppression.Apply(detections, iouThreshold: 0.45f, classAgnostic: true);

        Assert.Equal("container", Assert.Single(kept).Label);
    }

    [Fact]
    public void RespectsTheMaximumNumberOfDetections()
    {
        var detections = Enumerable
            .Range(0, 50)
            .Select(index => Box(index * 200, 0, 0.5f + (index * 0.001f)))
            .ToList();

        Assert.Equal(10, NonMaxSuppression.Apply(detections, 0.45f, maxDetections: 10).Count);
    }

    [Fact]
    public void EmptyInputProducesEmptyOutput()
        => Assert.Empty(NonMaxSuppression.Apply([], 0.45f));

    [Fact]
    public void OutputIsOrderedByDescendingScore()
    {
        var detections = new List<Detection>
        {
            Box(0, 0, 0.4f),
            Box(400, 0, 0.95f),
            Box(800, 0, 0.7f),
        };

        var kept = NonMaxSuppression.Apply(detections, 0.45f);
        var scores = kept.Select(detection => detection.Score).ToArray();

        Assert.Equal(scores.OrderByDescending(score => score).ToArray(), scores);
    }

    private static Detection Box(float x, float y, float score)
        => new(new BoundingBox(x, y, 80, 60), score, 0, "container");
}
