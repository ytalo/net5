using System.Text;
using System.Text.Json;
using Photo2BIC.Core.Engines;
using Photo2BIC.Core.Fusion;
using Video2BIC.Core.Bic;

namespace Photo2BIC.Core.Pipeline;

/// <summary>
/// Todo lo que se averiguo de una fotografia.
/// </summary>
/// <param name="Source">Ruta o etiqueta de la fotografia.</param>
/// <param name="Width">Ancho analizado, en pixeles.</param>
/// <param name="Height">Alto analizado, en pixeles.</param>
/// <param name="Verdict">Codigo decidido y sus alternativas.</param>
/// <param name="SizeType">Codigo de tamano y tipo, si aparecio junto al BIC.</param>
/// <param name="Results">Lo que devolvio cada motor sobre cada variante.</param>
/// <param name="Evidence">Codigos que propuso cada motor, antes de combinarlos.</param>
/// <param name="Duration">Tiempo total del analisis.</param>
public sealed record PhotoReadResult(
    string Source,
    int Width,
    int Height,
    BicVerdict Verdict,
    SizeTypeCode? SizeType,
    IReadOnlyList<OcrEngineResult> Results,
    IReadOnlyList<BicEvidence> Evidence,
    TimeSpan Duration)
{
    /// <summary>Se leyo un codigo, confirmado o no.</summary>
    public bool HasCode => Verdict.HasCode;

    /// <summary>El codigo se dio por confirmado.</summary>
    public bool IsConfirmed => Verdict.Confirmed;

    /// <summary>Motores que respondieron correctamente.</summary>
    public int EnginesResponded => Results.Count(result => result.Status == OcrEngineStatus.Ok);

    /// <summary>Motores que no estan configurados en esta maquina.</summary>
    public IReadOnlyList<OcrEngineResult> Unavailable
        => Results.Where(result => result.Status == OcrEngineStatus.Unavailable)
            .DistinctBy(result => result.Engine)
            .ToList();

    /// <summary>Motores que estaban disponibles pero fallaron.</summary>
    public IReadOnlyList<OcrEngineResult> Failures
        => Results.Where(result => result.Status == OcrEngineStatus.Failed).ToList();

    /// <summary>
    /// Motores que leyeron el codigo finalmente elegido. Es la respuesta a «quien
    /// acerto», que es lo que hace falta para ajustar los pesos del consenso.
    /// </summary>
    public IReadOnlyList<string> SupportingEngines => Verdict.Best?.Engines ?? [];

    /// <summary>
    /// Motores que respondieron con texto pero no propusieron el codigo elegido.
    /// </summary>
    public IReadOnlyList<string> DissentingEngines
    {
        get
        {
            var supporting = new HashSet<string>(SupportingEngines, StringComparer.OrdinalIgnoreCase);

            return Results
                .Where(result => result.Status == OcrEngineStatus.Ok && !supporting.Contains(result.Engine))
                .Select(result => result.Engine)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(engine => engine, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>Resumen de una linea, el que se escribe por pantalla.</summary>
    public override string ToString()
    {
        if (Verdict.Best is not { } best)
        {
            return $"{Path.GetFileName(Source)}: sin codigo " +
                   $"({EnginesResponded} motor(es) en {Duration.TotalSeconds:F1}s)";
        }

        var size = SizeType is null ? string.Empty : $"  {SizeType.Value}";
        var flags = new StringBuilder();

        if (!Verdict.Confirmed)
        {
            flags.Append(" [sin confirmar]");
        }

        if (Verdict.Ambiguous)
        {
            flags.Append(" [ambiguo]");
        }

        return $"{Path.GetFileName(Source)}: {best.Code.ToDisplayString()} " +
               $"{best.Score:P0} ({string.Join(", ", best.Engines)}){size}{flags}";
    }

    /// <summary>
    /// Vuelca el resultado en JSON, para encadenarlo con otra herramienta.
    /// </summary>
    /// <param name="indented">Con saltos de linea y sangria.</param>
    public string ToJson(bool indented = true)
    {
        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Escribe el resultado en un escritor JSON ya abierto.</summary>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteString("fuente", Source);
        writer.WriteNumber("ancho", Width);
        writer.WriteNumber("alto", Height);
        writer.WriteNumber("segundos", Math.Round(Duration.TotalSeconds, 3));
        writer.WriteBoolean("confirmado", Verdict.Confirmed);
        writer.WriteBoolean("ambiguo", Verdict.Ambiguous);

        if (Verdict.Best is { } best)
        {
            writer.WritePropertyName("codigo");
            WriteSupport(writer, best);
        }
        else
        {
            writer.WriteNull("codigo");
        }

        if (SizeType is { } sizeType)
        {
            writer.WriteStartObject("tamanoTipo");
            writer.WriteString("valor", sizeType.Value);
            writer.WriteString("longitud", sizeType.LengthDescription);
            writer.WriteString("altura", sizeType.HeightDescription);
            writer.WriteString("tipo", sizeType.TypeDescription);
            writer.WriteEndObject();
        }

        writer.WriteStartArray("alternativas");
        foreach (var alternative in Verdict.Alternatives)
        {
            WriteSupport(writer, alternative);
        }

        writer.WriteEndArray();

        writer.WriteStartArray("motores");
        foreach (var result in Results)
        {
            writer.WriteStartObject();
            writer.WriteString("motor", result.Engine);
            writer.WriteString("familia", result.Family);
            writer.WriteString("variante", result.Variant);
            writer.WriteString("estado", result.Status.ToString());
            writer.WriteNumber("milisegundos", Math.Round(result.Duration.TotalMilliseconds, 1));
            writer.WriteString("texto", result.JoinedText);
            writer.WriteNumber("confianza", Math.Round(result.MeanConfidence, 4));

            if (result.Error is not null)
            {
                writer.WriteString("error", result.Error);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("evidencias");
        foreach (var evidence in Evidence)
        {
            writer.WriteStartObject();
            writer.WriteString("motor", evidence.Engine);
            writer.WriteString("familia", evidence.Family);
            writer.WriteString("variante", evidence.Variant);
            writer.WriteString("codigo", evidence.Code.Value);
            writer.WriteNumber("confianza", Math.Round(evidence.Confidence, 4));
            writer.WriteNumber("correcciones", evidence.Candidate.Corrections);
            writer.WriteString("textoLeido", evidence.Candidate.RawText);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSupport(Utf8JsonWriter writer, BicSupport support)
    {
        writer.WriteStartObject();
        writer.WriteString("valor", support.Code.Value);
        writer.WriteString("formato", support.Code.ToDisplayString());
        writer.WriteString("propietario", support.Code.OwnerCode);
        writer.WriteString("categoria", support.Code.CategoryIdentifier.ToString());
        writer.WriteString("serie", support.Code.SerialNumber);
        writer.WriteNumber("digitoControl", support.Code.CheckDigit);
        writer.WriteNumber("confianza", Math.Round(support.Score, 4));
        writer.WriteNumber("lecturas", support.Readings);
        writer.WriteNumber("correcciones", support.MinCorrections);

        writer.WriteStartArray("motores");
        foreach (var engine in support.Engines)
        {
            writer.WriteStringValue(engine);
        }

        writer.WriteEndArray();

        writer.WriteStartArray("familias");
        foreach (var family in support.Families)
        {
            writer.WriteStringValue(family);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
