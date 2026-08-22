using Video2BIC.Core.Bic;
using Video2BIC.Core.Text;
using Xunit;

namespace Video2BIC.Tests;

public class SizeTypeCodeTests
{
    [Fact]
    public void DecodesTheMostCommonCodeOfAll()
    {
        // 22G1: el contenedor de 20 pies estandar.
        Assert.True(SizeTypeCode.TryParse("22G1", out var code));

        Assert.Contains("20 pies", code.LengthDescription);
        Assert.Contains("8 pies 6 pulgadas", code.HeightDescription);
        Assert.Contains("uso general", code.TypeDescription);
        Assert.Equal('2', code.LengthCode);
        Assert.Equal("G1", code.TypeCode);
    }

    [Theory]
    [InlineData("42G1", "40 pies")]
    // 45R1 es el frigorifico "high cube": mide 40 pies de largo y 9'6" de alto. El
    // 5 es la altura, no la longitud, y confundirlos es el error clasico al leer
    // estos codigos.
    [InlineData("45R1", "40 pies")]
    [InlineData("L5G1", "45 pies")]
    [InlineData("22U6", "20 pies")]
    [InlineData("22T6", "20 pies")]
    [InlineData("M2G1", "48 pies")]
    public void DecodesTheLengthOfOtherCommonCodes(string value, string expectedLength)
    {
        Assert.True(SizeTypeCode.TryParse(value, out var code));
        Assert.Contains(expectedLength, code.LengthDescription);
    }

    [Fact]
    public void TheSecondCharacterIsHeightNotLength()
    {
        Assert.True(SizeTypeCode.TryParse("45R1", out var highCube));
        Assert.Contains("40 pies", highCube.LengthDescription);
        Assert.Contains("9 pies 6 pulgadas", highCube.HeightDescription);
    }

    [Theory]
    [InlineData("45R1", "frigorifico")]
    [InlineData("22U1", "techo abierto")]
    [InlineData("22P1", "plataforma")]
    [InlineData("22T5", "gases")]
    [InlineData("22V0", "ventila")]
    [InlineData("22B0", "granel")]
    public void DecodesTheContainerType(string value, string expectedType)
    {
        Assert.True(SizeTypeCode.TryParse(value, out var code));
        Assert.Contains(expectedType, code.TypeDescription);
    }

    [Theory]
    [InlineData("22G")]         // faltan caracteres
    [InlineData("22G11")]       // sobran
    [InlineData("92G1")]        // longitud fuera de la tabla
    [InlineData("21G1")]        // altura fuera de la tabla
    [InlineData("22Q1")]        // grupo de tipo inexistente
    [InlineData("22G/")]        // caracter no alfanumerico
    [InlineData("")]
    [InlineData(null)]
    public void RejectsCodesOutsideTheStandardTables(string? value)
        => Assert.False(SizeTypeCode.TryParse(value, out _));

    [Fact]
    public void FindsTheSizeCodeAmongTheRecognisedText()
    {
        IReadOnlyList<TextFragment> fragments =
        [
            new("MSKU1234565", 0.9f, 0, 0, 200, 20),
            new("22G1", 0.8f, 0, 30, 80, 20),
        ];

        Assert.True(SizeTypeCode.TryFind(fragments, "MSKU1234565", out var code));
        Assert.Equal("22G1", code.Value);
    }

    [Fact]
    public void DoesNotMistakeAPieceOfTheBicCodeForASizeCode()
    {
        // Sin excluir el tramo del codigo BIC, "U123" de MSKU1234565 pasaria el
        // filtro de tablas y se publicaria como tamano.
        IReadOnlyList<TextFragment> fragments = [new("MSKU1234565", 0.9f, 0, 0, 200, 20)];

        Assert.False(SizeTypeCode.TryFind(fragments, "MSKU1234565", out _));
    }

    [Fact]
    public void SummarisesTheCodeInOneLine()
    {
        Assert.True(SizeTypeCode.TryParse("22G1", out var code));
        Assert.StartsWith("22G1:", code.ToDisplayString(), StringComparison.Ordinal);
    }
}
