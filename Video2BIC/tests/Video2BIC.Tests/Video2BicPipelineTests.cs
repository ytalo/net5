using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Tracking;
using MaritimeVision.Core.Video;
using OpenCvSharp;
using Video2BIC.Core.Pipeline;
using Video2BIC.Core.Recognition;
using Xunit;

namespace Video2BIC.Tests;

/// <summary>
/// Recorre el camino completo (leer video, detectar, seguir, leer el codigo y
/// votar) sobre metraje sintetico, con un detector y un OCR simulados.
/// </summary>
public class Video2BicPipelineTests : IDisposable
{
    private const string Code = "MSKU1234565";

    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "video2bic-tests", Guid.NewGuid().ToString("n"));

    public Video2BicPipelineTests() => Directory.CreateDirectory(_workDirectory);

    private static ContainerBicRecognizer Recognizer(IReadOnlyList<string> responses, float confidence = 0.9f)
        => new(new ScriptedTextRecognizer(responses, confidence));

    private string Video(int frames = 40, [System.Runtime.CompilerServices.CallerMemberName] string name = "video")
        => SyntheticContainer.WriteVideo(Path.Combine(_workDirectory, $"{name}.mp4"), Code, frames);

    [Fact]
    public void IdentifiesTheContainerThatCrossesTheScene()
    {
        using var source = VideoSource.Open(Video());
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        var pipeline = new Video2BicPipeline(
            detector, new ByteTracker(), recognizer, options: new Video2BicOptions { Annotate = false });

        var report = pipeline.Run(source, Array.Empty<IVideoSink>());

        var identification = Assert.Single(report.Confirmed);
        Assert.Equal(Code, identification.Code.Value);
        Assert.True(identification.Observations >= 3);
        Assert.Equal(1, report.ContainersTracked);
        Assert.Equal(1d, report.IdentificationRate);
    }

    [Fact]
    public void ReportsTheIdentificationExactlyOnceAndAsSoonAsHappens()
    {
        using var source = VideoSource.Open(Video());
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        var pipeline = new Video2BicPipeline(
            detector, new ByteTracker(), recognizer, options: new Video2BicOptions { Annotate = false });

        var events = new List<ContainerIdentification>();
        var report = pipeline.Run(source, Array.Empty<IVideoSink>(), events.Add);

        var identification = Assert.Single(events);
        Assert.Equal(Code, identification.Code.Value);

        // Se avisa antes de terminar el video, no al final: es lo que necesita una
        // barrera o una grua para actuar sobre el contenedor que tiene delante.
        Assert.True(identification.LastFrame < report.FramesProcessed - 1);
    }

    [Fact]
    public void SurvivesAnOcrThatFailsMoreOftenThanItSucceeds()
    {
        // Cinco respuestas de cada seis son basura o codigos rotos; la correcta se
        // repite y acaba imponiendose.
        using var source = VideoSource.Open(Video(frames: 60));
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer(
        [
            "MAX GROSS 30480", Code, "TARE 2200", "MSKU1234500", Code, "?????", Code,
            "HAZMAT", Code,
        ]);

        var pipeline = new Video2BicPipeline(
            detector, new ByteTracker(), recognizer, options: new Video2BicOptions { Annotate = false });

        var report = pipeline.Run(source, Array.Empty<IVideoSink>());

        Assert.Equal(Code, Assert.Single(report.Confirmed).Code.Value);
    }

    [Fact]
    public void DoesNotIdentifyAnythingWhenTheOcrNeverReadsACode()
    {
        using var source = VideoSource.Open(Video());
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer(["MAX GROSS 30480 KG"]);

        var pipeline = new Video2BicPipeline(
            detector, new ByteTracker(), recognizer, options: new Video2BicOptions { Annotate = false });

        var report = pipeline.Run(source, Array.Empty<IVideoSink>());

        Assert.Empty(report.Confirmed);
        Assert.Equal(1, report.ContainersTracked);
        Assert.True(report.ReadAttempts > 0);
        Assert.Equal(0, report.ReadsWithCandidate);
    }

    [Fact]
    public void SpacesTheReadingsInsteadOfRunningOcrOnEveryFrame()
    {
        using var source = VideoSource.Open(Video(frames: 40));
        using var detector = new ScriptedContainerDetector();
        var textRecognizer = new ScriptedTextRecognizer(["MAX GROSS"]);
        using var recognizer = new ContainerBicRecognizer(textRecognizer, ownsRecognizer: false);

        var pipeline = new Video2BicPipeline(
            detector,
            new ByteTracker(),
            recognizer,
            options: new Video2BicOptions { Annotate = false, ReadIntervalFrames = 10 });

        var report = pipeline.Run(source, Array.Empty<IVideoSink>());

        // 40 fotogramas, una lectura cada 10: del orden de cuatro, no cuarenta.
        Assert.InRange(report.ReadAttempts, 3, 6);
        Assert.Equal(report.ReadAttempts, textRecognizer.Calls);
    }

    [Fact]
    public void StopsReadingAContainerOnceItsCodeIsConfirmed()
    {
        using var source = VideoSource.Open(Video(frames: 60));
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        var pipeline = new Video2BicPipeline(
            detector,
            new ByteTracker(),
            recognizer,
            options: new Video2BicOptions { Annotate = false, ReadIntervalFrames = 2 });

        var report = pipeline.Run(source, Array.Empty<IVideoSink>());

        Assert.Single(report.Confirmed);

        // Con lectura cada dos fotogramas sobre 60 habria mas de veinte intentos si
        // no se parase al confirmar.
        Assert.InRange(report.ReadAttempts, 3, 8);
    }

    [Fact]
    public void KeepReadingModeAccumulatesEvidenceBeyondTheConfirmation()
    {
        using var source = VideoSource.Open(Video(frames: 60));
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        var pipeline = new Video2BicPipeline(
            detector,
            new ByteTracker(),
            recognizer,
            options: new Video2BicOptions
            {
                Annotate = false,
                ReadIntervalFrames = 2,
                StopWhenConfirmed = false,
            });

        var report = pipeline.Run(source, Array.Empty<IVideoSink>());

        Assert.True(report.ReadAttempts > 10);
        Assert.True(Assert.Single(report.Confirmed).Observations > 10);
    }

    [Fact]
    public void IgnoresClassesThatAreNotContainers()
    {
        using var source = VideoSource.Open(Video());
        using var detector = new ScriptedContainerDetector(label: "truck");
        using var recognizer = Recognizer([Code]);

        var options = new Video2BicOptions { Annotate = false };
        options.ContainerLabels.Clear();
        options.ContainerLabels.Add("container");

        var pipeline = new Video2BicPipeline(detector, new ByteTracker(), recognizer, options: options);
        var report = pipeline.Run(source, Array.Empty<IVideoSink>());

        Assert.Equal(0, report.ContainersTracked);
        Assert.Equal(0, report.ReadAttempts);
    }

    [Fact]
    public void WritesAnAnnotatedVideoThatCanBeReadBack()
    {
        var output = Path.Combine(_workDirectory, "anotado.mp4");

        using var source = VideoSource.Open(Video(frames: 30));
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);
        var sink = VideoFileSink.Create(output, source.Info.Width, source.Info.Height, source.Info.Fps);

        var pipeline = new Video2BicPipeline(detector, new ByteTracker(), recognizer);
        var report = pipeline.Run(source, [sink]);

        sink.Dispose();

        Assert.Equal(30, report.FramesProcessed);
        Assert.Equal(30, sink.FramesWritten);
        Assert.True(new FileInfo(output).Length > 0);

        using var written = VideoSource.Open(output);
        Assert.Equal(source.Info.Width, written.Info.Width);
    }

    [Fact]
    public void AnnotatingChangesTheFrame()
    {
        var path = Video(frames: 6);

        using var source = VideoSource.Open(path);
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        var pipeline = new Video2BicPipeline(detector, new ByteTracker(), recognizer);

        Mat? annotated = null;
        foreach (var processed in pipeline.Process(source))
        {
            // El fotograma se reutiliza entre iteraciones, asi que hay que clonarlo.
            annotated = processed.Frame.Clone();
            break;
        }

        Assert.NotNull(annotated);

        using (annotated)
        using (var original = new Mat())
        using (var reread = VideoSource.Open(path))
        {
            Assert.True(reread.TryReadFrame(original));

            using var difference = new Mat();
            Cv2.Absdiff(original, annotated, difference);
            Assert.True(Cv2.Sum(difference).Val0 > 0d, "El renderizado no modifico el fotograma.");
        }
    }

    [Fact]
    public void MaxFramesStopsTheRunEarly()
    {
        using var source = VideoSource.Open(Video(frames: 50));
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        var pipeline = new Video2BicPipeline(
            detector,
            new ByteTracker(),
            recognizer,
            options: new Video2BicOptions { Annotate = false, MaxFrames = 10 });

        Assert.Equal(10, pipeline.Process(source).Count());
        Assert.Equal(10, pipeline.Report.FramesProcessed);
    }

    [Fact]
    public void CancellationKeepsWhatWasAlreadyIdentified()
    {
        using var source = VideoSource.Open(Video(frames: 50));
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        var pipeline = new Video2BicPipeline(
            detector,
            new ByteTracker(),
            recognizer,
            options: new Video2BicOptions { Annotate = false, ReadIntervalFrames = 1 });

        using var cancellation = new CancellationTokenSource();
        var processed = 0;

        foreach (var _ in pipeline.Process(source, cancellation.Token))
        {
            if (++processed == 20)
            {
                cancellation.Cancel();
            }
        }

        Assert.Equal(20, processed);
        Assert.Single(pipeline.Report.Confirmed);
    }

    [Fact]
    public void TheJsonReportCarriesEveryFieldOfTheIdentification()
    {
        using var source = VideoSource.Open(Video());
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        var pipeline = new Video2BicPipeline(
            detector, new ByteTracker(), recognizer, options: new Video2BicOptions { Annotate = false });

        var report = pipeline.Run(source, Array.Empty<IVideoSink>());
        report.Source = "prueba.mp4";

        var json = report.ToJson();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("prueba.mp4", root.GetProperty("source").GetString());
        Assert.Equal(report.FramesProcessed, root.GetProperty("framesProcessed").GetInt32());

        var container = Assert.Single(root.GetProperty("containers").EnumerateArray().ToList());
        Assert.Equal(Code, container.GetProperty("bicCode").GetString());
        Assert.Equal("MSK", container.GetProperty("ownerCode").GetString());
        Assert.Equal("123456", container.GetProperty("serialNumber").GetString());
        Assert.Equal(5, container.GetProperty("checkDigit").GetInt32());
        Assert.True(container.GetProperty("checkDigitValid").GetBoolean());
        Assert.True(container.GetProperty("confirmed").GetBoolean());
    }

    [Fact]
    public void TheCsvReportHasAHeaderAndOneRowPerContainer()
    {
        using var source = VideoSource.Open(Video());
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        var pipeline = new Video2BicPipeline(
            detector, new ByteTracker(), recognizer, options: new Video2BicOptions { Annotate = false });

        var report = pipeline.Run(source, Array.Empty<IVideoSink>());
        var lines = report.ToCsv().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("track_id,bic_code", lines[0], StringComparison.Ordinal);
        Assert.Equal(2, lines.Length);
        Assert.Contains(Code, lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnImpossibleConfiguration()
    {
        using var detector = new ScriptedContainerDetector();
        using var recognizer = Recognizer([Code]);

        Assert.Throws<InvalidOperationException>(() => new Video2BicPipeline(
            detector,
            new ByteTracker(),
            recognizer,
            options: new Video2BicOptions { ReadIntervalFrames = 0 }));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza best-effort: no debe hacer fallar la suite.
        }
    }
}
