using MaritimeVision.Core.Models;
using OpenCvSharp;
using Video2BIC.Core.Imaging;
using Xunit;

namespace Video2BIC.Tests;

public class RoiExtractorTests
{
    [Fact]
    public void AddsTheRequestedMarginAroundTheBox()
    {
        var box = new BoundingBox(100f, 100f, 200f, 100f);

        var rect = RoiExtractor.ToRect(box, 640, 480, margin: 0.1d);

        Assert.Equal(80, rect.X);
        Assert.Equal(90, rect.Y);
        Assert.Equal(240, rect.Width);
        Assert.Equal(120, rect.Height);
    }

    [Fact]
    public void ClipsTheMarginAtTheEdgesOfTheFrame()
    {
        // Un contenedor que entra por el borde no debe generar un recorte fuera de
        // la imagen.
        var box = new BoundingBox(-20f, 10f, 200f, 100f);

        var rect = RoiExtractor.ToRect(box, 300, 200, margin: 0.5d);

        Assert.Equal(0, rect.X);
        Assert.True(rect.Right <= 300);
        Assert.True(rect.Bottom <= 200);
    }

    [Fact]
    public void EnlargesASmallCropUpToTheTargetHeight()
    {
        using var frame = new Mat(480, 640, MatType.CV_8UC3, Scalar.All(64));

        using var roi = RoiExtractor.Extract(frame, new Rect(100, 100, 120, 60), targetHeight: 240, maxScale: 4d, out var scale);

        Assert.Equal(4d, scale, 3);
        Assert.Equal(240, roi.Height);
        Assert.Equal(480, roi.Width);
    }

    [Fact]
    public void NeverEnlargesBeyondTheAllowedScale()
    {
        using var frame = new Mat(480, 640, MatType.CV_8UC3, Scalar.All(64));

        using var roi = RoiExtractor.Extract(frame, new Rect(0, 0, 40, 20), targetHeight: 480, maxScale: 3d, out var scale);

        Assert.Equal(3d, scale, 3);
        Assert.Equal(60, roi.Height);
    }

    [Fact]
    public void LeavesABigEnoughCropUntouched()
    {
        using var frame = new Mat(480, 640, MatType.CV_8UC3, Scalar.All(64));

        using var roi = RoiExtractor.Extract(frame, new Rect(0, 0, 400, 300), targetHeight: 240, maxScale: 4d, out var scale);

        Assert.Equal(1d, scale, 3);
        Assert.Equal(300, roi.Height);
    }

    [Fact]
    public void ADegenerateBoxProducesAnEmptyCrop()
    {
        using var frame = new Mat(480, 640, MatType.CV_8UC3, Scalar.All(64));

        using var roi = RoiExtractor.Extract(frame, new Rect(10, 10, 0, 0));

        Assert.True(roi.Empty());
    }
}

public class TextImagePreprocessorTests
{
    private static Mat Line(Scalar background, Scalar ink)
    {
        var image = new Mat(60, 300, MatType.CV_8UC3, background);
        Cv2.PutText(image, "MSKU1234565", new Point(10, 42), HersheyFonts.HersheySimplex, 1d, ink, 2);
        return image;
    }

    [Fact]
    public void LeavesTheLineAsSingleChannelAtTheTargetHeight()
    {
        using var line = Line(Scalar.All(230), Scalar.All(20));

        using var prepared = TextImagePreprocessor.Prepare(line, new TextPreprocessOptions { MinHeight = 48, MaxHeight = 48 });

        Assert.Equal(1, prepared.Channels());
        Assert.Equal(48, prepared.Height);
        Assert.Equal(240, prepared.Width);
    }

