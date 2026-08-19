using MaritimeVision.Core.Configuration;
using MaritimeVision.Core.Detectors;
using OpenCvSharp;
using Xunit;

namespace MaritimeVision.Tests;

/// <summary>
/// Ejercita <see cref="YoloDetector"/> contra un modelo ONNX real y diminuto
/// (<c>fixtures/tiny-yolo-fixture.onnx</c>, generado por el script que lo
/// acompana). Cubre el camino que las pruebas de tensores sinteticos no tocan:
/// preprocesado, ONNX Runtime, y la vuelta de las cajas al espacio de la imagen.
/// </summary>
public class YoloDetectorOnnxTests
{
    private const string ModelPath = "fixtures/tiny-yolo-fixture.onnx";

    private static readonly LabelSet Labels = new(["container", "container-truck"]);

    private static YoloDetectorOptions BaseOptions() => new()
    {
        ModelPath = ModelPath,
        Labels = Labels,
        ConfidenceThreshold = 0.25f,
        IouThreshold = 0.45f,
    };

    [Fact]
    public void LoadsTheModelAndReportsItsInputSize()
    {
        using var detector = new YoloDetector(BaseOptions());

        Assert.Equal((64, 64), detector.InputSize);
        Assert.Equal(2, detector.Labels.Count);
        Assert.Contains("tiny-yolo-fixture.onnx", detector.Description);
    }

    [Fact]
    public void InfersTheOutputLayoutOnTheFirstFrame()
    {
        using var detector = new YoloDetector(BaseOptions());
        Assert.Null(detector.OutputSpec);

        using var frame = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(0));
        detector.Detect(frame);

