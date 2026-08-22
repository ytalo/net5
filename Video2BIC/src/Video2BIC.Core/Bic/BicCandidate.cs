namespace Video2BIC.Core.Bic;

/// <summary>
/// Un codigo BIC propuesto a partir de una lectura de OCR, con la evidencia que
/// lo respalda.
/// </summary>
/// <param name="Code">Codigo reconstruido.</param>
/// <param name="Confidence">
/// Confianza en [0, 1]: la del OCR, penalizada por cada caracter que hubo que
/// corregir y por un digito de control que no cuadre.
/// </param>
/// <param name="Corrections">Caracteres sustituidos respecto al texto leido.</param>
/// <param name="RawText">Los once caracteres tal cual salieron del reconocedor.</param>
public readonly record struct BicCandidate(BicCode Code, float Confidence, int Corrections, string RawText)
{
    /// <summary>Atajo a <see cref="BicCode.HasValidCheckDigit"/>.</summary>
    public bool HasValidCheckDigit => Code.HasValidCheckDigit;

    /// <summary>El OCR acerto los once caracteres sin necesidad de correccion.</summary>
    public bool IsLiteral => Corrections == 0;

    /// <inheritdoc />
    public override string ToString()
    {
        var repairs = Corrections == 0 ? "literal" : $"{Corrections} correccion(es) sobre '{RawText}'";
        return $"{Code.Value} {Confidence:P0} ({repairs})";
    }
}
