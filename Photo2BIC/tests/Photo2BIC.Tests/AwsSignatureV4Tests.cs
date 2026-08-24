using System.Text;
using Photo2BIC.Core.Engines.Cloud;
using Xunit;

namespace Photo2BIC.Tests;

/// <summary>
/// La firma se contrasta con los vectores publicados por AWS.
/// </summary>
/// <remarks>
/// Es el unico sitio del proyecto donde un error no se manifiesta como un
/// resultado peor sino como un 403 seco y sin pista de por que. Los vectores
/// oficiales son la unica forma de saber que esta bien sin credenciales reales.
/// </remarks>
public class AwsSignatureV4Tests
{
    // Vector de la documentacion de AWS: clave de firma para el 30 de agosto de
    // 2015, region us-east-1, servicio iam.
    private const string ExampleSecret = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY";

    [Fact]
    public void DerivesTheDocumentedSigningKey()
    {
        var key = AwsSignatureV4.SigningKey(ExampleSecret, "20150830", "us-east-1", "iam");

        Assert.Equal(
            "c4afb1cc5771d871763a393e44b703571b55cc28424d1a5e86da6ed3c154a4b9",
            Convert.ToHexStringLower(key));
    }

    [Fact]
    public void SignsTheGetVanillaVectorOfTheOfficialSuite()
    {
        var headers = AwsSignatureV4.Sign(
            "GET",
            new Uri("https://example.amazonaws.com/"),
            new Dictionary<string, string>(),
            [],
            new AwsCredentials("AKIDEXAMPLE", ExampleSecret),
            "us-east-1",
            "service",
            new DateTimeOffset(2015, 8, 30, 12, 36, 0, TimeSpan.Zero));

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20150830/us-east-1/service/aws4_request, " +
            "SignedHeaders=host;x-amz-date, " +
            "Signature=5fa00fa31553b73ebf1942676e86291e8372ff2a2260956d9b8aae1d763fbf31",
            headers["authorization"]);
    }

    [Fact]
    public void PutsTheHostAndTheDateInTheSignedHeaders()
    {
        var headers = Sign(new Uri("https://textract.eu-west-1.amazonaws.com/"));

        Assert.Equal("textract.eu-west-1.amazonaws.com", headers["host"]);
        Assert.Equal("20240115T090000Z", headers["x-amz-date"]);
        Assert.Contains("SignedHeaders=content-type;host;x-amz-date;x-amz-target", headers["authorization"]);
    }

    [Fact]
    public void IncludesThePortInTheHostWhenItIsNotTheDefaultOne()
    {
        var headers = Sign(new Uri("https://localhost:4566/"));

        Assert.Equal("localhost:4566", headers["host"]);
    }

    [Fact]
    public void SignsTheSessionTokenWhenTheCredentialsAreTemporary()
    {
        var headers = AwsSignatureV4.Sign(
            "POST",
            new Uri("https://textract.eu-west-1.amazonaws.com/"),
            new Dictionary<string, string>(),
            [],
            new AwsCredentials("AKIDEXAMPLE", ExampleSecret, "token-temporal"),
            "eu-west-1",
            "textract",
            DateTimeOffset.UnixEpoch);

        Assert.Equal("token-temporal", headers["x-amz-security-token"]);
        Assert.Contains("x-amz-security-token", headers["authorization"]);
    }

    [Fact]
    public void ChangesTheSignatureWhenTheBodyChanges()
    {
        var first = AwsSignatureV4.Sign(
            "POST",
            new Uri("https://textract.eu-west-1.amazonaws.com/"),
            new Dictionary<string, string>(),
            Encoding.UTF8.GetBytes("{\"Document\":{}}"),
            new AwsCredentials("AKIDEXAMPLE", ExampleSecret),
            "eu-west-1",
            "textract",
            DateTimeOffset.UnixEpoch);

        var second = AwsSignatureV4.Sign(
            "POST",
            new Uri("https://textract.eu-west-1.amazonaws.com/"),
            new Dictionary<string, string>(),
            Encoding.UTF8.GetBytes("{\"Document\":{\"Bytes\":\"AA==\"}}"),
            new AwsCredentials("AKIDEXAMPLE", ExampleSecret),
            "eu-west-1",
            "textract",
            DateTimeOffset.UnixEpoch);

        Assert.NotEqual(first["authorization"], second["authorization"]);
    }

    /// <summary>
    /// El orden de la consulta lo fija la firma, no el llamante: dos URLs con los
    /// mismos parametros en distinto orden tienen que firmar igual.
    /// </summary>
    [Fact]
    public void SortsTheQueryParametersBeforeSigning()
    {
        var first = Sign(new Uri("https://textract.eu-west-1.amazonaws.com/?b=2&a=1"));
        var second = Sign(new Uri("https://textract.eu-west-1.amazonaws.com/?a=1&b=2"));

        Assert.Equal(first["authorization"], second["authorization"]);
    }

    /// <summary>
    /// Los espacios interiores de una cabecera se colapsan a uno antes de firmar,
    /// como exige la norma.
    /// </summary>
    [Fact]
    public void CollapsesTheWhitespaceInTheHeaderValues()
    {
        var spaced = AwsSignatureV4.Sign(
            "POST",
            new Uri("https://textract.eu-west-1.amazonaws.com/"),
            new Dictionary<string, string> { ["x-amz-target"] = "  Textract.Detect   DocumentText  " },
            [],
            new AwsCredentials("AKIDEXAMPLE", ExampleSecret),
            "eu-west-1",
            "textract",
            DateTimeOffset.UnixEpoch);

        Assert.Equal("Textract.Detect DocumentText", spaced["x-amz-target"]);
    }

    [Fact]
    public void HashesAnEmptyBodyWithTheKnownSha256()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            AwsSignatureV4.HashPayload([]));
    }

    private static IReadOnlyDictionary<string, string> Sign(Uri uri)
        => AwsSignatureV4.Sign(
            "POST",
            uri,
            new Dictionary<string, string>
            {
                ["content-type"] = "application/x-amz-json-1.1",
                ["x-amz-target"] = "Textract.DetectDocumentText",
            },
            [],
            new AwsCredentials("AKIDEXAMPLE", ExampleSecret),
            "eu-west-1",
            "textract",
            new DateTimeOffset(2024, 1, 15, 9, 0, 0, TimeSpan.Zero));
}
