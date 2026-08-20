using MaritimeVision.Core.Configuration;
using MaritimeVision.Core.Detectors;
using Xunit;

namespace MaritimeVision.Tests;

public class YoloOutputSpecTests
{
    [Fact]
    public void RecognisesTheYolov8Layout()
    {
        // Ultralytics v8/v9/v11 exportan [1, 4 + clases, anclas].
        var spec = YoloOutputSpec.Infer([1, 84, 8400], expectedClassCount: 80);

        Assert.True(spec.ChannelsFirst);
        Assert.False(spec.HasObjectness);
        Assert.False(spec.IsEndToEnd);
        Assert.Equal(8400, spec.Rows);
        Assert.Equal(80, spec.ClassCount);
    }

    [Fact]
    public void RecognisesTheYolov5Layout()
    {
        // YOLOv5/v7 exportan [1, anclas, 5 + clases], con objectness.
        var spec = YoloOutputSpec.Infer([1, 25200, 85], expectedClassCount: 80);

        Assert.False(spec.ChannelsFirst);
        Assert.True(spec.HasObjectness);
        Assert.False(spec.IsEndToEnd);
        Assert.Equal(25200, spec.Rows);
        Assert.Equal(80, spec.ClassCount);
    }

    [Fact]
    public void RecognisesAnExportWithBuiltInNms()
    {
        var spec = YoloOutputSpec.Infer([1, 300, 6], expectedClassCount: 80);

        Assert.True(spec.IsEndToEnd);
        Assert.Equal(300, spec.Rows);
    }

    [Fact]
    public void SingleClassYolov5IsNotMistakenForAnEndToEndExport()
    {
        // Un modelo de una sola clase produce tambien 6 valores por fila; lo que
        // desambigua es el numero de clases esperado.
        var spec = YoloOutputSpec.Infer([1, 25200, 6], expectedClassCount: 1);

        Assert.False(spec.IsEndToEnd);
        Assert.True(spec.HasObjectness);
        Assert.Equal(1, spec.ClassCount);
    }

    [Fact]
    public void SingleClassYolov8IsDetectedFromItsFiveFeatures()
    {
        var spec = YoloOutputSpec.Infer([1, 5, 8400], expectedClassCount: 1);

        Assert.True(spec.ChannelsFirst);
        Assert.False(spec.HasObjectness);
        Assert.Equal(1, spec.ClassCount);
    }

    [Fact]
    public void ATrainedContainerModelWithSevenClassesIsRecognised()
    {
        var spec = YoloOutputSpec.Infer([1, 11, 8400], expectedClassCount: 7);

        Assert.True(spec.ChannelsFirst);
        Assert.False(spec.HasObjectness);
        Assert.Equal(7, spec.ClassCount);
    }

    [Fact]
    public void ForcingTheFormatOverridesTheHeuristic()
    {
        var spec = YoloOutputSpec.Infer([1, 300, 6], expectedClassCount: 80, YoloOutputFormat.Yolov5);

        Assert.False(spec.IsEndToEnd);
        Assert.True(spec.HasObjectness);
    }

    [Fact]
    public void OffsetAddressesTheRightElementInBothLayouts()
    {
        var channelsFirst = YoloOutputSpec.Infer([1, 6, 4], expectedClassCount: 2);
        // [features, rows]: el valor (fila 2, feature 3) esta en 3 * 4 + 2 = 14.
        Assert.Equal(14, channelsFirst.Offset(2, 3));

        var rowsFirst = YoloOutputSpec.Infer([1, 4, 7], expectedClassCount: 2);
        // [rows, features]: el valor (fila 2, feature 3) esta en 2 * 7 + 3 = 17.
        Assert.Equal(17, rowsFirst.Offset(2, 3));
    }

    [Fact]
    public void RejectsShapesThatCannotBeInterpreted()
    {
        // Rango no soportado, y un tensor cuyas dos dimensiones son demasiado
        // pequenas para contener siquiera una caja.
        Assert.Throws<NotSupportedException>(() => YoloOutputSpec.Infer([1, 2, 3, 4, 5], 80));
        Assert.Throws<NotSupportedException>(() => YoloOutputSpec.Infer([1, 3, 4], 80));
    }

