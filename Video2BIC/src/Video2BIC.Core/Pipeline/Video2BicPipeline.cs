using System.Diagnostics;
using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Models;
using OpenCvSharp;
using Video2BIC.Core.Recognition;
using Video2BIC.Core.Rendering;

namespace Video2BIC.Core.Pipeline;

/// <summary>
/// Encadena las cuatro etapas de Video2BIC: leer el video, detectar contenedores,
/// seguirlos y leerles el codigo BIC.
/// </summary>
/// <remarks>
/// <para>
/// El seguimiento no es un adorno. Sin el, cada fotograma seria un contenedor
/// nuevo y el mismo codigo se contaria decenas de veces; con el, todas las
/// lecturas de un contenedor caen bajo la misma identidad y pueden votarse entre
/// si. Es lo que permite salir de un OCR con aciertos del 60 % por fotograma y
/// llegar a una identificacion fiable por contenedor.
/// </para>
/// <para>
/// El OCR es la etapa cara, asi que no se ejecuta en todos los fotogramas ni
/// sobre todos los contenedores: se reparte un presupuesto por fotograma entre los
/// contenedores que mas prometen (los mas grandes y los menos intentados) y se deja
/// de gastar en los que ya estan confirmados.
/// </para>
/// </remarks>
public sealed class Video2BicPipeline
{
    private readonly IObjectDetector _detector;
    private readonly IObjectTracker _tracker;
    private readonly IBicRecognizer _recognizer;
    private readonly BicTrackAggregator _aggregator;
    private readonly BicOverlayRenderer _renderer;
    private readonly Video2BicOptions _options;
    private readonly HashSet<string> _containerLabels;

    private readonly Dictionary<int, int> _lastAttemptFrame = [];
    private readonly Dictionary<int, int> _attemptsByTrack = [];

    public Video2BicPipeline(
        IObjectDetector detector,
        IObjectTracker tracker,
        IBicRecognizer recognizer,
        BicTrackAggregator? aggregator = null,
        BicOverlayRenderer? renderer = null,
        Video2BicOptions? options = null)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _aggregator = aggregator ?? new BicTrackAggregator();
        _renderer = renderer ?? new BicOverlayRenderer();
        _options = options ?? new Video2BicOptions();
        _options.Validate();

