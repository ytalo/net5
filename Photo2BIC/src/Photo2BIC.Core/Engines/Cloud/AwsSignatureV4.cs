using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Photo2BIC.Core.Engines.Cloud;

/// <summary>Credenciales de AWS.</summary>
/// <param name="AccessKeyId">Identificador de la clave.</param>
/// <param name="SecretAccessKey">Clave secreta.</param>
/// <param name="SessionToken">Token de sesion, si las credenciales son temporales.</param>
public readonly record struct AwsCredentials(string AccessKeyId, string SecretAccessKey, string? SessionToken = null)
{
    /// <summary><c>true</c> si hay identificador y secreto.</summary>
    public bool IsComplete
        => !string.IsNullOrWhiteSpace(AccessKeyId) && !string.IsNullOrWhiteSpace(SecretAccessKey);
}

/// <summary>
/// Firma Signature Version 4, la que exigen los servicios de AWS.
/// </summary>
/// <remarks>
/// <para>
/// Se implementa aqui, en unas cien lineas, en vez de arrastrar el SDK de AWS.
/// El SDK trae su cadena de resolucion de credenciales, su propia capa de
/// reintentos y varias decenas de megabytes de dependencias, y de todo eso este
/// proyecto solo necesita firmar una peticion contra un extremo.
/// </para>
/// <para>
/// El procedimiento tiene cuatro pasos: construir la peticion canonica, derivar
/// de ella la cadena a firmar, derivar una clave de firma encadenando HMAC sobre
/// fecha, region y servicio, y firmar. Cada paso esta separado y es publico
/// dentro del ensamblado precisamente para poder contrastarlos con los vectores
/// de prueba que publica AWS.
/// </para>
/// </remarks>
internal static class AwsSignatureV4
{
    /// <summary>Algoritmo, tal y como aparece en la cabecera <c>Authorization</c>.</summary>
    public const string Algorithm = "AWS4-HMAC-SHA256";

    /// <summary>Formato de <c>x-amz-date</c>.</summary>
    public const string TimestampFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>Formato de la fecha del ambito de credenciales.</summary>
    public const string DateFormat = "yyyyMMdd";

    /// <summary>
    /// Firma una peticion y devuelve las cabeceras que hay que anadirle.
    /// </summary>
    /// <param name="method">Metodo HTTP en mayusculas.</param>
    /// <param name="uri">URL completa de la peticion.</param>
    /// <param name="headers">
    /// Cabeceras a firmar. Debe incluir todas las que se vayan a enviar y que
    /// participen en la firma; <c>host</c>, <c>x-amz-date</c> y el token de sesion
    /// se anaden aqui si faltan.
    /// </param>
    /// <param name="payload">Cuerpo de la peticion.</param>
    /// <param name="credentials">Credenciales.</param>
    /// <param name="region">Region, por ejemplo <c>eu-west-1</c>.</param>
    /// <param name="service">Servicio, por ejemplo <c>textract</c>.</param>
    /// <param name="timestamp">Momento de la firma, en UTC.</param>
    /// <returns>
    /// Las cabeceras definitivas, incluidas <c>Authorization</c> y las que se hayan
    /// tenido que anadir.
    /// </returns>
    public static IReadOnlyDictionary<string, string> Sign(
        string method,
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        byte[] payload,
        AwsCredentials credentials,
        string region,
        string service,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(payload);

        var amzDate = timestamp.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var dateStamp = timestamp.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture);
        var payloadHash = Hex(SHA256.HashData(payload));

