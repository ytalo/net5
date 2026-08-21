using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Detectors;
using MaritimeVision.Core.Tracking;
using MaritimeVision.Core.Video;
using Video2BIC.Core.Bic;
using Video2BIC.Core.Ocr;
using Video2BIC.Core.Pipeline;
using Video2BIC.Core.Recognition;
using Video2BIC.Core.Rendering;

namespace Video2BIC.Cli;

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
                "scan" => RunScan(command),
                "check" => RunCheck(command),
                "decode" => RunDecode(command),
                "info" => RunInfo(command),
                _ => UnknownCommand(command.Command),
            };

            WarnAboutUnknownOptions(command);
            return exitCode;
        }
        catch (CommandLineException ex)
        {
            Console.Error.WriteLine($"Error de uso: {ex.Message}");
            Console.Error.WriteLine("Ejecuta 'video2bic --help' para ver las opciones disponibles.");
            return 2;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Analiza una secuencia de video: detecta contenedores, los sigue y les lee el
    /// codigo BIC.
    /// </summary>
    private static int RunScan(CommandLine command)
    {
        var source = command.GetRequiredString("source");

        using var video = VideoSource.Open(source);
        PrintStreamInfo(source, video.Info);

        using var detector = new YoloDetector(AppBuilder.BuildDetectorOptions(command));
        detector.Warmup();
        Console.WriteLine($"Detector: {detector.Description}");

        using var textRecognizer = AppBuilder.BuildTextRecognizer(command);
        using var bicRecognizer = AppBuilder.BuildBicRecognizer(command, textRecognizer);
        Console.WriteLine($"OCR:      {textRecognizer.Description}");

        var sinks = new List<IVideoSink>();
        VideoFileSink? fileSink = null;
        WindowSink? window = null;

        try
        {
            var output = command.GetString("output");
            if (output is not null)
            {
                fileSink = VideoFileSink.Create(
                    output, video.Info.Width, video.Info.Height, video.Info.Fps,
                    command.GetString("codec", "mp4v")!);
                sinks.Add(fileSink);
                Console.WriteLine($"Salida:   {output}");
            }

            if (command.HasFlag("window"))
            {
                if (WindowSink.TryCreate("Video2BIC", video.Info.Fps, out window, out var error) && window is not null)
                {
                    sinks.Add(window);
                    Console.WriteLine("Reproduciendo. Espacio pausa, 'q' o Esc cierra.");
                }
                else
                {
                    Console.Error.WriteLine($"[aviso] No hay entorno grafico disponible ({error}).");
                }
            }

            Console.WriteLine();

            var report = RunPipeline(command, video, detector, bicRecognizer, sinks);
            report.Source = source;

            Console.WriteLine();
            Console.WriteLine(report.ToString());

            WriteReportFiles(command, report);

            if (fileSink is not null)
            {
                Console.WriteLine($"Escritos {fileSink.FramesWritten} fotogramas en '{fileSink.Path}'.");
            }

            // Sin ningun contenedor identificado la ejecucion no ha fallado, pero el
            // que la invoca desde un script necesita distinguirlo.
            return report.Confirmed.Any() ? 0 : 3;
        }
        finally
        {
            foreach (var sink in sinks)
            {
                sink.Dispose();
            }
        }
    }

    /// <summary>Valida un codigo BIC escrito a mano, o le calcula el digito de control.</summary>
    private static int RunCheck(CommandLine command)
    {
        var text = command.Positional.FirstOrDefault()
                   ?? command.GetString("code")
                   ?? throw new CommandLineException("'check' necesita el codigo: video2bic check CSQU3054383");

        var normalized = new string(text.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        // Con diez caracteres se entiende que falta el digito de control y se calcula.
        if (normalized.Length == BicCheckDigit.PrefixLength)
        {
            if (!BicCheckDigit.TryCompute(normalized, out var digit))
            {
                Console.Error.WriteLine($"'{text}' no es un prefijo valido de codigo BIC.");
                return 1;
            }

            Console.WriteLine($"{normalized}{digit}");
            Console.WriteLine($"Digito de control calculado: {digit}");
            return 0;
        }

        if (!BicCode.TryParse(normalized, out var code))
        {
            Console.Error.WriteLine(
                $"'{text}' no tiene la forma de un codigo BIC " +
                "(3 letras de propietario, categoria U/J/Z, 6 digitos de serie y 1 de control).");
            return 1;
        }

        Console.WriteLine($"Codigo:      {code.ToDisplayString()}");
        Console.WriteLine($"Propietario: {code.OwnerCode}");
        Console.WriteLine($"Categoria:   {code.CategoryIdentifier} ({DescribeCategory(code.Category)})");
        Console.WriteLine($"Serie:       {code.SerialNumber}");
        Console.WriteLine($"Control:     {code.CheckDigit} (esperado {code.ExpectedCheckDigit})");
        Console.WriteLine(code.HasValidCheckDigit
            ? "Resultado:   VALIDO"
            : "Resultado:   INVALIDO, el digito de control no cuadra");

        return code.HasValidCheckDigit ? 0 : 1;
    }

    /// <summary>Interpreta un codigo de tamano y tipo ISO 6346.</summary>
    private static int RunDecode(CommandLine command)
    {
        var text = command.Positional.FirstOrDefault()
                   ?? command.GetString("code")
                   ?? throw new CommandLineException("'decode' necesita el codigo: video2bic decode 22G1");

        if (!SizeTypeCode.TryParse(text, out var code))
        {
            Console.Error.WriteLine($"'{text}' no es un codigo de tamano y tipo valido (por ejemplo 22G1, 45R1).");
            return 1;
        }

        Console.WriteLine($"Codigo:   {code.Value}");
        Console.WriteLine($"Longitud: {code.LengthDescription}");
        Console.WriteLine($"Altura:   {code.HeightDescription}");
        Console.WriteLine($"Tipo:     {code.TypeDescription}");
        return 0;
    }

    /// <summary>Muestra los metadatos de la secuencia, del modelo y del motor de OCR.</summary>
    private static int RunInfo(CommandLine command)
    {
        var source = command.GetString("source");
        var model = command.GetString("model");
        var reported = false;

        if (source is not null)
        {
            using var video = VideoSource.Open(source);
            PrintStreamInfo(source, video.Info);
            reported = true;
        }

        if (model is not null)
        {
            using var detector = new YoloDetector(AppBuilder.BuildDetectorOptions(command));
            Console.WriteLine($"Detector: {detector.Description}");
            Console.WriteLine($"Entrada:  {detector.InputSize.Width}x{detector.InputSize.Height}");
            Console.WriteLine($"Clases:   {string.Join(", ", detector.Labels)}");
            reported = true;
        }

        if (TesseractTextRecognizer.IsAvailable(command.GetString("tesseract", "tesseract")!, out var version))
        {
            Console.WriteLine($"Tesseract: {version}");
        }
        else
        {
            Console.WriteLine("Tesseract: no instalado (usa --ocr onnx --ocr-model para el motor propio)");
        }

        if (command.GetString("ocr-model") is { } ocrModel)
        {
            using var recognizer = AppBuilder.BuildTextRecognizer(command);
            Console.WriteLine($"OCR ONNX: {recognizer.Description} ({ocrModel})");
            reported = true;
        }

        if (!reported)
        {
            Console.WriteLine();
            Console.WriteLine("Indica --source, --model o --ocr-model para inspeccionarlos.");
        }

        return 0;
    }

    private static Video2BicReport RunPipeline(
        CommandLine command,
        IVideoSource video,
        IObjectDetector detector,
        IBicRecognizer recognizer,
        IReadOnlyList<IVideoSink> sinks)
    {
        var pipeline = new Video2BicPipeline(
            detector,
            new ByteTracker(AppBuilder.BuildTrackerOptions(command, video.Info.Fps)),
            recognizer,
            new BicTrackAggregator(AppBuilder.BuildAggregationOptions(command)),
            new BicOverlayRenderer(AppBuilder.BuildOverlayOptions(command)),
            AppBuilder.BuildPipelineOptions(command));

        // Ctrl+C debe cerrar el video de salida correctamente y conservar lo que ya
        // se haya identificado, en vez de perder toda la pasada.
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Interrumpido; cerrando la salida...");
            cancellation.Cancel();
        };

        return pipeline.Run(video, sinks, OnIdentified, cancellation.Token);

        void OnIdentified(ContainerIdentification identification)
        {
            var size = identification.SizeType is null
                ? string.Empty
                : $"  {identification.SizeType.ToDisplayString()}";

            Console.WriteLine(
                $"  >> contenedor #{identification.TrackId}: {identification.Code.ToDisplayString()} " +
                $"({identification.Confidence:P0}, {identification.Observations} lecturas){size}");
        }
    }

    private static void WriteReportFiles(CommandLine command, Video2BicReport report)
    {
        if (command.GetString("json") is { } jsonPath)
        {
            WriteText(jsonPath, report.ToJson());
            Console.WriteLine($"Informe JSON: {jsonPath}");
        }

        if (command.GetString("csv") is { } csvPath)
        {
            WriteText(csvPath, report.ToCsv());
            Console.WriteLine($"Informe CSV:  {csvPath}");
        }
    }

    private static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    private static string DescribeCategory(BicCategory category) => category switch
    {
        BicCategory.FreightContainer => "contenedor de carga",
        BicCategory.DetachableEquipment => "equipo desmontable",
        BicCategory.TrailerOrChassis => "chasis o plataforma",
        _ => "desconocida",
    };

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
        Video2BIC - detecta contenedores maritimos en video y les lee el codigo BIC (ISO 6346)

        USO
          video2bic scan   --source <ruta|url|indice> --model <modelo.onnx> [opciones]
          video2bic check  <codigo>            Valida un codigo BIC o calcula su digito de control
          video2bic decode <codigo>            Interpreta un codigo de tamano y tipo (22G1, 45R1...)
          video2bic info   [--source ...] [--model ...] [--ocr-model ...]

        ENTRADA Y SALIDA
          -s, --source <valor>     Fichero de video, URL rtsp://|http://, o indice de camara (0, 1...).
          -o, --output <ruta>      Video anotado de salida.
              --codec <fourcc>     Codec de salida. Por defecto mp4v; alternativas: avc1, XVID, MJPG.
          -j, --json <ruta>        Informe JSON con los contenedores identificados.
              --csv <ruta>         Informe CSV con los contenedores identificados.
              --window             Abrir ademas una ventana de reproduccion.
              --max-frames <n>     Procesar solo los primeros n fotogramas.
              --quiet              No escribir el progreso.

        DETECCION DE CONTENEDORES
          -m, --model <ruta>       Modelo YOLO exportado a ONNX.
          -l, --labels <ruta>      Fichero de etiquetas, una por linea. Por defecto: COCO.
          -c, --classes <lista>    Clases que debe emitir el detector (nombres o indices).
              --container-classes <lista>  Cuales de esas clases son contenedores. Por defecto: container.
              --conf <0-1>         Confianza minima de deteccion. Por defecto 0.35.
              --iou <0-1>          Umbral de IoU de la NMS. Por defecto 0.45.
              --imgsz <n>          Lado de entrada si el modelo es dinamico. Por defecto 640.
              --format <valor>     auto | yolov8 | yolov5 | e2e. Por defecto auto.
              --provider <valor>   cpu | cuda | directml. Por defecto cpu.
              --device <n>         Indice de GPU. Por defecto 0.
              --threads <n>        Hilos de inferencia. Por defecto, automatico.

        SEGUIMIENTO (ByteTrack)
              --track-thresh <0-1> Frontera entre confianza alta y baja. Por defecto 0.5.
              --low-thresh <0-1>   Confianza minima aprovechable. Por defecto 0.1.
              --match-thresh <0-1> Distancia IoU maxima al asociar. Por defecto 0.8.
              --track-buffer <n>   Fotogramas que un contenedor sobrevive ocluido. Por defecto 60.
              --class-agnostic     Permitir que un track cambie de clase.

        RECONOCIMIENTO DEL CODIGO
              --ocr <motor>        auto | tesseract | onnx. Por defecto auto.
              --ocr-model <ruta>   Modelo CRNN/CTC en ONNX (implica --ocr onnx).
              --ocr-charset <ruta> Alfabeto del modelo. Por defecto, 0-9 y A-Z.
              --ocr-blank <valor>  first | last. Donde coloca el modelo la clase en blanco.
              --ocr-height <n>     Alto de entrada del modelo si es dinamico. Por defecto 32.
              --ocr-conf <0-1>     Confianza minima del OCR. Por defecto 0.3.
              --tesseract <ruta>   Ejecutable de Tesseract. Por defecto 'tesseract'.
              --ocr-lang <codigo>  Idioma de Tesseract. Por defecto eng.
              --psm <n>            Modo de segmentacion de Tesseract. Por defecto 7 (una linea);
                                   11 (texto disperso) le entrega el panel entero.
              --force-regions      Localizar lineas antes de reconocer, aunque el motor sepa hacerlo.
              --max-regions <n>    Regiones de texto por lectura. Por defecto 8.
              --line-height <n>    Alto minimo al que se amplia cada linea. Por defecto 64.
              --binarize           Binarizar el recorte de linea (ayuda a Tesseract).
              --no-deskew          No corregir la inclinacion de la linea.
              --roi-height <n>     Alto al que se amplia el recorte del contenedor. Por defecto 480.
              --roi-scale <n>      Ampliacion maxima del recorte. Por defecto 4.
              --min-box-width <n>  Ancho minimo de contenedor para intentar leerlo. Por defecto 120.
              --min-box-height <n> Alto minimo. Por defecto 80.
              --max-corrections <n>  Caracteres que se permite corregir. Por defecto 2.
              --permissive         Aceptar codigos cuyo digito de control no cuadre (diagnostico).

        PRESUPUESTO Y CONFIRMACION
              --read-every <n>     Fotogramas entre dos lecturas del mismo contenedor. Por defecto 5.
              --max-reads <n>      Contenedores leidos por fotograma. Por defecto 2.
              --max-reads-per-container <n>  Intentos maximos por contenedor. Por defecto sin limite.
              --min-hits <n>       Detecciones consecutivas antes de leer. Por defecto 2.
              --min-readings <n>   Lecturas coincidentes para confirmar. Por defecto 3.
              --min-weight <n>     Evidencia acumulada minima. Por defecto 1.5.
              --lead-ratio <n>     Ventaja del codigo lider sobre el segundo. Por defecto 2.
              --keep-reading       Seguir leyendo un contenedor ya confirmado.

        PRESENTACION
              --thickness <n>      Grosor del borde. Por defecto 2.
              --show-regions       Dibujar las zonas donde se busco texto.
              --no-labels          No dibujar las etiquetas.
              --no-hud             No dibujar el panel superior.
              --no-annotate        No dibujar nada (solo analisis).

        EJEMPLOS
          Analizar un video y volcar el resultado a JSON:
            video2bic scan -s puerto.mp4 -m models/containers.onnx \
                           -l labels/maritime-container.names -c container -j salida/informe.json

          Guardar ademas el video anotado y ver donde busco el texto:
            video2bic scan -s puerto.mp4 -m models/containers.onnx -o salida/anotado.mp4 --show-regions

          Camara en vivo con un modelo de OCR propio en GPU:
            video2bic scan -s rtsp://camara.puerto.local/stream -m models/containers.onnx \
                           --ocr-model models/crnn.onnx --provider cuda

          Comprobar un codigo a mano:
            video2bic check CSQU3054383
            video2bic check CSQU305438        (calcula el digito de control)
            video2bic decode 22G1

        CODIGOS DE SALIDA
          0  correcto (en 'scan', al menos un contenedor identificado)
          1  error de ejecucion, o codigo invalido en 'check'
          2  error de uso
          3  la pasada termino sin identificar ningun contenedor
        """);
}