    [Fact]
    public void FallsBackToTheOtherOrientationWhenTheClassCountDoesNotMatch()
    {
        // Fichero de etiquetas equivocado (7 clases) contra un modelo de 2: la
        // orientacion preferida daria 3 valores por ancla, que es imposible, asi
        // que hay que interpretar la otra en lugar de fallar.
        var spec = YoloOutputSpec.Infer([1, 6, 3], expectedClassCount: 7);

        Assert.True(spec.ChannelsFirst);
        Assert.Equal(3, spec.Rows);
        Assert.Equal(2, spec.ClassCount);
    }

    [Fact]
    public void AModelWithManyAnchorsIsStillReadCorrectlyWithWrongLabels()
    {
        // El mismo desajuste sobre una forma realista no debe cambiar la lectura.
        var spec = YoloOutputSpec.Infer([1, 84, 8400], expectedClassCount: 7);

        Assert.True(spec.ChannelsFirst);
        Assert.Equal(8400, spec.Rows);
        Assert.Equal(80, spec.ClassCount);
    }
}

public class YoloOutputParserTests
{
    private static readonly LabelSet Labels = new(["container", "container-truck", "chassis"]);

    [Fact]
    public void ParsesAYolov8TensorAndConvertsCentresToCorners()
    {
        // Dos anclas, 3 clases: [1, 7, 2] en disposicion por canales.
        var spec = YoloOutputSpec.Infer([1, 7, 2], Labels.Count);
        var data = new float[7 * 2];

        // Ancla 0: caja centrada en (100, 200) de 40x60, clase "container" al 0.9.
        Write(data, spec, row: 0, [100f, 200f, 40f, 60f, 0.9f, 0.1f, 0.05f]);
        // Ancla 1: por debajo del umbral en todas las clases.
        Write(data, spec, row: 1, [300f, 300f, 20f, 20f, 0.05f, 0.02f, 0.01f]);

        var detections = YoloOutputParser.Parse(data, spec, Labels, confidenceThreshold: 0.25f);

        var detection = Assert.Single(detections);
        Assert.Equal("container", detection.Label);
        Assert.Equal(0.9f, detection.Score, 4);
        Assert.Equal(80f, detection.Box.Left, 3);
        Assert.Equal(170f, detection.Box.Top, 3);
        Assert.Equal(40f, detection.Box.Width, 3);
        Assert.Equal(60f, detection.Box.Height, 3);
    }

    [Fact]
    public void MultipliesObjectnessByClassScoreInTheYolov5Layout()
    {
        // Una ancla, 3 clases con objectness: [1, 1, 8] por filas.
        var spec = YoloOutputSpec.Infer([1, 1, 8], Labels.Count);
        Assert.True(spec.HasObjectness);

        var data = new float[8];
        Write(data, spec, row: 0, [50f, 50f, 10f, 10f, 0.8f, 0.5f, 0.1f, 0.05f]);

        var detection = Assert.Single(
            YoloOutputParser.Parse(data, spec, Labels, confidenceThreshold: 0.25f));

        // 0.8 de objectness por 0.5 de la mejor clase = 0.4.
        Assert.Equal(0.4f, detection.Score, 4);
        Assert.Equal("container", detection.Label);
    }

    [Fact]
    public void LowObjectnessDiscardsTheAnchorEvenWithAConfidentClass()
    {
        var spec = YoloOutputSpec.Infer([1, 1, 8], Labels.Count);
        var data = new float[8];
        Write(data, spec, row: 0, [50f, 50f, 10f, 10f, 0.05f, 0.99f, 0f, 0f]);

        Assert.Empty(YoloOutputParser.Parse(data, spec, Labels, confidenceThreshold: 0.25f));
    }

    [Fact]
    public void ParsesAnEndToEndTensorAsCorners()
    {
        var spec = YoloOutputSpec.Infer([1, 2, 6], expectedClassCount: 3, YoloOutputFormat.EndToEnd);
        var data = new float[2 * 6];

        Write(data, spec, row: 0, [10f, 20f, 60f, 90f, 0.77f, 1f]);
        Write(data, spec, row: 1, [10f, 20f, 60f, 90f, 0.05f, 0f]);

        var detection = Assert.Single(
            YoloOutputParser.Parse(data, spec, Labels, confidenceThreshold: 0.25f));

        Assert.Equal("container-truck", detection.Label);
        Assert.Equal(10f, detection.Box.Left, 3);
        Assert.Equal(50f, detection.Box.Width, 3);
        Assert.Equal(70f, detection.Box.Height, 3);
    }

