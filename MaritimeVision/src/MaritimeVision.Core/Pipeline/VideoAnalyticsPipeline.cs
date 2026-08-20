using System.Diagnostics;
using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Models;
using MaritimeVision.Core.Rendering;
using OpenCvSharp;

namespace MaritimeVision.Core.Pipeline;

/// <summary>Fotograma ya anotado junto con su analisis.</summary>
/// <param name="Frame">
/// Fotograma BGR. <b>Se reutiliza entre iteraciones</b>: quien lo consuma debe
/// clonarlo si necesita conservarlo mas alla de la iteracion actual.
/// </param>
/// <param name="Analysis">Detecciones y tracks de ese fotograma.</param>
/// <param name="Fps">FPS de proceso instantaneos, suavizados.</param>
public readonly record struct AnnotatedFrame(Mat Frame, FrameAnalysis Analysis, double Fps);

/// <summary>
/// Encadena lectura de video, deteccion, seguimiento y anotacion.
/// </summary>
/// <remarks>
/// Expone dos formas de consumo: <see cref="Process"/> devuelve los fotogramas uno
/// a uno (lo que necesita el visor web para emitir MJPEG) y <see cref="Run"/>
/// vuelca la secuencia completa en uno o varios destinos.
/// </remarks>
public sealed class VideoAnalyticsPipeline
{
    private readonly IObjectDetector _detector;
    private readonly IObjectTracker _tracker;
    private readonly BoxRenderer _renderer;
    private readonly PipelineOptions _options;

    public VideoAnalyticsPipeline(
        IObjectDetector detector,
        IObjectTracker tracker,
        BoxRenderer? renderer = null,
        PipelineOptions? options = null)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _renderer = renderer ?? new BoxRenderer();
        _options = options ?? new PipelineOptions();
    }

    /// <summary>Estadisticas de la ultima ejecucion.</summary>
    public PipelineStats Stats { get; private set; } = new();

    /// <summary>
    /// Recorre la secuencia devolviendo cada fotograma ya anotado.
    /// </summary>
    /// <param name="source">Fuente de video.</param>
    /// <param name="cancellationToken">Permite abortar la reproduccion.</param>
    public IEnumerable<AnnotatedFrame> Process(
        IVideoSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        _tracker.Reset();
        _renderer.Reset();

        var stats = new PipelineStats();
        Stats = stats;

        var frame = new Mat();
        var stopwatch = Stopwatch.StartNew();
        var frameTimer = new Stopwatch();
        var smoothedFps = 0d;
        var frameIndex = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_options.MaxFrames > 0 && frameIndex >= _options.MaxFrames)
                {
                    break;
                }

                if (!source.TryReadFrame(frame))
                {
                    break;
                }

                frameTimer.Restart();

                var inferenceStart = frameTimer.Elapsed;
                var detections = _detector.Detect(frame);
                var inferenceTime = frameTimer.Elapsed - inferenceStart;

                var trackingStart = frameTimer.Elapsed;
                var tracks = _tracker.Update(detections);
                var trackingTime = frameTimer.Elapsed - trackingStart;

                var timestamp = source.Info.Fps > 0d
                    ? TimeSpan.FromSeconds(frameIndex / source.Info.Fps)
                    : TimeSpan.Zero;

                var analysis = new FrameAnalysis(
                    frameIndex, timestamp, detections, tracks, inferenceTime, trackingTime);

                stats.FramesProcessed++;
                stats.TotalInferenceTime += inferenceTime;
                stats.TotalTrackingTime += trackingTime;
                stats.TotalDetections += detections.Count;
                foreach (var track in tracks)
                {
                    stats.RegisterTrack(track.Label, track.TrackId);
                }

                frameTimer.Stop();
                var instantFps = frameTimer.Elapsed.TotalSeconds > 0d
                    ? 1d / frameTimer.Elapsed.TotalSeconds
                    : 0d;

                // Media movil exponencial: el valor instantaneo oscila demasiado
                // para mostrarlo directamente en pantalla.
                smoothedFps = smoothedFps <= 0d ? instantFps : (smoothedFps * 0.9d) + (instantFps * 0.1d);

                if (_options.Annotate)
                {
                    _renderer.Draw(frame, analysis, smoothedFps);
                }

                if (_options.ReportProgress && frameIndex % _options.ProgressInterval == 0)
                {
                    ReportProgress(source, frameIndex, smoothedFps, tracks.Count);
                }

                yield return new AnnotatedFrame(frame, analysis, smoothedFps);

                frameIndex++;
            }
        }
        finally
        {
            stopwatch.Stop();
            stats.Elapsed = stopwatch.Elapsed;
            frame.Dispose();
        }
    }

    /// <summary>
    /// Procesa la secuencia entera volcandola en los destinos indicados.
    /// </summary>
    /// <param name="source">Fuente de video.</param>
    /// <param name="sinks">
    /// Destinos de los fotogramas anotados. Si alguno devuelve <c>false</c> la
    /// ejecucion se detiene (es como la ventana senala que el usuario quiere salir).
    /// </param>
    /// <param name="cancellationToken">Permite abortar la ejecucion.</param>
    public PipelineStats Run(
        IVideoSource source,
        IReadOnlyList<IVideoSink> sinks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        foreach (var annotated in Process(source, cancellationToken))
        {
            var keepGoing = true;
            foreach (var sink in sinks)
            {
                keepGoing &= sink.Write(annotated.Frame);
            }

            if (!keepGoing)
            {
                break;
            }
        }

        return Stats;
    }

    private static void ReportProgress(IVideoSource source, int frameIndex, double fps, int trackCount)
    {
        var total = source.Info.FrameCount;
        var progress = total is > 0
            ? $"{frameIndex}/{total.Value} ({(double)frameIndex / total.Value:P0})"
            : $"{frameIndex}";

        Console.WriteLine($"  fotograma {progress}  {fps:F1} FPS  tracks activos: {trackCount}");
    }
}
