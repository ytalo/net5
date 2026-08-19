namespace MaritimeVision.Core.Pipeline;

/// <summary>Resumen de una ejecucion del pipeline.</summary>
public sealed class PipelineStats
{
    private readonly Dictionary<string, HashSet<int>> _trackIdsByLabel = [];

    /// <summary>Fotogramas procesados.</summary>
    public int FramesProcessed { get; internal set; }

    /// <summary>Tiempo total de reloj.</summary>
    public TimeSpan Elapsed { get; internal set; }

    /// <summary>Tiempo acumulado en inferencia.</summary>
    public TimeSpan TotalInferenceTime { get; internal set; }

    /// <summary>Tiempo acumulado en seguimiento.</summary>
    public TimeSpan TotalTrackingTime { get; internal set; }

    /// <summary>Detecciones acumuladas antes del seguimiento.</summary>
    public long TotalDetections { get; internal set; }

    /// <summary>Fotogramas por segundo alcanzados de extremo a extremo.</summary>
    public double AverageFps => Elapsed.TotalSeconds > 0d ? FramesProcessed / Elapsed.TotalSeconds : 0d;

    /// <summary>Milisegundos medios por inferencia.</summary>
    public double AverageInferenceMs
        => FramesProcessed > 0 ? TotalInferenceTime.TotalMilliseconds / FramesProcessed : 0d;

    /// <summary>Milisegundos medios por actualizacion del tracker.</summary>
    public double AverageTrackingMs
        => FramesProcessed > 0 ? TotalTrackingTime.TotalMilliseconds / FramesProcessed : 0d;

    /// <summary>Identidades unicas observadas por clase: el recuento real de objetos distintos.</summary>
    public IReadOnlyDictionary<string, int> UniqueObjectsByLabel
        => _trackIdsByLabel.ToDictionary(entry => entry.Key, entry => entry.Value.Count);

    /// <summary>Total de identidades unicas de todas las clases.</summary>
    public int UniqueObjects => _trackIdsByLabel.Values.SelectMany(ids => ids).Distinct().Count();

    internal void RegisterTrack(string label, int trackId)
    {
        if (!_trackIdsByLabel.TryGetValue(label, out var ids))
        {
            ids = [];
            _trackIdsByLabel[label] = ids;
        }

        ids.Add(trackId);
    }

    /// <summary>Resumen legible para consola.</summary>
    public override string ToString()
    {
        var byLabel = UniqueObjectsByLabel
            .OrderByDescending(entry => entry.Value)
            .Select(entry => $"{entry.Key}={entry.Value}");

        return $"{FramesProcessed} fotogramas en {Elapsed.TotalSeconds:F1}s " +
               $"({AverageFps:F1} FPS, inferencia {AverageInferenceMs:F1} ms, seguimiento {AverageTrackingMs:F2} ms); " +
               $"objetos unicos: {UniqueObjects} [{string.Join(", ", byLabel)}]";
    }
}
