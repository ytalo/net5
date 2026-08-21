using Video2BIC.Core.Bic;
using Xunit;

namespace Video2BIC.Tests;

public class BicCodeTests
{
    [Fact]
    public void ParsesTheFourPartsOfACode()
    {
        Assert.True(BicCode.TryParse("CSQU3054383", out var code));

        Assert.Equal("CSQ", code.OwnerCode);
        Assert.Equal('U', code.CategoryIdentifier);
        Assert.Equal("305438", code.SerialNumber);
        Assert.Equal(3, code.CheckDigit);
        Assert.Equal(BicCategory.FreightContainer, code.Category);
        Assert.True(code.HasValidCheckDigit);
        Assert.Equal("CSQU3054383", code.Value);
        Assert.Equal("CSQU 305438 3", code.ToDisplayString());
    }

    [Theory]
    [InlineData("CSQU 305438 3")]
    [InlineData("CSQU-305438-3")]
    [InlineData("csqu3054383")]
    [InlineData(" CSQU3054383 ")]
    public void AcceptsTheSeparatorsAndTheCaseUsedInShippingPaperwork(string text)
    {
        Assert.True(BicCode.TryParse(text, out var code));
        Assert.Equal("CSQU3054383", code.Value);
    }

    [Theory]
    [InlineData("CS0U3054383")]     // digito donde va una letra
    [InlineData("CSQX3054383")]     // categoria que no es U, J ni Z
    [InlineData("CSQU30543A3")]     // letra donde va un digito
    [InlineData("CSQU305438")]      // falta el digito de control
    [InlineData("CSQU30543833")]    // sobra un caracter
    [InlineData("CSQU3054/83")]     // caracter no alfanumerico
    [InlineData("")]
    [InlineData(null)]
    public void RejectsAnythingWithoutTheShapeOfACode(string? text)
        => Assert.False(BicCode.TryParse(text, out _));

    [Theory]
    [InlineData('U', BicCategory.FreightContainer)]
    [InlineData('J', BicCategory.DetachableEquipment)]
    [InlineData('Z', BicCategory.TrailerOrChassis)]
    public void RecognisesTheThreeEquipmentCategories(char identifier, BicCategory expected)
    {
        var code = BicCode.Create("ABC", identifier, "123456");
        Assert.Equal(expected, code.Category);
        Assert.True(code.HasValidCheckDigit);
    }

    [Fact]
    public void ParsesACodeWhoseCheckDigitDoesNotMatch()
    {
        // Interpretar y validar son cosas distintas: para diagnosticar por que no se
        // lee un contenedor hace falta poder representar la lectura fallida.
        Assert.True(BicCode.TryParse("CSQU3054384", out var code));
        Assert.False(code.HasValidCheckDigit);
        Assert.Equal(3, code.ExpectedCheckDigit);
        Assert.False(BicCode.IsValid("CSQU3054384"));
    }

    [Fact]
    public void CreateComputesTheCheckDigitWhenItIsNotGiven()
    {
        var code = BicCode.Create("MSK", 'U', "123456");
        Assert.Equal("MSKU1234565", code.Value);
        Assert.True(code.HasValidCheckDigit);
    }

    [Theory]
    [InlineData("MS", 'U', "123456")]
    [InlineData("MSKU", 'U', "123456")]
    [InlineData("M5K", 'U', "123456")]
    [InlineData("MSK", 'X', "123456")]
    [InlineData("MSK", 'U', "12345")]
    [InlineData("MSK", 'U', "12345A")]
    public void CreateRejectsMalformedParts(string owner, char category, string serial)
        => Assert.Throws<ArgumentException>(() => BicCode.Create(owner, category, serial));

    [Fact]
    public void TwoCodesWithTheSameValueAreEqual()
    {
        Assert.True(BicCode.TryParse("CSQU3054383", out var first));
        Assert.True(BicCode.TryParse("CSQU 305438 3", out var second));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
