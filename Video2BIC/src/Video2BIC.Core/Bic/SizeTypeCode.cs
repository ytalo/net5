using Video2BIC.Core.Text;

namespace Video2BIC.Core.Bic;

/// <summary>
/// Codigo de tamano y tipo de la ISO 6346: los cuatro caracteres que suelen ir
/// justo debajo del codigo BIC (<c>22G1</c>, <c>45R1</c>, <c>42U6</c>...).
/// </summary>
/// <remarks>
/// No lleva digito de control, asi que no se puede validar como el codigo BIC.
/// Lo que si se puede es exigir que cada caracter pertenezca a la tabla de la
/// norma, que ya descarta casi cualquier cosa que no sea un codigo real.
/// </remarks>
public sealed record SizeTypeCode
{
    private SizeTypeCode(string value, string length, string height, string type)
    {
        Value = value;
        LengthDescription = length;
        HeightDescription = height;
        TypeDescription = type;
    }

    /// <summary>Los cuatro caracteres, en mayusculas.</summary>
    public string Value { get; }

    /// <summary>Longitud nominal, legible.</summary>
    public string LengthDescription { get; }

    /// <summary>Altura y anchura nominales, legibles.</summary>
    public string HeightDescription { get; }

    /// <summary>Tipo de contenedor, legible.</summary>
    public string TypeDescription { get; }

    /// <summary>Caracter de longitud.</summary>
    public char LengthCode => Value[0];

    /// <summary>Caracter de altura y anchura.</summary>
    public char HeightCode => Value[1];

    /// <summary>Los dos caracteres de tipo.</summary>
    public string TypeCode => Value[2..];

    private static readonly Dictionary<char, string> Lengths = new()
    {
        ['1'] = "2991 mm (10 pies)",
        ['2'] = "6058 mm (20 pies)",
        ['3'] = "9125 mm (30 pies)",
        ['4'] = "12192 mm (40 pies)",
        ['A'] = "7150 mm",
        ['B'] = "7315 mm (24 pies)",
        ['C'] = "7430 mm (24 pies 6 pulgadas)",
        ['D'] = "7450 mm",
        ['E'] = "7820 mm",
        ['F'] = "8100 mm",
        ['G'] = "12500 mm (41 pies)",
        ['H'] = "13106 mm (43 pies)",
        ['K'] = "13600 mm (44 pies 8 pulgadas)",
        ['L'] = "13716 mm (45 pies)",
        ['M'] = "14630 mm (48 pies)",
        ['N'] = "14935 mm (49 pies)",
        ['P'] = "16154 mm (53 pies)",
    };

    private static readonly Dictionary<char, string> Heights = new()
    {
        ['0'] = "2438 mm (8 pies) de alto, 2438 mm de ancho",
        ['2'] = "2591 mm (8 pies 6 pulgadas) de alto, 2438 mm de ancho",
        ['4'] = "2743 mm (9 pies) de alto, 2438 mm de ancho",
        ['5'] = "2896 mm (9 pies 6 pulgadas) de alto, 2438 mm de ancho",
        ['6'] = "mas de 2896 mm de alto, 2438 mm de ancho",
        ['8'] = "1295 mm (4 pies 3 pulgadas) de alto, 2438 mm de ancho",
        ['9'] = "hasta 1219 mm (4 pies) de alto, 2438 mm de ancho",
        ['C'] = "2591 mm de alto, entre 2438 y 2500 mm de ancho",
        ['D'] = "2743 mm de alto, entre 2438 y 2500 mm de ancho",
        ['E'] = "2895 mm de alto, entre 2438 y 2500 mm de ancho",
        ['F'] = "mas de 2895 mm de alto, entre 2438 y 2500 mm de ancho",
        ['L'] = "2591 mm de alto, mas de 2500 mm de ancho",
        ['M'] = "2743 mm de alto, mas de 2500 mm de ancho",
        ['N'] = "2895 mm de alto, mas de 2500 mm de ancho",
        ['P'] = "mas de 2895 mm de alto, mas de 2500 mm de ancho",
    };

    // Grupo de tipo: el primero de los dos caracteres finales.
    private static readonly Dictionary<char, string> TypeGroups = new()
    {
        ['G'] = "uso general sin ventilacion",
        ['V'] = "uso general con ventilacion",
        ['B'] = "granel seco",
        ['S'] = "carga especifica",
        ['R'] = "frigorifico",
        ['H'] = "termico con equipo removible",
        ['U'] = "techo abierto (open top)",
        ['P'] = "plataforma (flat rack)",
        ['T'] = "cisterna",
        ['A'] = "aereo / superficie",
    };

