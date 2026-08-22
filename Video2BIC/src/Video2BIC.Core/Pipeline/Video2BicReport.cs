using System.Globalization;
using System.Text;
using System.Text.Json;
using Video2BIC.Core.Recognition;

namespace Video2BIC.Core.Pipeline;

/// <summary>
/// Resumen de una pasada completa: que contenedores se identificaron y cuanto
/// costo.
/// </summary>
public sealed class Video2BicReport
{
    /// <summary>Fichero, URL o camara analizados. Lo rellena quien lanza el analisis.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Geometria y tasa de la secuencia leida.</summary>
    public string Stream { get; internal set; } = string.Empty;

    /// <summary>Fotogramas procesados.</summary>
    public int FramesProcessed { get; internal set; }

    /// <summary>Tiempo total de reloj.</summary>
    public TimeSpan Elapsed { get; internal set; }

    /// <summary>Tiempo acumulado en deteccion.</summary>
    public TimeSpan DetectionTime { get; internal set; }

    /// <summary>Tiempo acumulado en seguimiento.</summary>
    public TimeSpan TrackingTime { get; internal set; }

    /// <summary>Tiempo acumulado en reconocimiento de texto.</summary>
    public TimeSpan RecognitionTime { get; internal set; }

    /// <summary>Contenedores distintos que llego a seguir el tracker.</summary>
    public int ContainersTracked { get; internal set; }

    /// <summary>Lecturas intentadas.</summary>
    public int ReadAttempts { get; internal set; }

    /// <summary>Lecturas que propusieron al menos un codigo.</summary>
    public int ReadsWithCandidate { get; internal set; }

    /// <summary>Identificaciones, confirmadas primero.</summary>
    public IReadOnlyList<ContainerIdentification> Identifications { get; internal set; }
        = Array.Empty<ContainerIdentification>();

    /// <summary>Identificaciones confirmadas.</summary>
    public IEnumerable<ContainerIdentification> Confirmed
        => Identifications.Where(identification => identification.IsConfirmed);

    /// <summary>Fotogramas por segundo de extremo a extremo.</summary>
    public double AverageFps => Elapsed.TotalSeconds > 0d ? FramesProcessed / Elapsed.TotalSeconds : 0d;

    /// <summary>Milisegundos medios por lectura de codigo.</summary>
    public double AverageReadMs
        => ReadAttempts > 0 ? RecognitionTime.TotalMilliseconds / ReadAttempts : 0d;

    /// <summary>
    /// Proporcion de contenedores seguidos que acabaron con un codigo confirmado.
    /// </summary>
    public double IdentificationRate
        => ContainersTracked > 0 ? (double)Confirmed.Count() / ContainersTracked : 0d;

    /// <summary>Resumen legible para consola.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();

