using Photo2BIC.Core.Engines;
using Video2BIC.Core.Bic;
using Photo2BIC.Core.Fusion;
using Photo2BIC.Core.Pipeline;

namespace Photo2BIC.Cli;

/// <summary>
/// Presenta los resultados por pantalla.
/// </summary>
/// <remarks>
/// La aplicacion existe para comparar motores, asi que lo que se ensena no es
/// solo el codigo: es quien lo leyo, quien no y en que se equivoco cada uno. Esa
/// tabla es la herramienta de trabajo real, la que dice si merece la pena pagar
/// un servicio o si con Tesseract ya basta para las fotos que uno tiene.
/// </remarks>
public static class Output
{
    /// <summary>Escribe una tabla con columnas alineadas.</summary>
    /// <param name="headers">Titulos de las columnas.</param>
    /// <param name="rows">Filas; cada una con tantas celdas como titulos.</param>
    /// <param name="indent">Sangria de la tabla.</param>
    public static void Table(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, string indent = "  ")
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var widths = new int[headers.Count];

        for (var column = 0; column < headers.Count; column++)
        {
            widths[column] = headers[column].Length;

            foreach (var row in rows)
            {
                if (column < row.Length)
                {
                    widths[column] = Math.Max(widths[column], row[column].Length);
                }
            }
        }

        Console.WriteLine(indent + Line(headers.ToArray(), widths));
        Console.WriteLine(indent + string.Join("  ", widths.Select(width => new string('-', width))));

