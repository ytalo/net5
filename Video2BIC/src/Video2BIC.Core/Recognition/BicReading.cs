using MaritimeVision.Core.Models;
using OpenCvSharp;
using Video2BIC.Core.Bic;
using Video2BIC.Core.Text;

namespace Video2BIC.Core.Recognition;

/// <summary>
/// Lo que se saco de intentar leer el codigo de un contenedor en un fotograma.
/// </summary>
/// <param name="Candidates">Codigos propuestos, de mas a menos plausible.</param>
/// <param name="Fragments">Texto reconocido, en orden de lectura.</param>
/// <param name="Regions">Zonas del recorte ampliado donde se busco texto.</param>
/// <param name="SizeType">Codigo de tamano y tipo, si aparecio.</param>
/// <param name="Roi">Recorte del contenedor, en coordenadas del fotograma.</param>
/// <param name="RoiScale">Ampliacion que se aplico al recorte antes de buscar.</param>
/// <param name="Elapsed">Tiempo empleado en toda la lectura.</param>
public sealed record BicReadingResult(
    IReadOnlyList<BicCandidate> Candidates,
    IReadOnlyList<TextFragment> Fragments,
    IReadOnlyList<Rect> Regions,
    SizeTypeCode? SizeType,
    Rect Roi,
    double RoiScale,
    TimeSpan Elapsed)
{
    /// <summary>Resultado sin nada que aportar.</summary>
    public static BicReadingResult Empty { get; } = new(
        Array.Empty<BicCandidate>(),
        Array.Empty<TextFragment>(),
        Array.Empty<Rect>(),
        null,
        default,
        1d,
        TimeSpan.Zero);

    /// <summary>Candidato mas plausible, o <c>null</c> si no hubo ninguno.</summary>
    public BicCandidate? Best => Candidates.Count > 0 ? Candidates[0] : null;

    /// <summary>Se propuso al menos un codigo.</summary>
    public bool HasCandidate => Candidates.Count > 0;

    /// <summary>
    /// Devuelve una region del recorte a coordenadas del fotograma original,
    /// deshaciendo la ampliacion y el desplazamiento.
    /// </summary>
    public Rect ToFrame(Rect region)
    {
        var factor = RoiScale > 0d ? 1d / RoiScale : 1d;
        return new Rect(
            Roi.X + (int)Math.Round(region.X * factor),
            Roi.Y + (int)Math.Round(region.Y * factor),
            Math.Max(1, (int)Math.Round(region.Width * factor)),
            Math.Max(1, (int)Math.Round(region.Height * factor)));
    }
}

/// <summary>
/// Una lectura concreta atribuida a un contenedor seguido: la unidad de evidencia
/// que acumula <see cref="BicTrackAggregator"/>.
/// </summary>
/// <param name="TrackId">Identidad del contenedor en el seguimiento.</param>
/// <param name="FrameIndex">Fotograma en el que se leyo.</param>
/// <param name="Timestamp">Posicion temporal dentro de la secuencia.</param>
/// <param name="Candidate">Codigo propuesto y su confianza.</param>
/// <param name="SizeType">Codigo de tamano y tipo leido junto al BIC, si lo hubo.</param>
public readonly record struct BicReading(
    int TrackId,
    int FrameIndex,
    TimeSpan Timestamp,
    BicCandidate Candidate,
    SizeTypeCode? SizeType)
{
    /// <summary>Codigo propuesto.</summary>
    public BicCode Code => Candidate.Code;

    /// <inheritdoc />
    public override string ToString()
        => $"#{TrackId} f{FrameIndex} {Candidate}";
}

/// <summary>Lee el codigo BIC de un contenedor ya localizado en el fotograma.</summary>
public interface IBicRecognizer : IDisposable
{
    /// <summary>Descripcion legible de la cadena de reconocimiento.</summary>
    string Description { get; }

    /// <summary>
    /// Intenta leer el codigo dentro de la caja indicada.
    /// </summary>
    /// <param name="frame">Fotograma completo, BGR.</param>
    /// <param name="box">Caja del contenedor, en coordenadas del fotograma.</param>
    BicReadingResult Read(Mat frame, BoundingBox box);
}
