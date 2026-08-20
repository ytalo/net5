using MaritimeVision.Core.Models;
using OpenCvSharp;

namespace MaritimeVision.Core.Abstractions;

/// <summary>
/// Detector de objetos por fotograma. Abstraerlo permite sustituir YOLO por otro
/// modelo (o por un doble de prueba) sin tocar el pipeline ni el tracker.
/// </summary>
public interface IObjectDetector : IDisposable
{
    /// <summary>Etiquetas que el detector puede emitir, indexadas por <c>ClassId</c>.</summary>
    IReadOnlyList<string> Labels { get; }

    /// <summary>Descripcion legible del modelo cargado, para logs y diagnostico.</summary>
    string Description { get; }

    /// <summary>
    /// Detecta objetos en un fotograma BGR. Las cajas devueltas ya estan en
    /// coordenadas de pixel del fotograma de entrada.
    /// </summary>
    IReadOnlyList<Detection> Detect(Mat frame);
}