        foreach (var row in rows)
        {
            Console.WriteLine(indent + Line(row, widths));
        }
    }

    /// <summary>Escribe el resultado completo del analisis de una fotografia.</summary>
    /// <param name="result">Resultado.</param>
    /// <param name="detail">Incluir la tabla de lo que leyo cada motor.</param>
    public static void Report(PhotoReadResult result, bool detail)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (detail)
        {
            WriteEngineTable(result);
        }

        WriteEvidence(result);
        WriteVerdict(result);
        WriteProblems(result);
    }

    /// <summary>Tabla de motores disponibles y no disponibles.</summary>
    public static void Engines(IReadOnlyList<(IPhotoOcrEngine Engine, bool Available, string Reason)> diagnosis)
    {
        ArgumentNullException.ThrowIfNull(diagnosis);

        var rows = diagnosis
            .Select(entry => new[]
            {
                entry.Engine.Name,
                entry.Engine.Family,
                entry.Engine.Kind == OcrEngineKind.Local ? "local" : "nube",
                entry.Available ? "si" : "NO",
                entry.Available ? entry.Reason : $"falta: {entry.Reason}",
            })
            .ToList();

        Table(["motor", "familia", "donde", "listo", "detalle"], rows);
    }

    private static void WriteEngineTable(PhotoReadResult result)
    {
        var rows = result.Results
            .Select(engine => new[]
            {
                engine.Engine,
                engine.Variant,
                engine.Status == OcrEngineStatus.Ok
                    ? $"{engine.Duration.TotalMilliseconds:F0}"
                    : "-",
                engine.Status == OcrEngineStatus.Ok
                    ? $"{engine.MeanConfidence:P0}"
                    : engine.Status.ToString(),
                engine.Status == OcrEngineStatus.Ok
                    ? Shorten(engine.JoinedText, 52)
                    : Shorten(engine.Error ?? string.Empty, 52),
            })
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Lo que leyo cada motor:");
        Table(["motor", "variante", "ms", "conf", "texto"], rows);
    }

    private static void WriteEvidence(PhotoReadResult result)
    {
        if (result.Evidence.Count == 0)
        {
            return;
        }

        // Se agrupa por codigo para que se vea de un vistazo quien apoya que, que es
        // exactamente lo que el consenso esta decidiendo.
        var rows = result.Evidence
            .GroupBy(evidence => evidence.Code.Value, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .SelectMany(group => group
                .OrderByDescending(evidence => evidence.Confidence)
                .Select(evidence => new[]
                {
                    evidence.Code.Value,
                    evidence.Engine,
                    evidence.Variant,
                    $"{evidence.Confidence:P0}",
                    evidence.Candidate.Corrections == 0
                        ? "literal"
                        : $"{evidence.Candidate.Corrections} sobre '{evidence.Candidate.RawText}'",
                }))
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Codigos propuestos:");
        Table(["codigo", "motor", "variante", "conf", "correcciones"], rows);
    }

    private static void WriteVerdict(PhotoReadResult result)
    {
        Console.WriteLine();

        if (result.Verdict.Best is not { } best)
        {
            Console.WriteLine("Sin codigo BIC.");
            Console.WriteLine(
                $"  {result.EnginesResponded} lectura(s) de motor en {result.Duration.TotalSeconds:F1}s, " +
                "ninguna dio un codigo con el digito de control correcto.");
            return;
        }

        Console.WriteLine($"Codigo:      {best.Code.ToDisplayString()}");
        Console.WriteLine($"Propietario: {best.Code.OwnerCode}   " +
                          $"Categoria: {best.Code.CategoryIdentifier} ({Describe(best.Code.Category)})   " +
                          $"Serie: {best.Code.SerialNumber}   Control: {best.Code.CheckDigit}");

        if (result.SizeType is { } sizeType)
        {
            Console.WriteLine($"Tamano:      {sizeType.Value} - {sizeType.LengthDescription}, " +
                              $"{sizeType.HeightDescription}, {sizeType.TypeDescription}");
        }

        Console.WriteLine($"Confianza:   {best.Score:P0} " +
                          $"({best.Readings} lectura(s), {best.Families.Count} familia(s): " +
                          $"{string.Join(", ", best.Engines)})");

        if (!best.HasLiteralReading)
        {
            Console.WriteLine($"             ningun motor lo leyo literal; " +
                              $"la mejor lectura necesito {best.MinCorrections} correccion(es).");
        }

        Console.WriteLine(result.Verdict.Confirmed
            ? "Veredicto:   CONFIRMADO"
            : "Veredicto:   SIN CONFIRMAR - conviene que lo revise una persona.");

        if (result.Verdict.Ambiguous && result.Verdict.Alternatives.Count > 0)
        {
            Console.WriteLine(
                $"             hay otro codigo casi igual de respaldado: " +
                $"{result.Verdict.Alternatives[0].Code.Value} ({result.Verdict.Alternatives[0].Score:P0}).");
        }

        var dissenting = result.DissentingEngines;
        if (dissenting.Count > 0)
        {
            Console.WriteLine($"             no lo respaldan: {string.Join(", ", dissenting)}.");
        }
    }

    private static void WriteProblems(PhotoReadResult result)
    {
        foreach (var failure in result.Failures)
        {
            Console.Error.WriteLine($"[aviso] {failure.Engine} fallo: {failure.Error}");
        }

        var unavailable = result.Unavailable;
        if (unavailable.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Motores no disponibles (el analisis siguio sin ellos):");

        foreach (var engine in unavailable)
        {
            Console.WriteLine($"  {engine.Engine}: {engine.Error}");
        }
    }

    private static string Describe(BicCategory category) => category switch
    {
        BicCategory.FreightContainer => "contenedor de carga",
        BicCategory.DetachableEquipment => "equipo desmontable",
        BicCategory.TrailerOrChassis => "chasis o plataforma",
        _ => "desconocida",
    };

    private static string Line(IReadOnlyList<string> cells, IReadOnlyList<int> widths)
    {
        var parts = new List<string>(widths.Count);

        for (var column = 0; column < widths.Count; column++)
        {
            var cell = column < cells.Count ? cells[column] : string.Empty;

            // La ultima columna no se rellena, para no dejar cola de espacios.
            parts.Add(column == widths.Count - 1 ? cell : cell.PadRight(widths[column]));
        }

        return string.Join("  ", parts).TrimEnd();
    }

    private static string Shorten(string text, int length)
    {
        var single = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= length ? single : string.Concat(single.AsSpan(0, length - 1), "…");
    }
}
