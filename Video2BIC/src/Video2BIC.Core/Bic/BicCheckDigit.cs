namespace Video2BIC.Core.Bic;

/// <summary>
/// Digito de control de la norma ISO 6346, el que cierra el codigo BIC de un
/// contenedor.
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza que convierte el OCR en algo fiable: de las 11 posiciones del
/// codigo, la ultima esta determinada por las otras diez. Una lectura con una
/// letra mal reconocida casi nunca cuadra, asi que el digito de control descarta
/// la inmensa mayoria de los falsos positivos sin necesidad de ninguna otra
/// evidencia.
/// </para>
/// <para>
/// El calculo asigna un valor numerico a cada caracter, lo pondera por
/// <c>2^posicion</c> y toma el resto de dividir la suma entre 11. Las letras
/// saltan los multiplos de 11 (11, 22 y 33 no se usan) para que ninguna letra
/// sea equivalente a otra modulo 11.
/// </para>
/// </remarks>
public static class BicCheckDigit
{
    /// <summary>Numero de caracteres que participan en el calculo.</summary>
    public const int PrefixLength = 10;

    /// <summary>Longitud total del codigo, prefijo mas digito de control.</summary>
    public const int CodeLength = 11;

    // Valor ISO 6346 de cada letra: A=10 y a partir de ahi consecutivos, saltando
    // los multiplos de 11. El indice es la letra menos 'A'.
    private static readonly int[] LetterValues =
    [
        10, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24,
        25, 26, 27, 28, 29, 30, 31, 32, 34, 35, 36, 37, 38,
    ];

    /// <summary>
    /// Valor ISO 6346 de un caracter alfanumerico en mayusculas.
    /// </summary>
    /// <param name="character">Caracter <c>0-9</c> o <c>A-Z</c>.</param>
    /// <returns>El valor, o -1 si el caracter no es alfanumerico.</returns>
    public static int ValueOf(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'A' and <= 'Z' => LetterValues[character - 'A'],
        _ => -1,
    };

    /// <summary>
    /// Calcula el digito de control de los diez primeros caracteres del codigo.
    /// </summary>
    /// <param name="prefix">
    /// Codigo de propietario (3 letras), identificador de categoria (1 letra) y
    /// numero de serie (6 digitos), en mayusculas y sin separadores.
    /// </param>
    /// <returns>El digito de control, en el rango [0, 9].</returns>
    /// <exception cref="ArgumentException">
    /// Si el prefijo no tiene exactamente diez caracteres alfanumericos.
    /// </exception>
    public static int Compute(ReadOnlySpan<char> prefix)
    {
        if (!TryCompute(prefix, out var digit))
        {
            throw new ArgumentException(
                $"El prefijo debe tener {PrefixLength} caracteres alfanumericos en mayusculas.", nameof(prefix));
        }

        return digit;
    }

    /// <summary>
    /// Version sin excepciones de <see cref="Compute"/>: el camino habitual del
    /// OCR, donde recibir basura es lo normal y no un caso excepcional.
    /// </summary>
    public static bool TryCompute(ReadOnlySpan<char> prefix, out int checkDigit)
    {
        checkDigit = -1;

        if (prefix.Length != PrefixLength)
        {
            return false;
        }

        var sum = 0;
        var weight = 1;

        for (var index = 0; index < PrefixLength; index++)
        {
            var value = ValueOf(prefix[index]);
            if (value < 0)
            {
                return false;
            }

            sum += value * weight;
            weight <<= 1;
        }

        // Un resto de 10 se representa como 0. La norma desaconseja que un
        // propietario emita numeros de serie que caigan ahi, pero circulan, y no
        // aceptarlos supondria rechazar contenedores validos.
        checkDigit = sum % 11 % 10;
        return true;
    }

    /// <summary>
    /// Comprueba si un codigo completo de once caracteres cuadra con su propio
    /// digito de control.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<char> code)
        => code.Length == CodeLength
           && TryCompute(code[..PrefixLength], out var expected)
           && code[PrefixLength] - '0' == expected;
}