        builder.AppendLine(CultureInfo.InvariantCulture,
            $"{FramesProcessed} fotogramas en {Elapsed.TotalSeconds:F1}s ({AverageFps:F1} FPS)");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"deteccion {DetectionTime.TotalSeconds:F1}s, seguimiento {TrackingTime.TotalSeconds:F2}s, " +
            $"OCR {RecognitionTime.TotalSeconds:F1}s en {ReadAttempts} lecturas ({AverageReadMs:F0} ms/lectura)");
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"contenedores seguidos: {ContainersTracked}, identificados: {Confirmed.Count()} " +
            $"({IdentificationRate:P0})");

        foreach (var identification in Identifications)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"  {identification.ToDisplayString()}  " +
                $"[{identification.FirstSeen:hh\\:mm\\:ss\\.ff} - {identification.LastSeen:hh\\:mm\\:ss\\.ff}]");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Serializa el informe a JSON.
    /// </summary>
    /// <remarks>
    /// Se escribe a mano con <see cref="Utf8JsonWriter"/> en vez de por reflexion
    /// para que el formato sea explicito y estable: este JSON es la interfaz con el
    /// sistema de gestion de la terminal, y no debe cambiar porque se renombre una
    /// propiedad.
    /// </remarks>
    public string ToJson(bool indented = true)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartObject();
            writer.WriteString("source", Source);
            writer.WriteString("stream", Stream);
            writer.WriteNumber("framesProcessed", FramesProcessed);
            writer.WriteNumber("elapsedSeconds", Math.Round(Elapsed.TotalSeconds, 3));
            writer.WriteNumber("averageFps", Math.Round(AverageFps, 2));
            writer.WriteNumber("containersTracked", ContainersTracked);
            writer.WriteNumber("readAttempts", ReadAttempts);
            writer.WriteNumber("readsWithCandidate", ReadsWithCandidate);

            writer.WriteStartArray("containers");
            foreach (var identification in Identifications)
            {
                WriteIdentification(writer, identification);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteIdentification(Utf8JsonWriter writer, ContainerIdentification identification)
    {
        writer.WriteStartObject();
        writer.WriteNumber("trackId", identification.TrackId);
        writer.WriteString("bicCode", identification.Code.Value);
        writer.WriteString("ownerCode", identification.Code.OwnerCode);
        writer.WriteString("categoryIdentifier", identification.Code.CategoryIdentifier.ToString());
        writer.WriteString("serialNumber", identification.Code.SerialNumber);
        writer.WriteNumber("checkDigit", identification.Code.CheckDigit);
        writer.WriteBoolean("checkDigitValid", identification.Code.HasValidCheckDigit);
        writer.WriteBoolean("confirmed", identification.IsConfirmed);
        writer.WriteNumber("confidence", Math.Round(identification.Confidence, 4));
        writer.WriteNumber("observations", identification.Observations);
        writer.WriteNumber("competingCodes", identification.CompetingCodes);
        writer.WriteNumber("firstFrame", identification.FirstFrame);
        writer.WriteNumber("lastFrame", identification.LastFrame);
        writer.WriteString("firstSeen", identification.FirstSeen.ToString("c", CultureInfo.InvariantCulture));
        writer.WriteString("lastSeen", identification.LastSeen.ToString("c", CultureInfo.InvariantCulture));

        if (identification.SizeType is { } sizeType)
        {
            writer.WriteStartObject("sizeType");
            writer.WriteString("code", sizeType.Value);
            writer.WriteString("length", sizeType.LengthDescription);
            writer.WriteString("height", sizeType.HeightDescription);
            writer.WriteString("type", sizeType.TypeDescription);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("sizeType");
        }

        writer.WriteEndObject();
    }

    /// <summary>Serializa las identificaciones a CSV, con cabecera.</summary>
    public string ToCsv()
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "track_id,bic_code,owner_code,category,serial_number,check_digit,check_digit_valid," +
            "confirmed,confidence,observations,competing_codes,first_frame,last_frame,first_seen,last_seen,size_type");

        foreach (var identification in Identifications)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                identification.TrackId.ToString(CultureInfo.InvariantCulture),
                identification.Code.Value,
                identification.Code.OwnerCode,
                identification.Code.CategoryIdentifier.ToString(),
                identification.Code.SerialNumber,
                identification.Code.CheckDigit.ToString(CultureInfo.InvariantCulture),
                identification.Code.HasValidCheckDigit ? "true" : "false",
                identification.IsConfirmed ? "true" : "false",
                identification.Confidence.ToString("F4", CultureInfo.InvariantCulture),
                identification.Observations.ToString(CultureInfo.InvariantCulture),
                identification.CompetingCodes.ToString(CultureInfo.InvariantCulture),
                identification.FirstFrame.ToString(CultureInfo.InvariantCulture),
                identification.LastFrame.ToString(CultureInfo.InvariantCulture),
                identification.FirstSeen.ToString("c", CultureInfo.InvariantCulture),
                identification.LastSeen.ToString("c", CultureInfo.InvariantCulture),
                identification.SizeType?.Value ?? string.Empty,
            }));
        }

        return builder.ToString();
    }
}
