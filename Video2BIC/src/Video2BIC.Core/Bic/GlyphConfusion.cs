namespace Video2BIC.Core.Bic;

/// <summary>
/// Grupos de caracteres que un OCR confunde entre si sobre chapa corrugada.
/// </summary>
/// <remarks>
/// <para>
/// El codigo BIC mezcla letras y digitos en posiciones fijas, y esa rigidez es
/// justo lo que permite recuperar lecturas dudosas: si en la posicion de una
/// letra aparece un <c>0</c>, el candidato real es <c>O</c>, <c>Q</c> o <c>D</c>,
/// no un digito. Cada sustitucion se propone, no se aplica: quien decide es el
/// digito de control.
/// </para>
/// <para>
/// Los grupos se mantienen deliberadamente cortos. Cuantas mas alternativas se
/// admitan, mas probable es que una lectura equivocada acabe cuadrando el digito
/// de control por casualidad (hay una probabilidad de 1/11 por cada candidato
/// que se prueba).
/// </para>
/// </remarks>
public static class GlyphConfusion
{
    // Cada grupo reune glifos que se parecen lo suficiente como para que un
    // reconocedor los intercambie. El primer elemento no tiene ningun papel
    // especial: la relacion es simetrica.
    private static readonly string[] Groups =
    [
        "0OQD",
        "1IL",
        "2Z",
        "4A",
        "5S",
        "6G",
        "7T",
        "8B",
        "UV",
    ];

    private static readonly Dictionary<char, string> GroupByCharacter = BuildIndex();

    /// <summary>
    /// Letras que pueden estar detras de <paramref name="character"/>, incluida
    /// ella misma si ya es una letra. En orden estable, la lectura literal primero.
    /// </summary>
    public static IReadOnlyList<char> LetterCandidates(char character)
        => Candidates(character, char.IsAsciiLetterUpper);

    /// <summary>
    /// Digitos que pueden estar detras de <paramref name="character"/>, incluido
    /// el mismo si ya es un digito.
    /// </summary>
    public static IReadOnlyList<char> DigitCandidates(char character)
        => Candidates(character, char.IsAsciiDigit);

    /// <summary>
    /// <c>true</c> si los dos caracteres pertenecen al mismo grupo de confusion
    /// (o son el mismo caracter).
    /// </summary>
    public static bool AreConfusable(char first, char second)
        => first == second
           || (GroupByCharacter.TryGetValue(first, out var group) && group.Contains(second));

    private static IReadOnlyList<char> Candidates(char character, Func<char, bool> isAcceptable)
    {
        var candidates = new List<char>(4);

        if (isAcceptable(character))
        {
            candidates.Add(character);
        }

        if (GroupByCharacter.TryGetValue(character, out var group))
        {
            foreach (var alternative in group)
            {
                if (alternative != character && isAcceptable(alternative))
                {
                    candidates.Add(alternative);
                }
            }
        }

        return candidates;
    }

    private static Dictionary<char, string> BuildIndex()
    {
        var index = new Dictionary<char, string>();
        foreach (var group in Groups)
        {
            foreach (var character in group)
            {
                index[character] = group;
            }
        }

        return index;
    }
}