    [Fact]
    public void TurnsLightTextOnADarkPanelIntoDarkTextOnALightOne()
    {
        // El codigo se pinta en blanco sobre azul tan a menudo como en negro sobre
        // beis, y un OCR entrenado espera lo segundo.
        using var light = Line(Scalar.All(230), Scalar.All(20));
        using var dark = Line(Scalar.All(30), Scalar.All(240));

        using var preparedLight = TextImagePreprocessor.Prepare(light);
        using var preparedDark = TextImagePreprocessor.Prepare(dark);

        // Tras normalizar, en los dos casos los trazos quedan mas oscuros que el
        // fondo: es la unica polaridad que un OCR entrenado sobre documentos espera.
        Assert.False(TextImagePreprocessor.IsLightTextOnDarkBackground(preparedLight));
        Assert.False(TextImagePreprocessor.IsLightTextOnDarkBackground(preparedDark));

        // El panel oscuro se ha invertido y el claro no, asi que los dos resultados
        // se parecen mucho mas entre si que las entradas.
        Assert.True(Cv2.Mean(preparedDark).Val0 > 128d);
        Assert.True(Cv2.Mean(preparedLight).Val0 > 128d);
    }

    [Fact]
    public void LeavesTheAlreadyCorrectPolarityAlone()
    {
        using var light = Line(Scalar.All(230), Scalar.All(20));

        using var untouched = TextImagePreprocessor.Prepare(
            light, new TextPreprocessOptions { NormalizePolarity = false, Deskew = false });
        using var normalized = TextImagePreprocessor.Prepare(
            light, new TextPreprocessOptions { NormalizePolarity = true, Deskew = false });

        using var difference = new Mat();
        Cv2.Absdiff(untouched, normalized, difference);
        Assert.Equal(0d, Cv2.Sum(difference).Val0);
    }

    [Fact]
    public void DetectsThePolarityOfALine()
    {
        using var light = Line(Scalar.All(230), Scalar.All(20));
        using var dark = Line(Scalar.All(30), Scalar.All(240));

        using var grayLight = new Mat();
        using var grayDark = new Mat();
        Cv2.CvtColor(light, grayLight, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(dark, grayDark, ColorConversionCodes.BGR2GRAY);

        Assert.False(TextImagePreprocessor.IsLightTextOnDarkBackground(grayLight));
        Assert.True(TextImagePreprocessor.IsLightTextOnDarkBackground(grayDark));
    }

    [Fact]
    public void BinarizingLeavesOnlyTwoLevels()
    {
        using var line = Line(Scalar.All(230), Scalar.All(20));

        using var prepared = TextImagePreprocessor.Prepare(
            line, new TextPreprocessOptions { Binarize = true });

        Cv2.MinMaxIdx(prepared, out double min, out double max);
        Assert.Equal(0d, min);
        Assert.Equal(255d, max);
    }

    [Fact]
    public void StraightensASlightlyTiltedLine()
    {
        using var line = Line(Scalar.All(230), Scalar.All(20));
        using var tilted = new Mat();

        using var rotation = Cv2.GetRotationMatrix2D(new Point2f(150f, 30f), 6d, 1d);
        Cv2.WarpAffine(line, tilted, rotation, line.Size(), InterpolationFlags.Cubic, BorderTypes.Replicate);

        using var straightened = TextImagePreprocessor.Prepare(
            tilted, new TextPreprocessOptions { Deskew = true, MinHeight = 0 });
        using var untouched = TextImagePreprocessor.Prepare(
            tilted, new TextPreprocessOptions { Deskew = false, MinHeight = 0 });

        Assert.Equal(untouched.Size(), straightened.Size());
        Assert.True(SkewOf(straightened) < SkewOf(untouched));

        static double SkewOf(Mat gray)
        {
            using var binary = new Mat();
            Cv2.Threshold(gray, binary, 0d, 255d, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

            using var points = new Mat();
            Cv2.FindNonZero(binary, points);

            var angle = (double)Cv2.MinAreaRect(points).Angle;
            return Math.Abs(angle < -45d ? angle + 90d : angle);
        }
    }

    [Fact]
    public void AnEmptyImageProducesAnEmptyResult()
    {
        using var empty = new Mat();
        using var prepared = TextImagePreprocessor.Prepare(empty);

        Assert.True(prepared.Empty());
    }
}
