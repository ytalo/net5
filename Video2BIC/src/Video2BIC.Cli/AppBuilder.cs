using MaritimeVision.Core.Configuration;
using MaritimeVision.Core.Detectors;
using MaritimeVision.Core.Tracking;
using Video2BIC.Core.Bic;
using Video2BIC.Core.Imaging;
using Video2BIC.Core.Ocr;
using Video2BIC.Core.Pipeline;
using Video2BIC.Core.Recognition;
using Video2BIC.Core.Rendering;
using Video2BIC.Core.TextRegions;

namespace Video2BIC.Cli;

/// <summary>Motor de OCR elegido en la linea de comandos.</summary>
public enum OcrEngine
{
    /// <summary>ONNX si se indico un modelo; si no, Tesseract cuando este instalado.</summary>
    Auto = 0,

    /// <summary>Modelo CRNN/CTC ejecutado con ONNX Runtime.</summary>
    Onnx = 1,

    /// <summary>Binario de Tesseract instalado en el sistema.</summary>
    Tesseract = 2,
}

/// <summary>
/// Traduce las opciones de la linea de comandos a la configuracion de los
/// componentes.
/// </summary>
public static class AppBuilder
{
    private static readonly Dictionary<string, YoloOutputFormat> FormatAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["auto"] = YoloOutputFormat.Auto,
            ["yolov8"] = YoloOutputFormat.Yolov8,
            ["v8"] = YoloOutputFormat.Yolov8,
            ["yolov11"] = YoloOutputFormat.Yolov8,
            ["yolov5"] = YoloOutputFormat.Yolov5,
            ["v5"] = YoloOutputFormat.Yolov5,
            ["yolov7"] = YoloOutputFormat.Yolov5,
            ["e2e"] = YoloOutputFormat.EndToEnd,
            ["nms"] = YoloOutputFormat.EndToEnd,
        };

    private static readonly Dictionary<string, ExecutionProvider> ProviderAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cpu"] = ExecutionProvider.Cpu,
            ["cuda"] = ExecutionProvider.Cuda,
            ["gpu"] = ExecutionProvider.Cuda,
            ["directml"] = ExecutionProvider.DirectML,
            ["dml"] = ExecutionProvider.DirectML,
        };

    private static readonly Dictionary<string, OcrEngine> EngineAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["auto"] = OcrEngine.Auto,
            ["onnx"] = OcrEngine.Onnx,
            ["crnn"] = OcrEngine.Onnx,
            ["tesseract"] = OcrEngine.Tesseract,
            ["tess"] = OcrEngine.Tesseract,
        };

    private static readonly Dictionary<string, BlankPosition> BlankAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["first"] = BlankPosition.First,
            ["primero"] = BlankPosition.First,
            ["last"] = BlankPosition.Last,
            ["ultimo"] = BlankPosition.Last,
        };

    /// <summary>Construye la configuracion del detector de contenedores.</summary>
    public static YoloDetectorOptions BuildDetectorOptions(CommandLine command)
    {
        var labelsPath = command.GetString("labels");

        var options = new YoloDetectorOptions
        {
            ModelPath = command.GetRequiredString("model"),
            Labels = labelsPath is not null ? LabelSet.FromFile(labelsPath) : null,
            InputSize = command.GetInt("imgsz", 640),
            ConfidenceThreshold = command.GetFloat("conf", 0.35f),
            IouThreshold = command.GetFloat("iou", 0.45f),
            MaxDetections = command.GetInt("max-det", 100),
            OutputFormat = command.GetEnum("format", YoloOutputFormat.Auto, FormatAliases),
            Provider = command.GetEnum("provider", ExecutionProvider.Cpu, ProviderAliases),
            DeviceId = command.GetInt("device", 0),
            IntraOpThreads = command.GetInt("threads", 0),
        };

        foreach (var target in command.GetList("classes"))
        {
            options.TargetClasses.Add(target);
        }

        return options;
    }

    /// <summary>Construye la configuracion del seguimiento.</summary>
    public static ByteTrackOptions BuildTrackerOptions(CommandLine command, double frameRate) => new()
    {
        TrackThreshold = command.GetFloat("track-thresh", 0.5f),
        LowThreshold = command.GetFloat("low-thresh", 0.1f),
        MatchThreshold = command.GetFloat("match-thresh", 0.8f),

        // Un contenedor se ocluye a menudo detras de una grua o de otro contenedor,
        // y perder la identidad significa volver a leer el codigo desde cero y
        // contarlo dos veces. Un buffer mas largo que el de MaritimeVision compensa.
        TrackBuffer = command.GetInt("track-buffer", 60),
        FrameRate = frameRate,
        ClassAwareMatching = !command.HasFlag("class-agnostic"),
    };

    /// <summary>Construye el motor de OCR indicado.</summary>
    /// <exception cref="CommandLineException">
    /// Si no hay ningun motor utilizable, con la explicacion de como conseguir uno.
    /// </exception>
    public static ITextRecognizer BuildTextRecognizer(CommandLine command)
    {
        var engine = command.GetEnum("ocr", OcrEngine.Auto, EngineAliases);
        var modelPath = command.GetString("ocr-model");
        var executable = command.GetString("tesseract", "tesseract")!;

        if (engine == OcrEngine.Auto)
        {
            engine = modelPath is not null ? OcrEngine.Onnx : OcrEngine.Tesseract;
        }

        if (engine == OcrEngine.Onnx)
        {
            if (modelPath is null)
            {
                throw new CommandLineException("--ocr onnx necesita ademas --ocr-model <modelo.onnx>.");
            }

            var charsetPath = command.GetString("ocr-charset");
            var blank = command.GetEnum("ocr-blank", BlankPosition.First, BlankAliases);

            return new OnnxTextRecognizer(new OnnxTextRecognizerOptions
            {
                ModelPath = modelPath,
                Charset = charsetPath is not null
                    ? CtcCharset.FromFile(charsetPath, blank)
                    : new CtcCharset(CtcCharset.Alphanumeric.Characters, blank),
                InputHeight = command.GetInt("ocr-height", 32),
                MaxInputWidth = command.GetInt("ocr-max-width", 320),
                MinConfidence = command.GetFloat("ocr-conf", 0.3f),
                Provider = command.GetEnum("provider", ExecutionProvider.Cpu, ProviderAliases),
                DeviceId = command.GetInt("device", 0),
                IntraOpThreads = command.GetInt("threads", 0),
            });
        }

        if (!TesseractTextRecognizer.IsAvailable(executable, out _))
        {
            throw new CommandLineException(
                $"No se encontro el ejecutable de Tesseract ('{executable}'). Opciones: " +
                "instalarlo (apt install tesseract-ocr, brew install tesseract, winget install " +
                "UB-Mannheim.TesseractOCR), indicar su ruta con --tesseract, o usar un modelo " +
                "propio con --ocr onnx --ocr-model <modelo.onnx>.");
        }

        return new TesseractTextRecognizer(new TesseractOptions
        {
            Executable = executable,
            Language = command.GetString("ocr-lang", "eng")!,
            PageSegmentationMode = command.GetInt("psm", 7),
            MinConfidence = command.GetFloat("ocr-conf", 0.35f),
        });
    }

    /// <summary>Construye la cadena de reconocimiento de codigos.</summary>
    public static ContainerBicRecognizer BuildBicRecognizer(CommandLine command, ITextRecognizer recognizer)
    {
        var recognizerOptions = new BicRecognizerOptions
        {
            BoxMargin = command.GetFloat("roi-margin", 0.03f),
            MinBoxWidth = command.GetInt("min-box-width", 120),
            MinBoxHeight = command.GetInt("min-box-height", 80),
            RoiHeight = command.GetInt("roi-height", 480),
            MaxRoiScale = command.GetFloat("roi-scale", 4f),
            MaxRegionsPerRead = command.GetInt("max-regions", 8),
            AlwaysUseRegionDetector = command.HasFlag("force-regions"),
            Preprocess = new TextPreprocessOptions
            {
                MinHeight = command.GetInt("line-height", 64),
                Binarize = command.HasFlag("binarize"),
                Deskew = !command.HasFlag("no-deskew"),
            },
        };

        var parser = new BicCodeParser(new BicParserOptions
        {
            MaxCorrections = command.GetInt("max-corrections", 2),
            RequireValidCheckDigit = !command.HasFlag("permissive"),
        });

        var regionOptions = new TextRegionOptions
        {
            MaxRegions = command.GetInt("max-regions", 8),
        };

        return new ContainerBicRecognizer(
            recognizer,
            new MserTextRegionDetector(regionOptions),
            parser,
            recognizerOptions);
    }

    /// <summary>Construye la configuracion de la votacion por contenedor.</summary>
    public static BicAggregationOptions BuildAggregationOptions(CommandLine command) => new()
    {
        MinObservations = command.GetInt("min-readings", 3),
        MinAccumulatedWeight = command.GetFloat("min-weight", 1.5f),
        MinLeadRatio = command.GetFloat("lead-ratio", 2f),
    };

    /// <summary>Construye la configuracion del pipeline.</summary>
    public static Video2BicOptions BuildPipelineOptions(CommandLine command)
    {
        var options = new Video2BicOptions
        {
            ReadIntervalFrames = command.GetInt("read-every", 5),
            MinHitStreak = command.GetInt("min-hits", 2),
            MaxReadsPerFrame = command.GetInt("max-reads", 2),
            MaxReadsPerTrack = command.GetInt("max-reads-per-container", 0),
            StopWhenConfirmed = !command.HasFlag("keep-reading"),
            Annotate = !command.HasFlag("no-annotate"),
            MaxFrames = command.GetInt("max-frames", 0),
            ReportProgress = !command.HasFlag("quiet"),
            ProgressInterval = command.GetInt("progress-every", 30),
        };

        options.ContainerLabels.Clear();

        var labels = command.GetList("container-classes");
        if (labels.Count == 0)
        {
            labels = ["container"];
        }

        foreach (var label in labels)
        {
            options.ContainerLabels.Add(label);
        }

        return options;
    }

    /// <summary>Construye la configuracion de dibujo.</summary>
    public static BicOverlayOptions BuildOverlayOptions(CommandLine command) => new()
    {
        BoxThickness = command.GetInt("thickness", 2),
        ShowLabels = !command.HasFlag("no-labels"),
        ShowTextRegions = command.HasFlag("show-regions"),
        ShowHud = !command.HasFlag("no-hud"),
    };
}
