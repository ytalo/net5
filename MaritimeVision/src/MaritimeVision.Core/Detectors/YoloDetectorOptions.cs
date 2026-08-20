using MaritimeVision.Core.Configuration;

namespace MaritimeVision.Core.Detectors;

/// <summary>Proveedor de ejecucion que ONNX Runtime debe intentar usar.</summary>
public enum ExecutionProvider
{
    /// <summary>CPU. Siempre disponible.</summary>
    Cpu = 0,

    /// <summary>NVIDIA CUDA. Requiere el paquete <c>Microsoft.ML.OnnxRuntime.Gpu</c>.</summary>
    Cuda = 1,

    /// <summary>DirectML (Windows). Requiere el paquete <c>Microsoft.ML.OnnxRuntime.DirectML</c>.</summary>
    DirectML = 2,
}

/// <summary>Configuracion de <see cref="YoloDetector"/>.</summary>
public sealed class YoloDetectorOptions
{
    /// <summary>Ruta del modelo ONNX. Obligatoria.</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Etiquetas del modelo. Si es <c>null</c> se generan nombres sinteticos a
    /// partir del numero de clases que declare el tensor de salida.
    /// </summary>
    public LabelSet? Labels { get; set; }

    /// <summary>
    /// Lado de la entrada cuadrada del modelo. Solo se usa si el modelo declara
    /// dimensiones dinamicas; en caso contrario se toma la del propio modelo.
    /// </summary>
    public int InputSize { get; set; } = 640;

    /// <summary>Confianza minima para conservar una deteccion.</summary>
    public float ConfidenceThreshold { get; set; } = 0.25f;

    /// <summary>Solape a partir del cual la NMS considera dos cajas la misma deteccion.</summary>
    public float IouThreshold { get; set; } = 0.45f;

    /// <summary>Numero maximo de detecciones por fotograma.</summary>
    public int MaxDetections { get; set; } = 300;

    /// <summary>Aplicar la NMS ignorando la clase.</summary>
    public bool ClassAgnosticNms { get; set; }

    /// <summary>
    /// Clases de interes por nombre o indice. Vacio significa "todas". Es el filtro
    /// que restringe la salida al contenedor maritimo cuando el modelo detecta
    /// tambien camiones, gruas o personas.
    /// </summary>
    public IList<string> TargetClasses { get; } = new List<string>();

    /// <summary>Formato del tensor de salida; por defecto se deduce automaticamente.</summary>
    public YoloOutputFormat OutputFormat { get; set; } = YoloOutputFormat.Auto;

    /// <summary>Proveedor de ejecucion preferido.</summary>
    public ExecutionProvider Provider { get; set; } = ExecutionProvider.Cpu;

    /// <summary>Identificador del dispositivo para los proveedores de GPU.</summary>
    public int DeviceId { get; set; }

    /// <summary>Hilos intra-operacion; 0 deja decidir a ONNX Runtime.</summary>
    public int IntraOpThreads { get; set; }

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun parametro es invalido.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
        {
            throw new InvalidOperationException("Hay que indicar la ruta del modelo ONNX.");
        }

        if (!File.Exists(ModelPath))
        {
            throw new InvalidOperationException($"No se encontro el modelo '{ModelPath}'.");
        }

        if (ConfidenceThreshold is < 0f or > 1f)
        {
            throw new InvalidOperationException("ConfidenceThreshold debe estar en [0, 1].");
        }

        if (IouThreshold is < 0f or > 1f)
        {
            throw new InvalidOperationException("IouThreshold debe estar en [0, 1].");
        }

        if (InputSize <= 0 || InputSize % 32 != 0)
        {
            throw new InvalidOperationException("InputSize debe ser un multiplo positivo de 32.");
        }

        if (MaxDetections <= 0)
        {
            throw new InvalidOperationException("MaxDetections debe ser positivo.");
        }
    }
}
