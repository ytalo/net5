using MaritimeVision.Core.Configuration;
using MaritimeVision.Core.Detectors;
using MaritimeVision.Core.Pipeline;
using MaritimeVision.Core.Rendering;
using MaritimeVision.Core.Tracking;
using Microsoft.Extensions.Options;

namespace MaritimeVision.Web;

/// <summary>
/// Crea un pipeline independiente por reproduccion.
/// </summary>
/// <remarks>
/// Ni el detector ni el tracker son seguros para uso concurrente (el primero
/// reutiliza un buffer de entrada, el segundo mantiene el estado de los tracks),
/// asi que cada espectador recibe su propia instancia en lugar de compartirlas.
/// </remarks>
public sealed class VisionPipelineFactory(IOptions<VisionOptions> options, ContentPathResolver paths)
{
    private readonly VisionOptions _options = options.Value;

    /// <summary>Ruta absoluta del modelo, o <c>null</c> si no se encuentra.</summary>
    public string? ResolvedModelPath => paths.Resolve(_options.ModelPath);

    /// <summary>Comprueba que el modelo configurado existe.</summary>
    public bool IsModelAvailable => ResolvedModelPath is not null;

    /// <summary>Mensaje de diagnostico sobre el modelo.</summary>
    public string ModelStatus => ResolvedModelPath is { } resolved
        ? $"Modelo cargado desde '{resolved}'."
        : $"No se encuentra el modelo '{_options.ModelPath}'. Se busco en: " +
          $"{string.Join(", ", paths.CandidateRoots())}. Corrige MaritimeVision:ModelPath " +
          "en appsettings.json o la variable de entorno MaritimeVision__ModelPath.";

    /// <summary>Crea un detector nuevo.</summary>
    /// <exception cref="InvalidOperationException">Si el modelo no esta disponible.</exception>
    public YoloDetector CreateDetector()
    {
        if (!IsModelAvailable)
        {
            throw new InvalidOperationException(ModelStatus);
        }

        LabelSet? labels = null;
        if (!string.IsNullOrWhiteSpace(_options.LabelsPath))
        {
            var labelsPath = paths.Resolve(_options.LabelsPath)
                ?? throw new InvalidOperationException(
                    $"No se encuentra el fichero de etiquetas '{_options.LabelsPath}'. " +
                    "Corrige MaritimeVision:LabelsPath, o dejalo vacio para que las clases " +
                    "se deduzcan del propio modelo.");

            labels = LabelSet.FromFile(labelsPath);
        }

        var detectorOptions = new YoloDetectorOptions
        {
            ModelPath = ResolvedModelPath!,
            Labels = labels,
            ConfidenceThreshold = _options.ConfidenceThreshold,
            IouThreshold = _options.IouThreshold,
        };

        if (!string.IsNullOrWhiteSpace(_options.TargetClasses))
        {
            foreach (var target in _options.TargetClasses.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                detectorOptions.TargetClasses.Add(target.Trim());
            }
        }

        return new YoloDetector(detectorOptions);
    }

    /// <summary>Crea el pipeline completo para una secuencia de <paramref name="frameRate"/> FPS.</summary>
    public VideoAnalyticsPipeline CreatePipeline(YoloDetector detector, double frameRate)
    {
        var tracker = new ByteTracker(new ByteTrackOptions
        {
            TrackThreshold = _options.TrackThreshold,
            TrackBuffer = _options.TrackBuffer,
            FrameRate = frameRate,
        });

        return new VideoAnalyticsPipeline(detector, tracker, new BoxRenderer());
    }
}
