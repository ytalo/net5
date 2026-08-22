namespace Video2BIC.Core.Bic;

/// <summary>Categoria de equipo, la cuarta letra del codigo BIC.</summary>
public enum BicCategory
{
    /// <summary>Categoria no reconocida.</summary>
    Unknown = 0,

    /// <summary><c>U</c>: contenedor de carga. Es la inmensa mayoria del trafico.</summary>
    FreightContainer = 1,

    /// <summary><c>J</c>: equipo desmontable asociado a un contenedor de carga.</summary>
    DetachableEquipment = 2,

    /// <summary><c>Z</c>: chasis y plataformas de transporte.</summary>
    TrailerOrChassis = 3,
}

/// <summary>
/// Un codigo BIC (ISO 6346) ya interpretado: propietario, categoria, numero de
/// serie y digito de control.
/// </summary>
/// <remarks>
/// Solo se puede construir a traves de <see cref="TryParse(string, out BicCode)"/>
/// o <see cref="Create"/>, asi que una instancia siempre esta bien formada. Que
/// ademas cuadre el digito de control se consulta en
/// <see cref="HasValidCheckDigit"/>: al reconstruir una lectura de OCR interesa
/// poder representar un codigo con la forma correcta y el control fallido.
/// </remarks>
public sealed record BicCode
{
    private BicCode(string ownerCode, char categoryIdentifier, string serialNumber, int checkDigit)
    {
        OwnerCode = ownerCode;
        CategoryIdentifier = categoryIdentifier;
        SerialNumber = serialNumber;
        CheckDigit = checkDigit;
    }

    /// <summary>Codigo de propietario: tres letras registradas en el BIC.</summary>
    public string OwnerCode { get; }

    /// <summary>Identificador de categoria: <c>U</c>, <c>J</c> o <c>Z</c>.</summary>
    public char CategoryIdentifier { get; }

    /// <summary>Numero de serie: seis digitos asignados por el propietario.</summary>
    public string SerialNumber { get; }

    /// <summary>Digito de control leido, en el rango [0, 9].</summary>
    public int CheckDigit { get; }

    /// <summary>Categoria de equipo.</summary>
    public BicCategory Category => CategoryIdentifier switch
    {
        'U' => BicCategory.FreightContainer,
        'J' => BicCategory.DetachableEquipment,
        'Z' => BicCategory.TrailerOrChassis,
        _ => BicCategory.Unknown,
    };

    /// <summary>Los diez caracteres que alimentan el digito de control.</summary>
    public string Prefix => $"{OwnerCode}{CategoryIdentifier}{SerialNumber}";

    /// <summary>Codigo completo de once caracteres, sin separadores.</summary>
    public string Value => $"{Prefix}{CheckDigit}";

    /// <summary>Digito de control que corresponde al prefijo segun ISO 6346.</summary>
    public int ExpectedCheckDigit => BicCheckDigit.Compute(Prefix);

    /// <summary>
    /// <c>true</c> si el digito leido coincide con el calculado. Es el criterio
    /// que separa una lectura fiable de una que hay que descartar.
    /// </summary>
    public bool HasValidCheckDigit => CheckDigit == ExpectedCheckDigit;

    /// <summary>Presentacion habitual en documentacion: <c>ABCU 123456 7</c>.</summary>
    public string ToDisplayString() => $"{OwnerCode}{CategoryIdentifier} {SerialNumber} {CheckDigit}";

    /// <summary>
    /// Construye un codigo a partir de sus partes, calculando el digito de control
    /// si no se indica.
    /// </summary>
    /// <param name="ownerCode">Tres letras.</param>
    /// <param name="categoryIdentifier"><c>U</c>, <c>J</c> o <c>Z</c>.</param>
    /// <param name="serialNumber">Seis digitos.</param>
    /// <param name="checkDigit">Digito de control; <c>null</c> para calcularlo.</param>
    /// <exception cref="ArgumentException">Si alguna parte no tiene la forma exigida.</exception>
    public static BicCode Create(string ownerCode, char categoryIdentifier, string serialNumber, int? checkDigit = null)
    {
        ArgumentNullException.ThrowIfNull(ownerCode);
        ArgumentNullException.ThrowIfNull(serialNumber);

        var owner = ownerCode.ToUpperInvariant();
        var category = char.ToUpperInvariant(categoryIdentifier);

        if (owner.Length != 3 || !owner.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("El codigo de propietario son tres letras.", nameof(ownerCode));
        }

        if (category is not ('U' or 'J' or 'Z'))
        {
            throw new ArgumentException(
                "El identificador de categoria debe ser U, J o Z.", nameof(categoryIdentifier));
        }

        if (serialNumber.Length != 6 || !serialNumber.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("El numero de serie son seis digitos.", nameof(serialNumber));
        }

        var digit = checkDigit ?? BicCheckDigit.Compute($"{owner}{category}{serialNumber}");
        if (digit is < 0 or > 9)
        {
            throw new ArgumentException("El digito de control esta fuera de [0, 9].", nameof(checkDigit));
        }

        return new BicCode(owner, category, serialNumber, digit);
    }

    /// <summary>
    /// Interpreta un codigo de once caracteres. Acepta espacios y guiones
    /// intercalados, que es como aparece escrito en la documentacion de embarque.
    /// </summary>
    /// <remarks>
    /// No exige que el digito de control cuadre; eso se consulta despues en
    /// <see cref="HasValidCheckDigit"/>.
    /// </remarks>
    public static bool TryParse(string? text, out BicCode code)
        => TryParse(text.AsSpan(), out code);

    /// <inheritdoc cref="TryParse(string, out BicCode)" />
    public static bool TryParse(ReadOnlySpan<char> text, out BicCode code)
    {
        code = null!;

        Span<char> buffer = stackalloc char[BicCheckDigit.CodeLength];
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

    /// <summary>
    /// Interpreta once caracteres alfanumericos ya normalizados (mayusculas, sin
    /// separadores). Es el camino que recorre el analizador por cada ventana
    /// candidata, de ahi que no admita ninguna limpieza previa.
    /// </summary>
    public static bool TryParseExact(ReadOnlySpan<char> code, out BicCode result)
    {
        result = null!;

        if (code.Length != BicCheckDigit.CodeLength)
        {
            return false;
        }

        for (var index = 0; index < 3; index++)
        {
            if (!char.IsAsciiLetterUpper(code[index]))
            {
                return false;
            }
        }

        if (code[3] is not ('U' or 'J' or 'Z'))
        {
            return false;
        }

        for (var index = 4; index < BicCheckDigit.CodeLength; index++)
        {
            if (!char.IsAsciiDigit(code[index]))
            {
                return false;
            }
        }

        result = new BicCode(
            new string(code[..3]),
            code[3],
            new string(code[4..10]),
            code[10] - '0');

        return true;
    }

    /// <summary>
    /// <c>true</c> si el texto es un codigo BIC completo y con el digito de
    /// control correcto.
    /// </summary>
    public static bool IsValid(string? text)
        => TryParse(text, out var code) && code.HasValidCheckDigit;

    /// <inheritdoc />
    public override string ToString() => Value;
}
