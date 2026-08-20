using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Detectors;
using MaritimeVision.Core.Pipeline;
using MaritimeVision.Core.Rendering;
using MaritimeVision.Core.Tracking;
using MaritimeVision.Core.Video;

namespace MaritimeVision.Cli;

/// <summary>Punto de entrada de la herramienta de linea de comandos.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var command = CommandLine.Parse(args);

            if (command.Command.Length == 0 || command.HasFlag("help"))
            {
                PrintUsage();
                return command.Command.Length == 0 && args.Length > 0 ? 1 : 0;
            }

            var exitCode = command.Command.ToLowerInvariant() switch
            {
                "play" => RunPlay(command),
                "export" => RunExport(command),
                "info" => RunInfo(command),
                _ => UnknownCommand(command.Command),
            };

            WarnAboutUnknownOptions(command);
            return exitCode;
        }
        catch (CommandLineException ex)
        {
            Console.Error.WriteLine($"Error de uso: {ex.Message}");
            Console.Error.WriteLine("Ejecuta 'mvision --help' para ver las opciones disponibles.");
            return 2;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Reproduce la secuencia anotada en una ventana, y opcionalmente la guarda.</summary>
    private static int RunPlay(CommandLine command)
    {
        var source = command.GetRequiredString("source");
        using var video = VideoSource.Open(source);
        PrintStreamInfo(source, video.Info);

        using var detector = new YoloDetector(AppBuilder.BuildDetectorOptions(command));
        detector.Warmup();
        Console.WriteLine($"Modelo:   {detector.Description}");
        Console.WriteLine();

        var sinks = new List<IVideoSink>();
        var windowOpened = WindowSink.TryCreate("MaritimeVision", video.Info.Fps, out var window, out var error);

        if (windowOpened && window is not null)
        {
            sinks.Add(window);
            Console.WriteLine("Reproduciendo. Espacio pausa, 'q' o Esc cierra.");
        }
        else
        {
            Console.WriteLine($"[aviso] No hay entorno grafico disponible ({error}).");
            Console.WriteLine("        Usa 'export --output salida.mp4' o el visor web MaritimeVision.Web.");
        }

        var output = command.GetString("output");
        VideoFileSink? fileSink = null;
        if (output is not null)
        {
            fileSink = VideoFileSink.Create(
                output, video.Info.Width, video.Info.Height, video.Info.Fps, command.GetString("codec", "mp4v")!);
            sinks.Add(fileSink);
            Console.WriteLine($"Guardando ademas en '{output}'.");
        }

        if (sinks.Count == 0)
        {
            Console.Error.WriteLine("Error: sin ventana ni fichero de salida no hay nada que reproducir.");
            return 1;
        }

        try
        {
            return RunPipeline(command, video, detector, sinks);
        }
        finally
        {
            foreach (var sink in sinks)
            {
                sink.Dispose();
            }

            fileSink?.Dispose();
        }
    }

    /// <summary>Escribe la secuencia anotada en un fichero de video.</summary>
    private static int RunExport(CommandLine command)
    {
        var source = command.GetRequiredString("source");
        var output = command.GetRequiredString("output");

        using var video = VideoSource.Open(source);
        PrintStreamInfo(source, video.Info);

        using var detector = new YoloDetector(AppBuilder.BuildDetectorOptions(command));
        detector.Warmup();
        Console.WriteLine($"Modelo:   {detector.Description}");

        using var sink = VideoFileSink.Create(
            output, video.Info.Width, video.Info.Height, video.Info.Fps, command.GetString("codec", "mp4v")!);

        Console.WriteLine($"Salida:   {output}");
        Console.WriteLine();

        var exitCode = RunPipeline(command, video, detector, [sink]);
        Console.WriteLine($"Escritos {sink.FramesWritten} fotogramas en '{output}'.");
        return exitCode;
    }

    /// <summary>Muestra los metadatos de la secuencia y, si se indica, del modelo.</summary>
    private static int RunInfo(CommandLine command)
    {
        var source = command.GetString("source");
        if (source is not null)
        {
            using var video = VideoSource.Open(source);
            PrintStreamInfo(source, video.Info);
        }

        if (command.GetString("model") is not null)
        {
            using var detector = new YoloDetector(AppBuilder.BuildDetectorOptions(command));
            Console.WriteLine($"Modelo:   {detector.Description}");
            Console.WriteLine($"Entrada:  {detector.InputSize.Width}x{detector.InputSize.Height}");
            Console.WriteLine($"Clases:   {string.Join(", ", detector.Labels)}");
        }

        if (source is null && command.GetString("model") is null)
        {
            throw new CommandLineException("'info' necesita al menos --source o --model.");
        }

        return 0;
    }

    private static int RunPipeline(
        CommandLine command,
        IVideoSource video,
        IObjectDetector detector,
        IReadOnlyList<IVideoSink> sinks)
    {
        var tracker = new ByteTracker(AppBuilder.BuildTrackerOptions(command, video.Info.Fps));
        var renderer = new BoxRenderer(AppBuilder.BuildRenderOptions(command));

        var pipeline = new VideoAnalyticsPipeline(detector, tracker, renderer, new PipelineOptions
        {
            MaxFrames = command.GetInt("max-frames", 0),
            ReportProgress = !command.HasFlag("quiet"),
            ProgressInterval = command.GetInt("progress-every", 30),
        });

        // Ctrl+C debe cerrar los ficheros de salida correctamente en vez de
        // dejar un video truncado e ilegible.
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Interrumpido; cerrando la salida...");
            cancellation.Cancel();
        };

        var stats = pipeline.Run(video, sinks, cancellation.Token);

        Console.WriteLine();
        Console.WriteLine(stats.ToString());
        return 0;
    }

    private static void PrintStreamInfo(string source, VideoStreamInfo info)
    {
        var duration = info.Duration is { } value ? $", {value:hh\\:mm\\:ss}" : string.Empty;
        var frames = info.FrameCount is { } count ? $", {count} fotogramas" : ", en vivo";

        Console.WriteLine($"Entrada:  {source}");
        Console.WriteLine($"          {info.Width}x{info.Height} a {info.Fps:F2} FPS{frames}{duration}");
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Comando desconocido: '{command}'.");
        PrintUsage();
        return 2;
    }

    private static void WarnAboutUnknownOptions(CommandLine command)
    {
        var unknown = command.UnknownOptions();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine(
                $"[aviso] Opciones no reconocidas, ignoradas: {string.Join(", ", unknown.Select(name => "--" + name))}");
        }
    }

    private static void PrintUsage() => Console.WriteLine(
        """
        MaritimeVision - deteccion y seguimiento de contenedores maritimos (YOLO + ByteTrack)

        USO
          mvision play   --source <ruta|url|indice> --model <modelo.onnx> [opciones]
          mvision export --source <ruta|url|indice> --model <modelo.onnx> --output <salida.mp4> [opciones]
          mvision info   [--source <ruta|url|indice>] [--model <modelo.onnx>]

        ENTRADA Y SALIDA
          -s, --source <valor>     Fichero de video, URL rtsp://|http://, o indice de camara (0, 1...).
          -o, --output <ruta>      Fichero de video anotado. Obligatorio en 'export'.
              --codec <fourcc>     Codec de salida. Por defecto mp4v; alternativas: avc1, XVID, MJPG.
              --max-frames <n>     Procesar solo los primeros n fotogramas.
              --quiet              No escribir el progreso.

        MODELO
          -m, --model <ruta>       Modelo YOLO exportado a ONNX.
          -l, --labels <ruta>      Fichero de etiquetas, una por linea. Por defecto: COCO.
          -c, --classes <lista>    Clases de interes separadas por comas (nombres o indices).
              --conf <0-1>         Confianza minima de deteccion. Por defecto 0.25.
              --iou <0-1>          Umbral de IoU de la NMS. Por defecto 0.45.
              --imgsz <n>          Lado de entrada si el modelo es dinamico. Por defecto 640.
              --max-det <n>        Detecciones maximas por fotograma. Por defecto 300.
              --agnostic-nms       Aplicar la NMS ignorando la clase.
              --format <valor>     auto | yolov8 | yolov5 | e2e. Por defecto auto.
              --provider <valor>   cpu | cuda | directml. Por defecto cpu.
              --device <n>         Indice de GPU. Por defecto 0.
              --threads <n>        Hilos de inferencia. Por defecto, automatico.

        SEGUIMIENTO (ByteTrack)
              --track-thresh <0-1>     Frontera entre confianza alta y baja. Por defecto 0.5.
              --low-thresh <0-1>       Confianza minima aprovechable. Por defecto 0.1.
              --new-track-thresh <0-1> Confianza para abrir un track. Por defecto track-thresh + 0.1.
              --match-thresh <0-1>     Distancia IoU maxima al asociar. Por defecto 0.8.
              --track-buffer <n>       Fotogramas que un track sobrevive ocluido. Por defecto 30.
              --predict-frames <n>     Publicar la caja extrapolada n fotogramas. Por defecto 0.
              --class-agnostic         Permitir que un track cambie de clase.

        PRESENTACION
              --color-by <valor>   track | class. Por defecto track.
              --thickness <n>      Grosor del borde. Por defecto 2.
              --trail-length <n>   Longitud de la estela. Por defecto 30.
              --no-labels          No dibujar las etiquetas.
              --no-trails          No dibujar las estelas.
              --no-hud             No dibujar el panel superior.
              --show-detections    Dibujar tambien las detecciones crudas.

        EJEMPLOS
          Reproducir un video con las cajas de contenedor:
            mvision play -s puerto.mp4 -m models/containers.onnx -l labels/maritime-container.names -c container

          Exportar el video anotado sin necesidad de entorno grafico:
            mvision export -s puerto.mp4 -m models/containers.onnx -o out/puerto-anotado.mp4

          Analizar una camara IP en vivo:
            mvision play -s rtsp://camara.puerto.local/stream -m models/containers.onnx --track-buffer 60

        Durante la reproduccion: espacio pausa, 'q' o Esc cierra.
        """);
}
