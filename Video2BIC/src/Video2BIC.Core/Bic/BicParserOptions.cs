namespace Video2BIC.Core.Bic;

/// <summary>Configuracion de <see cref="BicCodeParser"/>.</summary>
public sealed class BicParserOptions
{
    /// <summary>
    /// Caracteres que se permite sustituir por un glifo parecido para hacer cuadrar
    /// el digito de control.
    /// </summary>
    /// <remarks>
    /// Subirlo recupera lecturas mas degradadas, pero cada candidato adicional que
    /// se prueba tiene una probabilidad de 1/11 de cuadrar el digito por azar. Dos
    /// es el equilibrio razonable; tres solo compensa si ademas se exigen varias
    /// lecturas coincidentes en el tiempo, que es lo que hace el pipeline.
    /// </remarks>
    public int MaxCorrections { get; set; } = 2;

    /// <summary>Factor por el que se multiplica la confianza en cada correccion.</summary>
    public float CorrectionPenalty { get; set; } = 0.65f;

    /// <summary>
    /// Descartar los candidatos cuyo digito de control no cuadre. Desactivarlo solo
    /// tiene sentido para diagnosticar por que no se lee un contenedor concreto.
    /// </summary>
    public bool RequireValidCheckDigit { get; set; } = true;

    /// <summary>
    /// Confianza que se aplica a un candidato con el digito de control incorrecto,
    /// cuando <see cref="RequireValidCheckDigit"/> esta desactivado.
    /// </summary>
    public float InvalidCheckDigitPenalty { get; set; } = 0.2f;

    /// <summary>
    /// Numero maximo de trozos de texto consecutivos que se concatenan antes de
    /// buscar. En la puerta de un contenedor el codigo suele venir en tres o cuatro
    /// lineas: propietario, categoria, numero de serie y digito de control.
    /// </summary>
    public int MaxJoinedFragments { get; set; } = 4;

    /// <summary>Candidatos devueltos como maximo, ya ordenados por confianza.</summary>
    public int MaxCandidates { get; set; } = 8;

    /// <summary>Variantes exploradas como maximo por cada ventana de once caracteres.</summary>
    public int MaxVariantsPerWindow { get; set; } = 4096;

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun valor es imposible.</exception>
    public void Validate()
    {
        if (MaxCorrections is < 0 or > BicCheckDigit.CodeLength)
        {
            throw new InvalidOperationException(
                $"MaxCorrections debe estar entre 0 y {BicCheckDigit.CodeLength}.");
        }

        if (CorrectionPenalty is <= 0f or > 1f)
        {
            throw new InvalidOperationException("CorrectionPenalty debe estar en (0, 1].");
        }

        if (InvalidCheckDigitPenalty is < 0f or > 1f)
        {
            throw new InvalidOperationException("InvalidCheckDigitPenalty debe estar en [0, 1].");
        }

        if (MaxJoinedFragments < 1)
        {
            throw new InvalidOperationException("MaxJoinedFragments debe ser positivo.");
        }

        if (MaxCandidates < 1)
        {
            throw new InvalidOperationException("MaxCandidates debe ser positivo.");
        }

        if (MaxVariantsPerWindow < 1)
        {
            throw new InvalidOperationException("MaxVariantsPerWindow debe ser positivo.");
        }
    }
}