        _containerLabels = new HashSet<string>(_options.ContainerLabels, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Informe de la ultima ejecucion.</summary>
    public Video2BicReport Report { get; private set; } = new();

    /// <summary>Votacion acumulada por contenedor.</summary>
    public BicTrackAggregator Aggregator => _aggregator;

    /// <summary>
    /// Recorre la secuencia devolviendo cada fotograma ya procesado y anotado.
    /// </summary>
    /// <param name="source">Fuente de video.</param>
    /// <param name="cancellationToken">Permite abortar la ejecucion.</param>
    public IEnumerable<Video2BicFrame> Process(
        IVideoSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        _tracker.Reset();
        _aggregator.Reset();
        _lastAttemptFrame.Clear();
        _attemptsByTrack.Clear();

        var report = new Video2BicReport();
        Report = report;

        var trackedIds = new HashSet<int>();
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

                var detectionStart = frameTimer.Elapsed;
                var detections = _detector.Detect(frame);
                var detectionTime = frameTimer.Elapsed - detectionStart;

                var trackingStart = frameTimer.Elapsed;
                var tracks = _tracker.Update(detections);
                var trackingTime = frameTimer.Elapsed - trackingStart;

                var timestamp = source.Info.Fps > 0d
                    ? TimeSpan.FromSeconds(frameIndex / source.Info.Fps)
                    : TimeSpan.Zero;

                var containers = tracks.Where(IsContainer).ToList();
                foreach (var container in containers)
                {
                    trackedIds.Add(container.TrackId);
                }

                var recognitionStart = frameTimer.Elapsed;
                var (readings, identified, regions) = ReadCodes(frame, containers, frameIndex, timestamp, report);
                var recognitionTime = frameTimer.Elapsed - recognitionStart;

                var analysis = new FrameAnalysis(
                    frameIndex, timestamp, detections, tracks, detectionTime, trackingTime);

                report.FramesProcessed++;
                report.DetectionTime += detectionTime;
                report.TrackingTime += trackingTime;
                report.RecognitionTime += recognitionTime;

                frameTimer.Stop();
                var instantFps = frameTimer.Elapsed.TotalSeconds > 0d ? 1d / frameTimer.Elapsed.TotalSeconds : 0d;
                smoothedFps = smoothedFps <= 0d ? instantFps : (smoothedFps * 0.9d) + (instantFps * 0.1d);

                if (_options.Annotate)
                {
                    _renderer.Draw(frame, containers, CurrentIdentifications(containers), regions, frameIndex, smoothedFps);
                }

                if (_options.ReportProgress && frameIndex % _options.ProgressInterval == 0)
                {
                    ReportProgress(source, frameIndex, smoothedFps, containers.Count, trackedIds.Count);
                }

                yield return new Video2BicFrame(frame, analysis, readings, identified, smoothedFps);

                frameIndex++;
            }
        }
        finally
        {
            stopwatch.Stop();
            report.Stream = SourceLabel(source);
            report.Elapsed = stopwatch.Elapsed;
            report.ContainersTracked = trackedIds.Count;
            report.Identifications = _aggregator.Results();
            frame.Dispose();
        }
    }

    /// <summary>
    /// Procesa la secuencia entera volcandola en los destinos indicados.
    /// </summary>
    /// <param name="source">Fuente de video.</param>
    /// <param name="sinks">
    /// Destinos de los fotogramas anotados. Si alguno devuelve <c>false</c> la
    /// ejecucion se detiene.
    /// </param>
    /// <param name="onIdentified">
    /// Se invoca cuando un contenedor queda identificado, en cuanto ocurre.
    /// </param>
    /// <param name="cancellationToken">Permite abortar la ejecucion.</param>
    public Video2BicReport Run(
        IVideoSource source,
        IReadOnlyList<IVideoSink> sinks,
        Action<ContainerIdentification>? onIdentified = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        foreach (var processed in Process(source, cancellationToken))
        {
            if (onIdentified is not null)
            {
                foreach (var identification in processed.NewlyIdentified)
                {
                    onIdentified(identification);
                }
            }

            var keepGoing = true;
            foreach (var sink in sinks)
            {
                keepGoing &= sink.Write(processed.Frame);
            }

            if (!keepGoing)
            {
                break;
            }
        }

        return Report;
    }

    private bool IsContainer(TrackedObject track)
        => _containerLabels.Count == 0 || _containerLabels.Contains(track.Label);

    /// <summary>
    /// Elige a que contenedores se les intenta leer el codigo en este fotograma y
    /// acumula lo que salga.
    /// </summary>
    private (List<BicReading> Readings, List<ContainerIdentification> Identified, List<Rect> Regions) ReadCodes(
        Mat frame,
        IReadOnlyList<TrackedObject> containers,
        int frameIndex,
        TimeSpan timestamp,
        Video2BicReport report)
    {
        var readings = new List<BicReading>();
        var identified = new List<ContainerIdentification>();
        var regions = new List<Rect>();

        var budget = _options.MaxReadsPerFrame;
        if (budget <= 0 || containers.Count == 0)
        {
            return (readings, identified, regions);
        }

        // Se atiende primero a quien menos veces se ha intentado y, a igualdad, al
        // contenedor mas grande: el que ocupa mas pixeles es el que mejor se lee.
        var queue = containers
            .Where(container => ShouldRead(container, frameIndex))
            .OrderBy(container => _attemptsByTrack.GetValueOrDefault(container.TrackId))
            .ThenByDescending(container => container.Box.Area)
            .Take(budget);

        foreach (var container in queue)
        {
            _lastAttemptFrame[container.TrackId] = frameIndex;
            _attemptsByTrack[container.TrackId] = _attemptsByTrack.GetValueOrDefault(container.TrackId) + 1;

            var result = _recognizer.Read(frame, container.Box);
            report.ReadAttempts++;

            foreach (var region in result.Regions)
            {
                regions.Add(result.ToFrame(region));
            }

            if (!result.HasCandidate)
            {
                continue;
            }

            report.ReadsWithCandidate++;

            foreach (var candidate in result.Candidates)
            {
                var reading = new BicReading(container.TrackId, frameIndex, timestamp, candidate, result.SizeType);
                readings.Add(reading);

                if (_aggregator.Observe(reading) is { } identification)
                {
                    identified.Add(identification);
                }
            }
        }

        return (readings, identified, regions);
    }

    private bool ShouldRead(TrackedObject container, int frameIndex)
    {
        // Una caja que viene solo de la prediccion de Kalman no tiene pixeles
        // frescos debajo: leerla es leer el fondo.
        if (!container.IsConfirmed || container.HitStreak < _options.MinHitStreak)
        {
            return false;
        }

        if (_options.StopWhenConfirmed && _aggregator.IsConfirmed(container.TrackId))
        {
            return false;
        }

        if (_options.MaxReadsPerTrack > 0
            && _attemptsByTrack.GetValueOrDefault(container.TrackId) >= _options.MaxReadsPerTrack)
        {
            return false;
        }

        return !_lastAttemptFrame.TryGetValue(container.TrackId, out var last)
               || frameIndex - last >= _options.ReadIntervalFrames;
    }

    private Dictionary<int, ContainerIdentification> CurrentIdentifications(IReadOnlyList<TrackedObject> containers)
    {
        var identifications = new Dictionary<int, ContainerIdentification>(containers.Count);

        foreach (var container in containers)
        {
            if (_aggregator.Get(container.TrackId) is { } identification)
            {
                identifications[container.TrackId] = identification;
            }
        }

        return identifications;
    }

    private static string SourceLabel(IVideoSource source)
        => $"{source.Info.Width}x{source.Info.Height} @ {source.Info.Fps:F2} FPS";

    private static void ReportProgress(
        IVideoSource source, int frameIndex, double fps, int visible, int tracked)
    {
        var total = source.Info.FrameCount;
        var progress = total is > 0
            ? $"{frameIndex}/{total.Value} ({(double)frameIndex / total.Value:P0})"
            : $"{frameIndex}";

        Console.WriteLine(
            $"  fotograma {progress}  {fps:F1} FPS  contenedores en escena: {visible}  vistos: {tracked}");
    }
}
