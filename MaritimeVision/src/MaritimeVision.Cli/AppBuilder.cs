using MaritimeVision.Core.Configuration;
using MaritimeVision.Core.Detectors;
using MaritimeVision.Core.Rendering;
using MaritimeVision.Core.Tracking;

namespace MaritimeVision.Cli;

/// <summary>
/// Traduce las opciones de la linea de comandos a la configuracion de los
/// componentes del Core.
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

    private static readonly Dictionary<string, ColorMode> ColorAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["track"] = ColorMode.ByTrackId,
            ["id"] = ColorMode.ByTrackId,
            ["class"] = ColorMode.ByClass,
        };

    /// <summary>Construye la configuracion del detector.</summary>
    public static YoloDetectorOptions BuildDetectorOptions(CommandLine command)
    {
        var labelsPath = command.GetString("labels");

        var options = new YoloDetectorOptions
        {
            ModelPath = command.GetRequiredString("model"),
            Labels = labelsPath is not null ? LabelSet.FromFile(labelsPath) : null,
            InputSize = command.GetInt("imgsz", 640),
            ConfidenceThreshold = command.GetFloat("conf", 0.25f),
            IouThreshold = command.GetFloat("iou", 0.45f),
            MaxDetections = command.GetInt("max-det", 300),
            ClassAgnosticNms = command.HasFlag("agnostic-nms"),
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

    /// <summary>Construye la configuracion del tracker.</summary>
    public static ByteTrackOptions BuildTrackerOptions(CommandLine command, double frameRate)
    {
        var newTrackThreshold = command.GetFloat("new-track-thresh", -1f);

        return new ByteTrackOptions
        {
            TrackThreshold = command.GetFloat("track-thresh", 0.5f),
            LowThreshold = command.GetFloat("low-thresh", 0.1f),
            NewTrackThreshold = newTrackThreshold >= 0f ? newTrackThreshold : null,
            MatchThreshold = command.GetFloat("match-thresh", 0.8f),
            TrackBuffer = command.GetInt("track-buffer", 30),
            FrameRate = frameRate,
            ClassAwareMatching = !command.HasFlag("class-agnostic"),
            MaxPredictedFrames = command.GetInt("predict-frames", 0),
        };
    }

    /// <summary>Construye la configuracion de dibujo.</summary>
    public static RenderOptions BuildRenderOptions(CommandLine command) => new()
    {
        ColorMode = command.GetEnum("color-by", ColorMode.ByTrackId, ColorAliases),
        ShowLabels = !command.HasFlag("no-labels"),
        ShowTrails = !command.HasFlag("no-trails"),
        ShowHud = !command.HasFlag("no-hud"),
        ShowRawDetections = command.HasFlag("show-detections"),
        BoxThickness = command.GetInt("thickness", 2),
        TrailLength = command.GetInt("trail-length", 30),
    };
}
