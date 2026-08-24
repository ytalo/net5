namespace Photo2BIC.Core.Fusion;

/// <summary>Configuracion de <see cref="BicConsensus"/>.</summary>
public sealed class ConsensusOptions
{
    /// <summary>
    /// Factor con el que se amortigua cada lectura adicional de la misma familia.
    /// </summary>
    /// <remarks>
    /// Dos lecturas del mismo motor sobre dos variantes de la misma fotografia no
    /// son dos opiniones independientes: comparten el modelo, el alfabeto y los
    /// mismos puntos ciegos. Aportan algo -la variante binarizada y la de contraste
    /// igualado no fallan exactamente igual-, pero mucho menos que un motor
    /// distinto diciendo lo mismo. Con 0,35 la segunda lectura de una familia vale
    /// aproximadamente un tercio, la tercera un noveno y la cuarta ya casi nada.
    /// </remarks>
    public float CorrelatedDiscount { get; set; } = 0.35f;

    /// <summary>
    /// Peso por familia tecnologica, entre 0 y 1. Las que no figuren pesan
    /// <see cref="DefaultFamilyWeight"/>.
    /// </summary>
    /// <remarks>
    /// Vacio por defecto, y a proposito: no hay una jerarquia universal de motores.
    /// Cual acierta mas depende de las camaras, de la luz y del estado de la flota
    /// que se este leyendo, asi que estos pesos se ajustan midiendo sobre las
    /// propias fotografias, no copiando los de otro.
    /// </remarks>
    public Dictionary<string, float> FamilyWeights { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Peso de una familia que no figure en <see cref="FamilyWeights"/>.</summary>
    public float DefaultFamilyWeight { get; set; } = 1f;

    /// <summary>Confianza combinada minima para dar un codigo por confirmado.</summary>
    public float MinScore { get; set; } = 0.6f;

    /// <summary>Confianza maxima que se puede alcanzar, por mucha evidencia que haya.</summary>
    /// <remarks>
    /// Una <i>o</i> ruidosa nunca llega a 1 en aritmetica exacta, pero si en coma
    /// flotante: a partir de una docena de evidencias fuertes el complemento
    /// (1 - confianza) cae por debajo de lo que un <c>float</c> representa y el
    /// resultado se satura. El techo lo evita, y ademas dice algo cierto: por
    /// muchos motores que coincidan, lo que hay debajo sigue siendo un OCR sobre
    /// una fotografia. Presentar un 100 % induciria a saltarse la revision
    /// justamente en los casos en los que el consenso se ha equivocado en bloque
    /// -una foto movida, un panel repintado- que existen y son los caros.
    /// </remarks>
    public float MaxScore { get; set; } = 0.999f;

    /// <summary>
    /// Familias distintas que tienen que coincidir para dar un codigo por
    /// confirmado.
    /// </summary>
    /// <remarks>
    /// Uno por defecto: con un solo motor instalado la aplicacion tiene que seguir
    /// sirviendo para algo. Subirlo a dos es lo razonable en produccion, donde el
    /// coste de dar por bueno un codigo equivocado es un contenedor mal asignado.
    /// </remarks>
    public int MinFamilies { get; set; } = 1;

    /// <summary>
    /// Diferencia de confianza por debajo de la cual dos codigos se consideran
    /// empatados, y el resultado se marca como ambiguo.
    /// </summary>
    public float AmbiguityMargin { get; set; } = 0.15f;

    /// <summary>Codigos alternativos que se conservan en el veredicto.</summary>
    public int MaxAlternatives { get; set; } = 4;

    /// <summary>Peso efectivo de una familia.</summary>
    public float WeightOf(string family)
        => FamilyWeights.TryGetValue(family, out var weight) ? weight : DefaultFamilyWeight;

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun valor es imposible.</exception>
    public void Validate()
    {
        if (CorrelatedDiscount is < 0f or > 1f)
        {
            throw new InvalidOperationException("CorrelatedDiscount debe estar en [0, 1].");
        }

        if (DefaultFamilyWeight is < 0f or > 1f)
        {
            throw new InvalidOperationException("DefaultFamilyWeight debe estar en [0, 1].");
        }

        foreach (var (family, weight) in FamilyWeights)
        {
            if (weight is < 0f or > 1f)
            {
                throw new InvalidOperationException($"El peso de la familia '{family}' debe estar en [0, 1].");
            }
        }

        if (MinScore is < 0f or > 1f)
        {
            throw new InvalidOperationException("MinScore debe estar en [0, 1].");
        }

        if (MaxScore is <= 0f or > 1f)
        {
            throw new InvalidOperationException("MaxScore debe estar en (0, 1].");
        }

        if (MaxScore < MinScore)
        {
            throw new InvalidOperationException("MaxScore no puede ser menor que MinScore.");
        }

        if (MinFamilies < 1)
        {
            throw new InvalidOperationException("MinFamilies debe ser positivo.");
        }

        if (AmbiguityMargin is < 0f or > 1f)
        {
            throw new InvalidOperationException("AmbiguityMargin debe estar en [0, 1].");
        }

        if (MaxAlternatives < 0)
        {
            throw new InvalidOperationException("MaxAlternatives no puede ser negativo.");
        }
    }
}
