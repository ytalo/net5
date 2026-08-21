using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Models;
using MaritimeVision.Core.Tracking;
using MaritimeVision.Core.Video;
using OpenCvSharp;
using Video2BIC.Core.Bic;
using Video2BIC.Core.Ocr;
using Video2BIC.Core.Pipeline;
using Video2BIC.Core.Recognition;
using Xunit;

namespace Video2BIC.Tests;

/// <summary>
/// Marca una prueba que necesita el binario de Tesseract instalado. Sin el, la
/// prueba se salta con una explicacion en vez de fallar: la suite tiene que pasar
/// en una maquina limpia.
/// </summary>
public sealed class RequiresTesseractFactAttribute : FactAttribute
{
    public RequiresTesseractFactAttribute()
    {
        if (!TesseractTextRecognizer.IsAvailable())
        {
            Skip = "Tesseract no esta instalado (apt install tesseract-ocr).";
        }
    }
}

/// <summary>
/// La interpretacion del TSV se prueba con salidas fijas: es analisis de texto y
/// no depende de que Tesseract este instalado.
/// </summary>
public class TesseractTsvTests
{
    private const string Header =
        "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext";

    [Fact]
    public void ReadsTheWordsWithTheirBoxAndConfidence()
    {
        var tsv = string.Join('\n',
            Header,
            "1\t1\t0\t0\t0\t0\t0\t0\t400\t200\t-1\t",
            "5\t1\t1\t1\t1\t1\t20\t30\t180\t28\t92.5\tMSKU",
            "5\t1\t1\t1\t2\t1\t20\t70\t220\t28\t88.1\t1234565");

        var fragments = TesseractTextRecognizer.ParseTsv(tsv, minConfidence: 0f);

        Assert.Equal(2, fragments.Count);

        Assert.Equal("MSKU", fragments[0].Text);
        Assert.Equal(0.925f, fragments[0].Confidence, 3);
        Assert.Equal(20, fragments[0].Left);
        Assert.Equal(30, fragments[0].Top);
        Assert.Equal(180, fragments[0].Width);
        Assert.Equal(28, fragments[0].Height);

        Assert.Equal("1234565", fragments[1].Text);
    }

    [Fact]
    public void IgnoresTheLevelsThatAreNotWords()
    {
        // Los niveles 1 a 4 son pagina, bloque, parrafo y linea: contenedores, no
        // texto. Tomarlos por palabras duplicaria cada lectura.
        var tsv = string.Join('\n',
            Header,
            "2\t1\t1\t0\t0\t0\t0\t0\t400\t200\t-1\t",
            "4\t1\t1\t1\t1\t0\t20\t30\t180\t28\t-1\t",
            "5\t1\t1\t1\t1\t1\t20\t30\t180\t28\t90\tMSKU");

        Assert.Single(TesseractTextRecognizer.ParseTsv(tsv, 0f));
    }

    [Fact]
    public void DropsTheWordsBelowTheConfidenceThreshold()
    {
        var tsv = string.Join('\n',
            Header,
            "5\t1\t1\t1\t1\t1\t0\t0\t10\t10\t90\tMSKU",
            "5\t1\t1\t1\t1\t2\t0\t0\t10\t10\t12\tXX",
            "5\t1\t1\t1\t1\t3\t0\t0\t10\t10\t-1\t");

        var fragments = TesseractTextRecognizer.ParseTsv(tsv, minConfidence: 0.5f);

        Assert.Equal("MSKU", Assert.Single(fragments).Text);
    }

    [Fact]
    public void SurvivesTruncatedOrEmptyOutput()
    {
        Assert.Empty(TesseractTextRecognizer.ParseTsv(string.Empty, 0f));
        Assert.Empty(TesseractTextRecognizer.ParseTsv(Header, 0f));
        Assert.Empty(TesseractTextRecognizer.ParseTsv("5\t1\t1\tincompleto", 0f));
    }

    [Fact]
    public void AcceptsWindowsLineEndings()
    {
        var tsv = string.Join("\r\n",
            Header,
            "5\t1\t1\t1\t1\t1\t20\t30\t180\t28\t92\tMSKU");

        Assert.Equal("MSKU", Assert.Single(TesseractTextRecognizer.ParseTsv(tsv, 0f)).Text);
    }
}

/// <summary>
/// Prueba de extremo a extremo del reconocimiento sobre una imagen de verdad: se
/// pinta un contenedor con su codigo y se comprueba que la cadena completa lo
/// devuelve.
/// </summary>
/// <remarks>
/// Se salta si Tesseract no esta instalado. Es la unica prueba de la suite que
/// depende de algo externo, y merece la pena: es la que demuestra que las piezas
/// encajan de verdad y no solo entre ellas.
/// </remarks>
public class TesseractRecognitionTests
{
    private static readonly BoundingBox PanelBox = new(80f, 120f, 700f, 300f);

