using System.Text;
using Video2BIC.Core.Text;

namespace Video2BIC.Core.Bic;

/// <summary>
/// Extrae codigos BIC de texto reconocido en la imagen, aunque venga sucio,
/// partido en varias lineas o con caracteres confundidos.
/// </summary>
/// <remarks>
/// <para>
/// El procedimiento tiene tres pasos. Primero se normaliza y se concatenan los
/// trozos consecutivos, porque el codigo casi nunca cae entero en una sola linea.
/// Despues se recorre el resultado con una ventana de once caracteres. Por ultimo,
/// si la ventana no es un codigo valido tal cual, se prueban sustituciones de
/// glifos parecidos hasta agotar el presupuesto de correcciones.
/// </para>
/// <para>
/// El filtro final siempre es el digito de control. Sin el, permitir correcciones
/// convertiria cualquier matricula, numero de bastidor o rotulo publicitario en un
/// codigo BIC plausible.
/// </para>
/// </remarks>
public sealed class BicCodeParser
{
    private readonly BicParserOptions _options;

    public BicCodeParser(BicParserOptions? options = null)
    {
        _options = options ?? new BicParserOptions();
        _options.Validate();
    }

    /// <summary>Configuracion en uso.</summary>
    public BicParserOptions Options => _options;

    /// <summary>
    /// Busca codigos en un unico texto.
    /// </summary>
    /// <param name="text">Texto reconocido, con o sin ruido.</param>
    /// <param name="confidence">Confianza del reconocedor, en [0, 1].</param>
    /// <returns>Candidatos ordenados de mas a menos plausible.</returns>
    public IReadOnlyList<BicCandidate> Parse(string? text, float confidence = 1f)
        => Parse([new TextFragment(text ?? string.Empty, confidence)]);

    /// <summary>
    /// Busca codigos en los trozos de texto de un recorte, en orden de lectura.
    /// </summary>
    /// <param name="fragments">
    /// Trozos ya ordenados como se leen: de arriba abajo y de izquierda a derecha.
    /// </param>
    /// <returns>Candidatos ordenados de mas a menos plausible.</returns>
    public IReadOnlyList<BicCandidate> Parse(IReadOnlyList<TextFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        var best = new Dictionary<string, BicCandidate>(StringComparer.Ordinal);

        foreach (var (text, confidence) in JoinFragments(fragments))
        {
            ScanWindows(text, confidence, best);
        }

        return best.Values
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Corrections)
            .ThenBy(candidate => candidate.Code.Value, StringComparer.Ordinal)
            .Take(_options.MaxCandidates)
            .ToList();
    }

    /// <summary>
    /// Genera los textos sobre los que buscar: cada trozo por separado y las
    /// concatenaciones de trozos consecutivos, que es como se recompone un codigo
    /// repartido en varias lineas.
    /// </summary>
    private IEnumerable<(string Text, float Confidence)> JoinFragments(IReadOnlyList<TextFragment> fragments)
    {
        var normalized = new List<(string Text, float Confidence)>(fragments.Count);
        foreach (var fragment in fragments)
        {
            var text = fragment.Normalized;
            if (text.Length > 0)
            {
                normalized.Add((text, Math.Clamp(fragment.Confidence, 0f, 1f)));
            }
        }

        var builder = new StringBuilder();

        for (var start = 0; start < normalized.Count; start++)
        {
            builder.Clear();
            var weighted = 0d;
            var characters = 0;

            var limit = Math.Min(_options.MaxJoinedFragments, normalized.Count - start);
            for (var count = 0; count < limit; count++)
            {
                var (text, confidence) = normalized[start + count];
                builder.Append(text);

                // La confianza del texto unido es la media ponderada por longitud:
                // una linea larga y bien leida no deberia quedar arrastrada por un
                // digito suelto reconocido con dudas, ni al reves.
                weighted += confidence * text.Length;
                characters += text.Length;

                if (builder.Length >= BicCheckDigit.CodeLength)
                {
                    yield return (builder.ToString(), (float)(weighted / characters));
                }
            }
        }
    }

    private void ScanWindows(string text, float confidence, Dictionary<string, BicCandidate> best)
    {
        for (var start = 0; start + BicCheckDigit.CodeLength <= text.Length; start++)
        {
            var window = text.AsSpan(start, BicCheckDigit.CodeLength);

            // Camino rapido: el OCR lo leyo bien y no hay nada que reparar.
            if (BicCode.TryParseExact(window, out var literal) && literal.HasValidCheckDigit)
            {
                Offer(best, new BicCandidate(literal, confidence, 0, new string(window)));
                continue;
            }

            Repair(window, confidence, best);
        }
    }

    /// <summary>
    /// Explora las sustituciones de glifos parecidos que hacen de la ventana un
    /// codigo BIC valido, sin pasar del presupuesto de correcciones.
    /// </summary>
    private void Repair(ReadOnlySpan<char> window, float confidence, Dictionary<string, BicCandidate> best)
    {
        var raw = new string(window);
        var buffer = new char[BicCheckDigit.CodeLength];
        var budget = _options.MaxCorrections;
        var explored = 0;

        Descend(0, budget, 0);

        void Descend(int position, int remaining, int corrections)
        {
            if (explored >= _options.MaxVariantsPerWindow)
            {
                return;
            }

            if (position == BicCheckDigit.CodeLength)
            {
                explored++;
                Evaluate(corrections);
                return;
            }

            var original = raw[position];
            foreach (var candidate in CandidatesFor(position, original))
            {
                var cost = candidate == original ? 0 : 1;
                if (cost > remaining)
                {
                    continue;
                }

                buffer[position] = candidate;
                Descend(position + 1, remaining - cost, corrections + cost);
            }
        }

        void Evaluate(int corrections)
        {
            if (corrections == 0)
            {
                // Ya se probo en el camino rapido y no cuadraba.
                return;
            }

            if (!BicCode.TryParseExact(buffer, out var code))
            {
                return;
            }

            if (!code.HasValidCheckDigit && _options.RequireValidCheckDigit)
            {
                return;
            }

            var penalty = (float)Math.Pow(_options.CorrectionPenalty, corrections);
            if (!code.HasValidCheckDigit)
            {
                penalty *= _options.InvalidCheckDigitPenalty;
            }

            Offer(best, new BicCandidate(code, confidence * penalty, corrections, raw));
        }
    }

    /// <summary>
    /// Caracteres admisibles en una posicion: letras en el codigo de propietario,
    /// <c>U</c>/<c>J</c>/<c>Z</c> en la categoria y digitos en el resto.
    /// </summary>
    private static IReadOnlyList<char> CandidatesFor(int position, char original) => position switch
    {
        < 3 => GlyphConfusion.LetterCandidates(original),
        3 => CategoryCandidates(original),
        _ => GlyphConfusion.DigitCandidates(original),
    };

    private static IReadOnlyList<char> CategoryCandidates(char original)
    {
        var candidates = new List<char>(3);
        foreach (var letter in GlyphConfusion.LetterCandidates(original))
        {
            if (letter is 'U' or 'J' or 'Z')
            {
                candidates.Add(letter);
            }
        }

        return candidates;
    }

    private static void Offer(Dictionary<string, BicCandidate> best, in BicCandidate candidate)
    {
        if (!best.TryGetValue(candidate.Code.Value, out var existing)
            || candidate.Confidence > existing.Confidence
            || (candidate.Confidence == existing.Confidence && candidate.Corrections < existing.Corrections))
        {
            best[candidate.Code.Value] = candidate;
        }
    }
}
