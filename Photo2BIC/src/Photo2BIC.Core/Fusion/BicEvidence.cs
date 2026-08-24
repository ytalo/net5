using Video2BIC.Core.Bic;

namespace Photo2BIC.Core.Fusion;

/// <summary>
/// Un codigo propuesto por un motor concreto sobre una variante concreta.
/// </summary>
/// <param name="Engine">Motor que lo leyo.</param>
/// <param name="Family">Familia tecnologica del motor.</param>
/// <param name="Variant">Variante de preprocesado sobre la que lo leyo.</param>
/// <param name="Candidate">El codigo, con la confianza y las correcciones que hicieron falta.</param>
public readonly record struct BicEvidence(string Engine, string Family, string Variant, BicCandidate Candidate)
{
    /// <summary>Codigo propuesto.</summary>
    public BicCode Code => Candidate.Code;

    /// <summary>Confianza de la lectura, ya penalizada por las correcciones.</summary>
    public float Confidence => Candidate.Confidence;

    /// <inheritdoc />
    public override string ToString() => $"{Engine}/{Variant}: {Candidate}";
}

/// <summary>
/// Un codigo con toda la evidencia que lo respalda, ya agregada.
/// </summary>
/// <param name="Code">El codigo.</param>
/// <param name="Score">
/// Confianza combinada en [0, 1], calculada por <see cref="BicConsensus"/>.
/// </param>
/// <param name="Engines">Motores que lo leyeron, en orden alfabetico.</param>
/// <param name="Families">Familias tecnologicas distintas que lo leyeron.</param>
/// <param name="Readings">Lecturas totales, contando variantes.</param>
/// <param name="MinCorrections">
/// Correcciones de la mejor lectura. Cero significa que algun motor lo leyo tal
/// cual, sin necesidad de reparar ninguna letra.
/// </param>
/// <param name="BestConfidence">Confianza de la mejor lectura individual.</param>
public sealed record BicSupport(
    BicCode Code,
    float Score,
    IReadOnlyList<string> Engines,
    IReadOnlyList<string> Families,
    int Readings,
    int MinCorrections,
    float BestConfidence)
{
    /// <summary>Algun motor lo leyo literalmente, sin corregir ningun caracter.</summary>
    public bool HasLiteralReading => MinCorrections == 0;

    /// <inheritdoc />
    public override string ToString()
        => $"{Code.ToDisplayString()} {Score:P0} " +
           $"({Readings} lectura(s) de {Families.Count} familia(s): {string.Join(", ", Engines)})";
}

/// <summary>
/// Conclusion del analisis de una fotografia.
/// </summary>
/// <param name="Best">El codigo mas respaldado, o <c>null</c> si no se leyo ninguno.</param>
/// <param name="Alternatives">
/// Los demas codigos que aparecieron, ordenados de mas a menos respaldo. Casi
/// siempre esta vacio; cuando no lo esta es que la fotografia tenia dos
/// contenedores o que un motor se equivoco de una forma que colo el digito de
/// control.
/// </param>
/// <param name="Confirmed">
/// El respaldo alcanza los umbrales configurados. Un codigo sin confirmar no es
/// necesariamente falso, pero no se debe dar por bueno sin que lo mire alguien.
/// </param>
/// <param name="Ambiguous">
/// Hay otro codigo con un respaldo demasiado parecido al del mejor. Es la
/// situacion que hay que sacar por pantalla en vez de resolver a la brava.
/// </param>
public sealed record BicVerdict(
    BicSupport? Best,
    IReadOnlyList<BicSupport> Alternatives,
    bool Confirmed,
    bool Ambiguous)
{
    /// <summary>Veredicto vacio: ningun motor propuso un codigo valido.</summary>
    public static BicVerdict None { get; } = new(null, [], false, false);

    /// <summary>Se leyo algun codigo, confirmado o no.</summary>
    public bool HasCode => Best is not null;

    /// <inheritdoc />
    public override string ToString() => Best is null
        ? "sin codigo"
        : $"{Best}{(Confirmed ? string.Empty : " [sin confirmar]")}{(Ambiguous ? " [ambiguo]" : string.Empty)}";
}