        var spec = Assert.IsType<YoloOutputSpec>(detector.OutputSpec);
        Assert.True(spec.ChannelsFirst);
        Assert.False(spec.HasObjectness);
        Assert.Equal(3, spec.Rows);
        Assert.Equal(2, spec.ClassCount);
    }

    [Fact]
    public void PixelDataReachesTheModel()
    {
        // En el modelo de prueba la confianza de la primera ancla es la media de
        // los pixeles de entrada, asi que el contenido del fotograma cambia el
        // resultado. Si el preprocesado no llegase al modelo, las dos llamadas
        // devolverian exactamente lo mismo.
        using var detector = new YoloDetector(BaseOptions());

        using var black = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(0));
        var onBlack = detector.Detect(black).Where(d => d.Label == "container").Max(d => d.Score);

        using var white = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(255));
        var onWhite = detector.Detect(white).Where(d => d.Label == "container").Max(d => d.Score);

        Assert.Equal(0.80f, onBlack, 3);   // solo el ancla de confianza fija
        Assert.Equal(1.00f, onWhite, 2);   // la media de un fotograma blanco es 1
    }

    [Fact]
    public void SuppressesTheDuplicateAnchor()
    {
        using var detector = new YoloDetector(BaseOptions());

        // Con un fotograma blanco las dos primeras anclas son de la misma clase y
        // se solapan casi por completo: la NMS debe dejar solo la mejor.
        using var white = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(255));
        var detections = detector.Detect(white);

        Assert.Equal(2, detections.Count);
        Assert.Single(detections, detection => detection.Label == "container");
        Assert.Single(detections, detection => detection.Label == "container-truck");
    }

    [Fact]
    public void ConvertsCentresToCornersInTheOriginalFrame()
    {
        using var detector = new YoloDetector(BaseOptions());
        using var frame = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(255));

        var container = detector.Detect(frame).First(detection => detection.Label == "container");

        // El ancla 0 declara centro (32, 32) y tamano 20x16.
        Assert.Equal(22f, container.Box.Left, 1);
        Assert.Equal(24f, container.Box.Top, 1);
        Assert.Equal(20f, container.Box.Width, 1);
        Assert.Equal(16f, container.Box.Height, 1);
    }

    [Fact]
    public void UndoesTheLetterboxOnANonSquareFrame()
    {
        using var detector = new YoloDetector(BaseOptions());

        // 128x96 en un lienzo de 64x64: escala 0.5, alto util 48 y 8 px de relleno
        // arriba y abajo.
        using var frame = new Mat(96, 128, MatType.CV_8UC3, Scalar.All(255));

        // Se comprueba sobre el ancla de "container-truck" porque su confianza es
        // constante en el modelo de prueba: la del ancla de "container" depende de
        // la media de la entrada, que el propio relleno gris altera.
        var truck = detector.Detect(frame).First(detection => detection.Label == "container-truck");

        // El modelo declara centro (10, 50) y tamano 8x8, es decir la caja
        // (6, 46)-(14, 54) en el espacio del lienzo. Deshaciendo relleno y escala:
        // x = 6 / 0.5 = 12, y = (46 - 8) / 0.5 = 76, y 16x16 de tamano.
        Assert.Equal(12f, truck.Box.Left, 1);
        Assert.Equal(76f, truck.Box.Top, 1);
        Assert.Equal(16f, truck.Box.Width, 1);
        Assert.Equal(16f, truck.Box.Height, 1);
    }

    [Fact]
    public void LetterboxPaddingLowersTheInputDependentScore()
    {
        // Consecuencia directa del preprocesado: al meter un fotograma no cuadrado
        // el relleno gris (114/255) entra en la media, de modo que un fotograma
        // blanco ya no puntua 1.0. Es la prueba de que el relleno es real.
        using var detector = new YoloDetector(BaseOptions());

        using var square = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(255));
        var withoutPadding = detector.Detect(square).Max(detection => detection.Score);

        using var wide = new Mat(32, 128, MatType.CV_8UC3, Scalar.All(255));
        var withPadding = detector.Detect(wide).Max(detection => detection.Score);

        Assert.Equal(1.0f, withoutPadding, 2);
        Assert.True(withPadding < withoutPadding);
    }

    [Fact]
    public void TargetClassesFilterTheOutput()
    {
        var options = BaseOptions();
        options.TargetClasses.Add("container-truck");

        using var detector = new YoloDetector(options);
        using var frame = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(255));

        var detection = Assert.Single(detector.Detect(frame));
        Assert.Equal("container-truck", detection.Label);
    }

    [Fact]
    public void RaisingTheConfidenceThresholdDropsTheWeakerDetections()
    {
        var options = BaseOptions();
        options.ConfidenceThreshold = 0.75f;

        using var detector = new YoloDetector(options);
        using var frame = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(0));

        // Con el fotograma negro quedan 0.80 (container) y 0.70 (container-truck).
        var detection = Assert.Single(detector.Detect(frame));
        Assert.Equal("container", detection.Label);
    }

    [Fact]
    public void WithoutExplicitLabelsTheClassCountIsInferredFromTheModel()
    {
        var options = BaseOptions();
        options.Labels = null;

        using var detector = new YoloDetector(options);

        Assert.Equal(2, detector.Labels.Count);
        Assert.Equal("class_0", detector.Labels[0]);
    }

    [Fact]
    public void AnEmptyFrameProducesNoDetections()
    {
        using var detector = new YoloDetector(BaseOptions());
        using var empty = new Mat();

        Assert.Empty(detector.Detect(empty));
    }

    [Fact]
    public void UsingTheDetectorAfterDisposeIsRejected()
    {
        var detector = new YoloDetector(BaseOptions());
        detector.Dispose();

        using var frame = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(0));
        Assert.Throws<ObjectDisposedException>(() => detector.Detect(frame));
    }

    [Fact]
    public void WarmupResolvesTheOutputLayoutBeforeTheFirstRealFrame()
    {
        using var detector = new YoloDetector(BaseOptions());
        Assert.Null(detector.OutputSpec);

        detector.Warmup();

        Assert.NotNull(detector.OutputSpec);
    }

    [Fact]
    public void AMismatchedLabelFileStillProducesUsableGeometry()
    {
        // El modelo emite 2 clases; se le pasan 7 etiquetas a proposito. El
        // detector avisa por la salida de error, pero no debe reventar: interpreta
        // la geometria a partir del propio tensor.
        var options = BaseOptions();
        options.Labels = new LabelSet([
            "container", "container-truck", "chassis", "reach-stacker", "gantry-crane", "ship", "person",
        ]);

        using var detector = new YoloDetector(options);
        using var frame = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(255));

        var detections = detector.Detect(frame);

        Assert.Equal(2, detections.Count);
        Assert.Equal(2, detector.OutputSpec!.ClassCount);
        Assert.Equal(22f, detections.First(d => d.ClassId == 0).Box.Left, 1);
    }

    [Fact]
    public void AMissingModelIsReportedClearly()
    {
        var options = BaseOptions();
        options.ModelPath = "fixtures/no-existe.onnx";

        var error = Assert.Throws<InvalidOperationException>(() => new YoloDetector(options));
        Assert.Contains("no-existe.onnx", error.Message);
    }

    [Fact]
    public void InvalidThresholdsAreRejectedBeforeLoadingTheModel()
    {
        var options = BaseOptions();
        options.ConfidenceThreshold = 1.5f;

        Assert.Throws<InvalidOperationException>(() => new YoloDetector(options));
    }
}