    // Codigos completos frecuentes, para no quedarse en la descripcion generica.
    private static readonly Dictionary<string, string> TypeDetails = new(StringComparer.Ordinal)
    {
        ["G0"] = "uso general, aberturas en uno o ambos extremos",
        ["G1"] = "uso general, aberturas en un extremo y rejillas de ventilacion pasiva",
        ["G2"] = "uso general, aberturas en un extremo y en un lateral",
        ["G3"] = "uso general, aberturas en un extremo y en ambos laterales",
        ["V0"] = "ventilado, ventilacion natural",
        ["V2"] = "ventilado, ventilacion forzada interna",
        ["V4"] = "ventilado, ventilacion forzada externa",
        ["B0"] = "granel seco, cerrado",
        ["B1"] = "granel seco, presurizable",
        ["S0"] = "carga especifica, ganado",
        ["S1"] = "carga especifica, automoviles",
        ["S2"] = "carga especifica, pesca viva",
        ["R0"] = "frigorifico con equipo integrado",
        ["R1"] = "frigorifico y calefactado con equipo integrado",
        ["R2"] = "frigorifico autoportante, refrigeracion mecanica",
        ["R3"] = "frigorifico autoportante, refrigeracion mecanica y calefaccion",
        ["H0"] = "termico, equipo de frio o calor removible en el exterior",
        ["H1"] = "termico, equipo removible en el interior",
        ["H2"] = "termico, equipo removible en el exterior",
        ["H5"] = "aislado sin equipo, aislamiento reforzado",
        ["U0"] = "techo abierto, aberturas en uno o ambos extremos",
        ["U1"] = "techo abierto, aberturas en un extremo y techo desmontable",
        ["U2"] = "techo abierto, aberturas en un extremo y en un lateral",
        ["U6"] = "techo abierto, media altura con aberturas en ambos extremos",
        ["P0"] = "plataforma sin estructura",
        ["P1"] = "plataforma con dos cabeceros fijos",
        ["P2"] = "plataforma con cabeceros abatibles",
        ["P3"] = "plataforma con cabeceros plegables",
        ["P4"] = "plataforma con superestructura completa",
        ["P5"] = "plataforma con superestructura abierta",
        ["T0"] = "cisterna para liquidos no peligrosos, presion minima",
        ["T1"] = "cisterna para liquidos no peligrosos, hasta 0,45 bar",
        ["T2"] = "cisterna para liquidos no peligrosos, hasta 1,50 bar",
        ["T3"] = "cisterna para liquidos peligrosos, hasta 1,50 bar",
        ["T4"] = "cisterna para liquidos peligrosos, hasta 2,65 bar",
        ["T5"] = "cisterna para gases, hasta 7,00 bar",
        ["T6"] = "cisterna para gases, hasta 9,80 bar",
        ["T7"] = "cisterna para gases licuados",
        ["T8"] = "cisterna para productos secos a granel presurizados",
    };

    /// <summary>
    /// Interpreta un codigo de tamano y tipo. Acepta espacios y guiones.
    /// </summary>
    public static bool TryParse(string? text, out SizeTypeCode code)
    {
        code = null!;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Span<char> buffer = stackalloc char[4];
        var length = 0;

        foreach (var character in text)
        {
            if (character is ' ' or '-' or '_' or '.')
            {
                continue;
            }

            if (length == buffer.Length || !char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }

            buffer[length++] = char.ToUpperInvariant(character);
        }

        return length == buffer.Length && TryParseExact(buffer, out code);
    }

    /// <summary>Interpreta cuatro caracteres ya normalizados.</summary>
    public static bool TryParseExact(ReadOnlySpan<char> text, out SizeTypeCode code)
    {
        code = null!;

        if (text.Length != 4
            || !Lengths.TryGetValue(text[0], out var length)
            || !Heights.TryGetValue(text[1], out var height)
            || !TypeGroups.TryGetValue(text[2], out var group)
            || !char.IsAsciiLetterOrDigit(text[3]))
        {
            return false;
        }

        var value = new string(text);
        var detail = TypeDetails.TryGetValue(value[2..], out var described) ? described : group;

        code = new SizeTypeCode(value, length, height, detail);
        return true;
    }

    /// <summary>
    /// Busca un codigo de tamano y tipo entre los trozos de texto reconocidos,
    /// ignorando el tramo que ya ocupa el codigo BIC.
    /// </summary>
    /// <param name="fragments">Trozos de texto en orden de lectura.</param>
    /// <param name="exclude">Codigo BIC ya identificado, para no leerlo dos veces.</param>
    /// <param name="code">Codigo encontrado.</param>
    public static bool TryFind(
        IReadOnlyList<TextFragment> fragments,
        string? exclude,
        out SizeTypeCode code)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        code = null!;

        foreach (var fragment in fragments)
        {
            var normalized = fragment.Normalized;

            // Un tramo que solapa con el propio codigo BIC no es un codigo de
            // tamano: hay que saltarselo o "U123" se colaria como candidato.
            var bicStart = string.IsNullOrEmpty(exclude)
                ? -1
                : normalized.IndexOf(exclude, StringComparison.Ordinal);

            for (var start = 0; start + 4 <= normalized.Length; start++)
            {
                if (bicStart >= 0 && start < bicStart + exclude!.Length && start + 4 > bicStart)
                {
                    continue;
                }

                if (TryParseExact(normalized.AsSpan(start, 4), out code))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Resumen legible de una linea.</summary>
    public string ToDisplayString()
        => $"{Value}: {LengthDescription}, {HeightDescription}, {TypeDescription}";

    /// <inheritdoc />
    public override string ToString() => Value;
}