    /// <summary>Tesseract sobre el recorte entero, dejandole a el la disposicion.</summary>
    private static ContainerBicRecognizer WholePanel()
        => new(new TesseractTextRecognizer(new TesseractOptions { PageSegmentationMode = 11 }));

    /// <summary>
    /// Se localizan las lineas primero y se le pasa a Tesseract cada una recortada y
    /// enderezada. Cuesta un proceso por linea, pero acierta bastante mas.
    /// </summary>
    private static ContainerBicRecognizer LineByLine()
        => new(new TesseractTextRecognizer(new TesseractOptions { PageSegmentationMode = 7 }));

    [RequiresTesseractFact]
    public void ReadsTheCodePaintedOnASyntheticContainer()
    {
        using var scene = SyntheticContainer.Scene(900, 500, new Rect(80, 120, 700, 300), "MSKU1234565");
        using var recognizer = LineByLine();

        var result = recognizer.Read(scene, PanelBox);

        Assert.True(
            result.HasCandidate,
            $"No se propuso ningun codigo. Texto reconocido: {Describe(result)}");

        Assert.Contains(result.Candidates, candidate => candidate.Code.Value == "MSKU1234565");
    }

    [RequiresTesseractFact]
    public void LocatesTheTwoLinesOfTheContainer()
    {
        using var scene = SyntheticContainer.Scene(900, 500, new Rect(80, 120, 700, 300), "MSKU1234565", "22G1");
        using var recognizer = LineByLine();

        var result = recognizer.Read(scene, PanelBox);

        Assert.True(result.Regions.Count >= 2, $"Regiones encontradas: {result.Regions.Count}.");
        Assert.NotEmpty(result.Fragments);
    }

    [RequiresTesseractFact]
    public void ReadsTheSizeTypeCodeToo()
    {
        using var scene = SyntheticContainer.Scene(900, 500, new Rect(80, 120, 700, 300), "MSKU1234565", "22G1");
        using var recognizer = LineByLine();

        var result = recognizer.Read(scene, PanelBox);

        Assert.NotNull(result.SizeType);
        Assert.Equal("22G1", result.SizeType.Value);
    }

    [RequiresTesseractFact]
    public void RefusesToInventACodeOnABlankPanel()
    {
        using var scene = SyntheticContainer.Scene(900, 500, new Rect(80, 120, 700, 300), string.Empty, null);
        using var recognizer = WholePanel();

        var result = recognizer.Read(scene, PanelBox);

        Assert.False(result.HasCandidate, $"Se invento un codigo: {Describe(result)}");
    }

    [RequiresTesseractFact]
    public void DoesNotTryToReadAContainerTooFarAway()
    {
        using var scene = SyntheticContainer.Scene(900, 500, new Rect(80, 120, 60, 40), "MSKU1234565", null);
        using var recognizer = WholePanel();

        var box = new BoundingBox(80f, 120f, 60f, 40f);

        Assert.False(recognizer.IsWorthReading(box));
        Assert.Same(BicReadingResult.Empty, recognizer.Read(scene, box));
    }

    [RequiresTesseractFact]
    public void EveryProposedCodePassesTheStandaloneCheckDigit()
    {
        using var scene = SyntheticContainer.Scene(900, 500, new Rect(80, 120, 700, 300), "CSQU3054383");
        using var recognizer = LineByLine();

        var result = recognizer.Read(scene, PanelBox);

        Assert.All(result.Candidates, candidate => Assert.True(BicCheckDigit.IsValid(candidate.Code.Value)));
    }

    [RequiresTesseractFact]
    public void ReadsTheCodeOfAContainerCrossingASyntheticVideo()
    {
        // La prueba que recorre la aplicacion entera tal como se usa: video, YOLO
        // (simulado), ByteTrack, MSER, Tesseract, ISO 6346 y votacion temporal.
        var directory = Path.Combine(Path.GetTempPath(), "video2bic-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);

        try
        {
            var path = SyntheticContainer.WriteVideo(
                Path.Combine(directory, "puerto.mp4"), "MSKU1234565", frames: 30);

            using var source = VideoSource.Open(path);
            using var detector = new ScriptedContainerDetector();
            using var recognizer = LineByLine();

            var pipeline = new Video2BicPipeline(
                detector,
                new ByteTracker(),
                recognizer,
                options: new Video2BicOptions { Annotate = false, ReadIntervalFrames = 4 });

            var report = pipeline.Run(source, Array.Empty<IVideoSink>());

            var identification = Assert.Single(report.Confirmed);
            Assert.Equal("MSKU1234565", identification.Code.Value);
            Assert.Equal("22G1", identification.SizeType?.Value);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Limpieza best-effort.
            }
        }
    }

    private static string Describe(BicReadingResult result)
        => result.Fragments.Count == 0
            ? "(ninguno)"
            : string.Join(" | ", result.Fragments.Select(fragment => fragment.Text));
}