    [Fact]
    public void KeepsOnlyTheRequestedTargetClasses()
    {
        var spec = YoloOutputSpec.Infer([1, 7, 3], Labels.Count);
        var data = new float[7 * 3];

        Write(data, spec, row: 0, [100f, 100f, 40f, 40f, 0.9f, 0f, 0f]);   // container
        Write(data, spec, row: 1, [200f, 200f, 40f, 40f, 0f, 0.9f, 0f]);   // container-truck
        Write(data, spec, row: 2, [300f, 300f, 40f, 40f, 0f, 0f, 0.9f]);   // chassis

        var targets = Labels.Resolve(["container", "chassis"]);
        var detections = YoloOutputParser.Parse(data, spec, Labels, 0.25f, targets);

        Assert.Equal(2, detections.Count);
        Assert.DoesNotContain(detections, detection => detection.Label == "container-truck");
    }

    [Fact]
    public void DegenerateBoxesAreDiscarded()
    {
        var spec = YoloOutputSpec.Infer([1, 7, 1], Labels.Count);
        var data = new float[7];
        Write(data, spec, row: 0, [100f, 100f, 0f, 40f, 0.9f, 0f, 0f]);

        Assert.Empty(YoloOutputParser.Parse(data, spec, Labels, 0.25f));
    }

    [Fact]
    public void RejectsATensorSmallerThanItsDeclaredShape()
    {
        var spec = YoloOutputSpec.Infer([1, 7, 10], Labels.Count);
        Assert.Throws<ArgumentException>(() => YoloOutputParser.Parse(new float[7], spec, Labels, 0.25f));
    }

    private static void Write(float[] data, YoloOutputSpec spec, int row, float[] features)
    {
        for (var i = 0; i < features.Length; i++)
        {
            data[spec.Offset(row, i)] = features[i];
        }
    }
}

public class LabelSetTests
{
    [Fact]
    public void CocoHasTheEightyStandardClassesInOrder()
    {
        Assert.Equal(80, LabelSet.Coco.Count);
        Assert.Equal("person", LabelSet.Coco.GetLabel(0));
        Assert.Equal("truck", LabelSet.Coco.GetLabel(7));
        Assert.Equal("boat", LabelSet.Coco.GetLabel(8));
        Assert.Equal("toothbrush", LabelSet.Coco.GetLabel(79));
    }

    [Fact]
    public void ResolveAcceptsNamesAndIndicesInterchangeably()
    {
        var resolved = LabelSet.Coco.Resolve(["truck", "8", "person"]);
        Assert.Equal(new[] { 0, 7, 8 }, resolved.OrderBy(id => id));
    }

    [Fact]
    public void ResolveIsCaseInsensitive()
        => Assert.Equal(new[] { 7 }, LabelSet.Coco.Resolve(["TRUCK"]));

    [Fact]
    public void ResolveReportsUnknownClasses()
    {
        var error = Assert.Throws<ArgumentException>(() => LabelSet.Coco.Resolve(["container"]));
        Assert.Contains("container", error.Message);
    }

    [Fact]
    public void CommentsAndBlankLinesAreIgnoredWhenLoadingLabels()
    {
        var labels = new LabelSet(["# clases", "", "container", "  ship  "]);

        Assert.Equal(2, labels.Count);
        Assert.Equal("container", labels.GetLabel(0));
        Assert.Equal("ship", labels.GetLabel(1));
    }

    [Fact]
    public void OutOfRangeIndicesFallBackToASyntheticName()
        => Assert.Equal("class_99", LabelSet.Coco.GetLabel(99));

    [Fact]
    public void AnEmptyLabelSetIsRejected()
        => Assert.Throws<ArgumentException>(() => new LabelSet([" ", "# solo comentarios"]));
}
