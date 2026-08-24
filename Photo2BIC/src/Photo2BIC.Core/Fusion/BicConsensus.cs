using Photo2BIC.Core.Engines;
using Video2BIC.Core.Bic;

namespace Photo2BIC.Core.Fusion;

/// <summary>
/// Decide que codigo BIC lleva el contenedor a partir de lo que leyo cada motor.
/// </summary>
/// <remarks>
/// <para>
/// Video2BIC resolvia esto votando entre los fotogramas en los que se veia el
/// mismo contenedor: la lectura correcta se repite y las equivocadas se dispersan.
/// En una fotografia no hay fotogramas que votar, asi que la variedad hay que
/// buscarla en otra parte: en motores distintos leyendo la misma imagen y en el
/// mismo motor leyendo la imagen preprocesada de varias maneras.
/// </para>
/// <para>
/// La diferencia entre esas dos fuentes es lo importante y es lo que esta clase
/// modela. Dos motores distintos que coinciden son dos opiniones independientes y
/// su acuerdo multiplica la confianza. El mismo motor coincidiendo consigo mismo
/// sobre otra variante es mucho menos: comparte modelo, alfabeto y puntos ciegos,
/// asi que si se equivoca en una imagen tiende a equivocarse igual en la otra. Se
/// agrupa por familia tecnologica y las lecturas dentro de una familia se
/// amortiguan geometricamente.
/// </para>
/// <para>
/// La combinacion es una <i>o</i> ruidosa: la confianza en un codigo es la
/// probabilidad de que <b>al menos una</b> de las evidencias que lo respaldan sea
/// correcta, <c>1 - producto de (1 - confianza)</c>. Tiene la propiedad que hace
/// falta: dos evidencias mediocres suman mas que una buena, y anadir una
/// evidencia jamas baja la confianza. Que no llegue nunca a la certeza lo asegura
/// <c>ConsensusOptions.MaxScore</c>: en aritmetica exacta la formula ya no llega
/// a 1, pero en coma flotante se satura, y presentar un 100 % invitaria a saltarse
/// la revision justo en los casos en los que todos los motores se han equivocado
/// a la vez.
/// </para>
/// <para>
/// Nada de esto sustituye al digito de control, que actua antes: aqui solo llegan
/// codigos que ya cuadran. El consenso decide entre codigos plausibles, no entre
/// cadenas de texto.
/// </para>
/// </remarks>
public sealed class BicConsensus
{
    private readonly ConsensusOptions _options;
    private readonly BicCodeParser _parser;

    /// <param name="options">Configuracion; <c>null</c> usa la de por defecto.</param>
    /// <param name="parser">
    /// Analizador que convierte texto en codigos; <c>null</c> usa uno por defecto,
    /// que exige digito de control valido.
    /// </param>
    public BicConsensus(ConsensusOptions? options = null, BicCodeParser? parser = null)
    {
        _options = options ?? new ConsensusOptions();
        _options.Validate();
        _parser = parser ?? new BicCodeParser();
    }

    /// <summary>Configuracion en uso.</summary>
    public ConsensusOptions Options => _options;

    /// <summary>Analizador de codigos en uso.</summary>
    public BicCodeParser Parser => _parser;

    /// <summary>
    /// Extrae de lo que leyo un motor los codigos que propone.
    /// </summary>
    /// <remarks>
    /// Los trozos van en orden de lectura, que es lo que permite recomponer un
    /// codigo repartido en varias lineas -<c>MSKU</c> arriba, <c>123456</c> debajo
    /// y el digito de control en su recuadro-.
    /// </remarks>
    public IReadOnlyList<BicEvidence> Extract(OcrEngineResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status != OcrEngineStatus.Ok || result.Fragments.Count == 0)
        {
            return [];
        }

        return _parser.Parse(result.Fragments)
            .Select(candidate => new BicEvidence(result.Engine, result.Family, result.Variant, candidate))
            .ToList();
    }

    /// <summary>Extrae los codigos de varios resultados.</summary>
    public IReadOnlyList<BicEvidence> Extract(IEnumerable<OcrEngineResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results.SelectMany(Extract).ToList();
    }

    /// <summary>Analiza los resultados de los motores y decide.</summary>
    public BicVerdict Evaluate(IEnumerable<OcrEngineResult> results) => Decide(Extract(results));

    /// <summary>
    /// Decide a partir de la evidencia ya extraida.
    /// </summary>
    /// <param name="evidence">Codigos propuestos por los motores.</param>
    public BicVerdict Decide(IReadOnlyList<BicEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Count == 0)
        {
            return BicVerdict.None;
        }

        var supports = evidence
            .GroupBy(item => item.Code.Value, StringComparer.Ordinal)
            .Select(Aggregate)
            .OrderByDescending(support => support.Score)
            .ThenByDescending(support => support.Families.Count)
            .ThenBy(support => support.MinCorrections)
            .ThenBy(support => support.Code.Value, StringComparer.Ordinal)
            .ToList();

        var best = supports[0];

        var ambiguous = supports.Count > 1
                        && best.Score - supports[1].Score < _options.AmbiguityMargin;

        var confirmed = best.Score >= _options.MinScore
                        && best.Families.Count >= _options.MinFamilies
                        && !ambiguous;

        return new BicVerdict(
            best,
            supports.Skip(1).Take(_options.MaxAlternatives).ToList(),
            confirmed,
            ambiguous);
    }

    /// <summary>
    /// Combina toda la evidencia de un mismo codigo en una confianza unica.
    /// </summary>
    private BicSupport Aggregate(IGrouping<string, BicEvidence> group)
    {
        var readings = group.ToList();
        var score = 0f;

        foreach (var family in readings.GroupBy(item => item.Family, StringComparer.OrdinalIgnoreCase))
        {
            var weight = _options.WeightOf(family.Key);
            if (weight <= 0f)
            {
                continue;
            }

            // Dentro de la familia: o ruidosa amortiguada. La mejor lectura entra
            // entera y cada una que la sigue aporta cada vez menos, porque cada vez
            // es menos creible que este diciendo algo nuevo.
            var familyScore = 0f;
            var damping = 1f;

            foreach (var reading in family.OrderByDescending(item => item.Confidence))
            {
                familyScore += (1f - familyScore) * Math.Clamp(reading.Confidence, 0f, 1f) * damping;
                damping *= _options.CorrelatedDiscount;
            }

            // Entre familias: o ruidosa sin amortiguar. Son evidencias
            // independientes y su acuerdo es exactamente lo que se queria medir.
            score += (1f - score) * familyScore * weight;
        }

        var families = readings
            .Select(item => item.Family)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(family => family, StringComparer.Ordinal)
            .ToList();

        var engines = readings
            .Select(item => item.Engine)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(engine => engine, StringComparer.Ordinal)
            .ToList();

        return new BicSupport(
            readings[0].Code,
            Math.Clamp(score, 0f, _options.MaxScore),
            engines,
            families,
            readings.Count,
            readings.Min(item => item.Candidate.Corrections),
            readings.Max(item => item.Confidence));
    }
}
