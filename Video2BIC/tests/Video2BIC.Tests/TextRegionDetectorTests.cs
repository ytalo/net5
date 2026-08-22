using OpenCvSharp;
using Video2BIC.Core.TextRegions;
using Xunit;

namespace Video2BIC.Tests;

/// <summary>
/// Comprueba que el localizador encuentra los rotulos de un contenedor sintetico
/// y que no se deja enganar por el corrugado del panel.
/// </summary>
public class TextRegionDetectorTests
{
    private readonly MserTextRegionDetector _detector = new();

    [Fact]
    public void FindsTheCodePaintedOnTheContainer()
    {
        using var scene = SyntheticContainer.Scene(640, 360, new Rect(60, 80, 480, 200), "MSKU1234565");

        var regions = _detector.Detect(scene);

        Assert.NotEmpty(regions);

        // El codigo se pinta a la altura de y=155 aproximadamente; alguna region
        // tiene que cubrirlo.
        Assert.Contains(regions, region => region.Box.Top <= 160 && region.Box.Bottom >= 150);
    }

    [Fact]
    public void ReturnsTheRegionsInReadingOrder()
    {
        using var scene = SyntheticContainer.Scene(640, 360, new Rect(60, 80, 480, 200), "MSKU1234565");

        var regions = _detector.Detect(scene);

        for (var index = 1; index < regions.Count; index++)
        {
            Assert.True(
                regions[index].Box.Y > regions[index - 1].Box.Y
                || (regions[index].Box.Y == regions[index - 1].Box.Y
                    && regions[index].Box.X >= regions[index - 1].Box.X),
                "Las regiones no salen en orden de lectura.");
        }
    }

    [Fact]
    public void DoesNotReportTextOnAPanelWithoutAny()
    {
        // Panel liso con corrugado pero sin rotulos: si el corrugado bastase para
        // disparar el detector, cada contenedor gastaria el presupuesto de OCR en
        // leer chapa.
        using var scene = new Mat(360, 640, MatType.CV_8UC3, new Scalar(96, 104, 112));
        SyntheticContainer.Draw(scene, new Rect(60, 80, 480, 200), string.Empty, sizeType: null);

        Assert.Empty(_detector.Detect(scene));
    }

    [Fact]
    public void FindsBothTheCodeAndTheSizeTypeLine()
    {
        using var scene = SyntheticContainer.Scene(640, 360, new Rect(60, 80, 480, 200), "MSKU1234565", "22G1");

        var regions = _detector.Detect(scene);

        // Dos lineas separadas verticalmente: la del codigo y la del tamano.
        var rows = regions.Select(region => region.Box.Y / 20).Distinct().Count();
        Assert.True(rows >= 2, $"Se esperaban al menos dos lineas de texto, se encontraron {rows}.");
    }

    [Fact]
    public void RespectsTheRegionBudget()
    {
        var detector = new MserTextRegionDetector(new TextRegionOptions { MaxRegions = 1 });
        using var scene = SyntheticContainer.Scene(640, 360, new Rect(60, 80, 480, 200), "MSKU1234565");

        Assert.True(detector.Detect(scene).Count <= 1);
    }

    [Fact]
    public void EveryRegionFitsInsideTheImage()
    {
        using var scene = SyntheticContainer.Scene(640, 360, new Rect(0, 0, 640, 360), "MSKU1234565");

        foreach (var region in _detector.Detect(scene))
        {
            Assert.True(region.Box.X >= 0 && region.Box.Y >= 0);
            Assert.True(region.Box.Right <= scene.Width && region.Box.Bottom <= scene.Height);
            Assert.InRange(region.Score, 0f, 1f);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 8)]
    public void IgnoresImagesTooSmallToContainText(int width, int height)
    {
        using var tiny = width == 0
            ? new Mat()
            : new Mat(height, width, MatType.CV_8UC3, Scalar.All(0));

        Assert.Empty(_detector.Detect(tiny));
    }

    [Fact]
    public void RejectsAnImpossibleConfiguration()
        => Assert.Throws<InvalidOperationException>(
            () => new MserTextRegionDetector(new TextRegionOptions { MaxRegions = 0 }));
}
