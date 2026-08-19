using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Models;
using MaritimeVision.Core.Pipeline;
using MaritimeVision.Core.Rendering;
using MaritimeVision.Core.Tracking;
using MaritimeVision.Core.Video;
using OpenCvSharp;
using Xunit;

namespace MaritimeVision.Tests;

/// <summary>
/// Ejercita el recorrido completo (leer video, detectar, seguir, dibujar,
/// escribir) sobre metraje sintetico y un detector simulado.
/// </summary>
public class PipelineIntegrationTests : IDisposable
{
    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "maritimevision-tests", Guid.NewGuid().ToString("n"));

    private static readonly SyntheticObject[] TwoContainers =
    [
        new(0, "container", StartX: 20, Y: 60, SpeedX: 5.5f, Width: 90, Height: 70),
        new(0, "container", StartX: 40, Y: 230, SpeedX: 4.0f, Width: 80, Height: 65),
    ];

    public PipelineIntegrationTests() => Directory.CreateDirectory(_workDirectory);

    [Fact]
    public void ReadsBackTheSyntheticVideoItJustWrote()
    {
        var path = SyntheticVideo.Write(Path.Combine(_workDirectory, "entrada.mp4"), TwoContainers, frames: 40);

        using var source = VideoSource.Open(path);

        Assert.Equal(640, source.Info.Width);
        Assert.Equal(360, source.Info.Height);
        Assert.Equal(25d, source.Info.Fps, 1);
        Assert.False(source.Info.IsLive);

        using var frame = new Mat();
        var frames = 0;
        while (source.TryReadFrame(frame))
        {
            Assert.False(frame.Empty());
            frames++;
        }

        Assert.Equal(40, frames);
    }

    [Fact]
    public void ProducesAnAnnotatedVideoWithStableIdentities()
    {
        const int frameCount = 60;
        var input = SyntheticVideo.Write(Path.Combine(_workDirectory, "puerto.mp4"), TwoContainers, frameCount);
        var output = Path.Combine(_workDirectory, "puerto-anotado.mp4");

        using var source = VideoSource.Open(input);
        using var detector = new ScriptedDetector(TwoContainers);
        using var sink = VideoFileSink.Create(
            output, source.Info.Width, source.Info.Height, source.Info.Fps);

        var pipeline = new VideoAnalyticsPipeline(detector, new ByteTracker(), new BoxRenderer());
        var stats = pipeline.Run(source, [sink]);

        Assert.Equal(frameCount, stats.FramesProcessed);
        Assert.Equal(frameCount, sink.FramesWritten);

        // Dos objetos reales deben producir exactamente dos identidades.
        Assert.Equal(2, stats.UniqueObjects);
        Assert.Equal(2, stats.UniqueObjectsByLabel["container"]);

        sink.Dispose();

        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0, "El video anotado quedo vacio.");

        // El fichero producido tiene que volver a abrirse y leerse.
        using var written = VideoSource.Open(output);
        Assert.Equal(source.Info.Width, written.Info.Width);
        Assert.Equal(source.Info.Height, written.Info.Height);
    }

    [Fact]
    public void TheAnnotatedFrameDiffersFromTheOriginal()
    {
        var input = SyntheticVideo.Write(Path.Combine(_workDirectory, "dibujo.mp4"), TwoContainers, frames: 12);

        using var source = VideoSource.Open(input);
        using var detector = new ScriptedDetector(TwoContainers);
        var pipeline = new VideoAnalyticsPipeline(detector, new ByteTracker(), new BoxRenderer());

        Mat? firstAnnotated = null;
        foreach (var annotated in pipeline.Process(source))
        {
            // El fotograma se reutiliza entre iteraciones, asi que hay que clonarlo.
            firstAnnotated = annotated.Frame.Clone();
            break;
        }

        Assert.NotNull(firstAnnotated);

        using (firstAnnotated)
        using (var original = new Mat())
        using (var reread = VideoSource.Open(input))
        {
            Assert.True(reread.TryReadFrame(original));

            using var difference = new Mat();
            Cv2.Absdiff(original, firstAnnotated, difference);

            // Si el renderizado hubiese sido un no-op, la diferencia seria nula.
            Assert.True(Cv2.Sum(difference).Val0 > 0d, "El renderizado no modifico el fotograma.");
        }
    }

    [Fact]
    public void KeepsIdentitiesThroughAFullOcclusionInTheMiddleOfTheSequence()
    {
        const int frameCount = 60;
        var input = SyntheticVideo.Write(Path.Combine(_workDirectory, "oclusion.mp4"), TwoContainers, frameCount);

        // El primer objeto desaparece del detector entre los fotogramas 25 y 33.
        using var detector = new ScriptedDetector(
            TwoContainers,
            (frame, index) => index == 0 && frame is >= 25 and <= 33 ? 0f : 0.9f);

        using var source = VideoSource.Open(input);
        var pipeline = new VideoAnalyticsPipeline(
            detector, new ByteTracker(), new BoxRenderer(), new PipelineOptions { Annotate = false });

        var idsByFrame = new List<int[]>();
        foreach (var annotated in pipeline.Process(source))
        {
            idsByFrame.Add(annotated.Analysis.Tracks.Select(track => track.TrackId).OrderBy(id => id).ToArray());
        }

        Assert.Equal(frameCount, idsByFrame.Count);

        // Durante la oclusion solo se ve un objeto...
        Assert.Single(idsByFrame[30]);

        // ...y al reaparecer se recuperan las dos identidades originales.
        Assert.Equal(2, idsByFrame[^1].Length);
        Assert.Equal(idsByFrame[20], idsByFrame[^1]);
        Assert.Equal(2, pipeline.Stats.UniqueObjects);
    }

    [Fact]
    public void PartiallyOccludedLowConfidenceDetectionsDoNotBreakTheTrack()
    {
        const int frameCount = 50;
        var input = SyntheticVideo.Write(Path.Combine(_workDirectory, "baja.mp4"), TwoContainers, frameCount);

        // La confianza del segundo objeto cae al 30% en la franja central.
        using var detector = new ScriptedDetector(
            TwoContainers,
            (frame, index) => index == 1 && frame is >= 15 and <= 35 ? 0.3f : 0.9f);

        using var source = VideoSource.Open(input);
        var pipeline = new VideoAnalyticsPipeline(
            detector, new ByteTracker(), new BoxRenderer(), new PipelineOptions { Annotate = false });

        var trackCounts = new List<int>();
        foreach (var annotated in pipeline.Process(source))
        {
            trackCounts.Add(annotated.Analysis.Tracks.Count);
        }

        // Los dos objetos siguen presentes durante toda la bajada de confianza.
        Assert.All(trackCounts.Skip(2), count => Assert.Equal(2, count));
        Assert.Equal(2, pipeline.Stats.UniqueObjects);
    }

    [Fact]
    public void MaxFramesStopsTheRunEarly()
    {
        var input = SyntheticVideo.Write(Path.Combine(_workDirectory, "corte.mp4"), TwoContainers, frames: 50);

        using var source = VideoSource.Open(input);
        using var detector = new ScriptedDetector(TwoContainers);
        var pipeline = new VideoAnalyticsPipeline(
            detector, new ByteTracker(), new BoxRenderer(), new PipelineOptions { MaxFrames = 10 });

        Assert.Equal(10, pipeline.Process(source).Count());
        Assert.Equal(10, pipeline.Stats.FramesProcessed);
    }

    [Fact]
    public void CancellationStopsTheRunAndStillClosesTheOutput()
    {
        var input = SyntheticVideo.Write(Path.Combine(_workDirectory, "cancela.mp4"), TwoContainers, frames: 50);
        var output = Path.Combine(_workDirectory, "cancela-out.mp4");

        using var source = VideoSource.Open(input);
        using var detector = new ScriptedDetector(TwoContainers);
        var sink = VideoFileSink.Create(output, source.Info.Width, source.Info.Height, source.Info.Fps);
        var pipeline = new VideoAnalyticsPipeline(detector, new ByteTracker(), new BoxRenderer());

        using var cancellation = new CancellationTokenSource();
        var processed = 0;

        foreach (var annotated in pipeline.Process(source, cancellation.Token))
        {
            sink.Write(annotated.Frame);
            if (++processed == 8)
            {
                cancellation.Cancel();
            }
        }

        sink.Dispose();

        Assert.Equal(8, processed);
        using var written = VideoSource.Open(output);
        Assert.Equal(source.Info.Width, written.Info.Width);
    }

    [Fact]
    public void StatsReportPlausibleTimings()
    {
        var input = SyntheticVideo.Write(Path.Combine(_workDirectory, "tiempos.mp4"), TwoContainers, frames: 30);

        using var source = VideoSource.Open(input);
        using var detector = new ScriptedDetector(TwoContainers);
        var pipeline = new VideoAnalyticsPipeline(detector, new ByteTracker(), new BoxRenderer());

        var sinks = Array.Empty<IVideoSink>();
        var stats = pipeline.Run(source, sinks);

        Assert.Equal(30, stats.FramesProcessed);
        Assert.Equal(60, stats.TotalDetections);
        Assert.True(stats.AverageFps > 0d);
        Assert.True(stats.AverageTrackingMs >= 0d);
        Assert.Contains("container", stats.ToString());
    }

    [Fact]
    public void OpeningAMissingFileFailsWithAClearError()
        => Assert.Throws<FileNotFoundException>(
            () => VideoSource.Open(Path.Combine(_workDirectory, "no-existe.mp4")));

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
