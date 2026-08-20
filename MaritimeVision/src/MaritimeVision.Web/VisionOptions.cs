namespace MaritimeVision.Web;

/// <summary>
/// Configuracion del visor, enlazada a la seccion <c>MaritimeVision</c> de
/// <c>appsettings.json</c> o a variables de entorno.
/// </summary>
public sealed class VisionOptions
{
    /// <summary>Nombre de la seccion de configuracion.</summary>
    public const string SectionName = "MaritimeVision";

    /// <summary>Ruta del modelo YOLO en ONNX.</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>Ruta del fichero de etiquetas; vacio usa COCO.</summary>
    public string? LabelsPath { get; set; }

    /// <summary>Clases de interes separadas por comas; vacio no filtra.</summary>
    public string? TargetClasses { get; set; }

    /// <summary>Confianza minima de deteccion.</summary>
    public float ConfidenceThreshold { get; set; } = 0.25f;

    /// <summary>Umbral de IoU de la NMS.</summary>
    public float IouThreshold { get; set; } = 0.45f;

    /// <summary>Frontera entre confianza alta y baja del tracker.</summary>
    public float TrackThreshold { get; set; } = 0.5f;

    /// <summary>Fotogramas que un track sobrevive sin detecciones.</summary>
    public int TrackBuffer { get; set; } = 30;

    /// <summary>Calidad JPEG del flujo MJPEG, de 1 a 100.</summary>
    public int JpegQuality { get; set; } = 80;

    /// <summary>Tamano maximo admitido en la subida de video, en megabytes.</summary>
    public int MaxUploadMegabytes { get; set; } = 512;

    /// <summary>Directorio donde se guardan los videos subidos.</summary>
    public string UploadDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "maritimevision-uploads");

    /// <summary>
    /// Permitir origenes de red (RTSP/HTTP) indicados por el usuario. Desactivado
    /// por defecto: en un despliegue accesible desde fuera, dejar que un visitante
    /// elija la URL convierte al servidor en un cliente de red arbitrario.
    /// </summary>
    public bool AllowNetworkSources { get; set; }
}