        var signed = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in headers)
        {
            signed[name.ToLowerInvariant()] = Collapse(value);
        }

        signed["host"] = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        signed["x-amz-date"] = amzDate;

        if (!string.IsNullOrWhiteSpace(credentials.SessionToken))
        {
            signed["x-amz-security-token"] = credentials.SessionToken;
        }

        var signedHeaders = string.Join(';', signed.Keys);
        var canonicalRequest = CanonicalRequest(method, uri, signed, signedHeaders, payloadHash);
        var scope = $"{dateStamp}/{region}/{service}/aws4_request";
        var stringToSign = StringToSign(amzDate, scope, canonicalRequest);
        var signingKey = SigningKey(credentials.SecretAccessKey, dateStamp, region, service);
        var signature = Hex(HmacSha256(signingKey, Encoding.UTF8.GetBytes(stringToSign)));

        var result = new Dictionary<string, string>(signed, StringComparer.Ordinal)
        {
            ["authorization"] =
                $"{Algorithm} Credential={credentials.AccessKeyId}/{scope}, " +
                $"SignedHeaders={signedHeaders}, Signature={signature}",
        };

        return result;
    }

    /// <summary>
    /// Peticion canonica: metodo, ruta, consulta, cabeceras firmadas y huella del
    /// cuerpo, cada parte normalizada de una unica manera posible.
    /// </summary>
    public static string CanonicalRequest(
        string method,
        Uri uri,
        IReadOnlyDictionary<string, string> signedHeaderValues,
        string signedHeaders,
        string payloadHash)
    {
        var builder = new StringBuilder();

        builder.Append(method.ToUpperInvariant()).Append('\n');
        builder.Append(CanonicalPath(uri)).Append('\n');
        builder.Append(CanonicalQuery(uri)).Append('\n');

        foreach (var (name, value) in signedHeaderValues)
        {
            builder.Append(name).Append(':').Append(value).Append('\n');
        }

        builder.Append('\n');
        builder.Append(signedHeaders).Append('\n');
        builder.Append(payloadHash);

        return builder.ToString();
    }

    /// <summary>Cadena a firmar, derivada de la peticion canonica.</summary>
    public static string StringToSign(string amzDate, string credentialScope, string canonicalRequest)
        => $"{Algorithm}\n{amzDate}\n{credentialScope}\n{Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";

    /// <summary>
    /// Clave de firma, encadenando HMAC sobre fecha, region, servicio y el
    /// terminador del esquema.
    /// </summary>
    /// <remarks>
    /// El encadenamiento es lo que hace que una clave de firma solo sirva para un
    /// dia, una region y un servicio concretos: si se filtra, el dano esta acotado
    /// y la clave secreta original no queda expuesta.
    /// </remarks>
    public static byte[] SigningKey(string secretAccessKey, string dateStamp, string region, string service)
    {
        var key = Encoding.UTF8.GetBytes($"AWS4{secretAccessKey}");
        var date = HmacSha256(key, Encoding.UTF8.GetBytes(dateStamp));
        var regional = HmacSha256(date, Encoding.UTF8.GetBytes(region));
        var scoped = HmacSha256(regional, Encoding.UTF8.GetBytes(service));

        return HmacSha256(scoped, Encoding.UTF8.GetBytes("aws4_request"));
    }

    /// <summary>Huella hexadecimal en minusculas del cuerpo de la peticion.</summary>
    public static string HashPayload(byte[] payload) => Hex(SHA256.HashData(payload));

    /// <summary>
    /// Ruta canonica: cada segmento codificado segun RFC 3986, y <c>/</c> si esta
    /// vacia.
    /// </summary>
    private static string CanonicalPath(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return "/";
        }

        // AbsolutePath ya viene con los caracteres reservados codificados, pero AWS
        // exige su propia codificacion: se descodifica y se vuelve a codificar
        // segmento a segmento para que el resultado sea el mismo se escriba como se
        // escriba la URL.
        var segments = path.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            segments[index] = Encode(Uri.UnescapeDataString(segments[index]));
        }

        return string.Join('/', segments);
    }

    /// <summary>Consulta canonica: parametros ordenados por nombre y codificados.</summary>
    private static string CanonicalQuery(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        if (query.Length == 0)
        {
            return string.Empty;
        }

        var parameters = new List<(string Name, string Value)>();

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            var name = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];

            parameters.Add((
                Encode(Uri.UnescapeDataString(name)),
                Encode(Uri.UnescapeDataString(value))));
        }

        // El orden es por nombre codificado y, a igual nombre, por valor.
        parameters.Sort((first, second) =>
        {
            var byName = string.CompareOrdinal(first.Name, second.Name);
            return byName != 0 ? byName : string.CompareOrdinal(first.Value, second.Value);
        });

        return string.Join('&', parameters.Select(parameter => $"{parameter.Name}={parameter.Value}"));
    }

    /// <summary>
    /// Codificacion porcentual de AWS: todo salvo los caracteres no reservados de
    /// RFC 3986, en hexadecimal mayusculo.
    /// </summary>
    private static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length * 2);

        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var character = (char)b;

            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '~')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Normaliza el valor de una cabecera: sin espacios en los extremos y con los
    /// interiores colapsados a uno solo.
    /// </summary>
    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var space = false;

        foreach (var character in value.Trim())
        {
            if (character is ' ' or '\t')
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(character);
        }

        return builder.ToString();
    }

    private static byte[] HmacSha256(byte[] key, byte[] data) => HMACSHA256.HashData(key, data);

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
