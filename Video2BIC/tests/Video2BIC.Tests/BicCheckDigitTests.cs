using Video2BIC.Core.Bic;
using Xunit;

namespace Video2BIC.Tests;

/// <summary>
/// El digito de control es el filtro que sostiene toda la aplicacion: si esta mal
/// calculado, o se descartan lecturas buenas o se aceptan codigos inventados.
/// </summary>
public class BicCheckDigitTests
{
    [Theory]
    // El ejemplo canonico de la propia norma ISO 6346.
    [InlineData("CSQU305438", 3)]
    [InlineData("MSKU123456", 5)]
    [InlineData("TGHU765432", 0)]
    [InlineData("MAEU123456", 7)]
    [InlineData("HLXU876543", 0)]
    [InlineData("GESU555555", 7)]
    [InlineData("TCNU123456", 5)]
    [InlineData("CMAU000000", 5)]
    [InlineData("BICU000000", 6)]
    public void ComputesTheCheckDigitOfKnownCodes(string prefix, int expected)
        => Assert.Equal(expected, BicCheckDigit.Compute(prefix));

    [Fact]
    public void ARemainderOfTenIsPublishedAsZero()
    {
        // La norma representa el resto 10 como 0. Rechazar estos codigos supondria
        // dar por invalidos contenedores que existen y circulan.
        Assert.Equal(0, BicCheckDigit.Compute("MSKU000008"));
        Assert.True(BicCheckDigit.IsValid("MSKU0000080"));
    }

    [Theory]
    [InlineData('0', 0)]
    [InlineData('9', 9)]
    [InlineData('A', 10)]
    // Los multiplos de 11 se saltan: despues de A=10 viene B=12, no 11.
    [InlineData('B', 12)]
    [InlineData('K', 21)]
    [InlineData('L', 23)]
    [InlineData('U', 32)]
    [InlineData('V', 34)]
    [InlineData('Z', 38)]
    public void AssignsTheIso6346ValueToEachCharacter(char character, int expected)
        => Assert.Equal(expected, BicCheckDigit.ValueOf(character));

    [Fact]
    public void NoLetterValueIsAMultipleOfEleven()
    {
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            Assert.NotEqual(0, BicCheckDigit.ValueOf(letter) % 11);
        }
    }

    [Fact]
    public void LetterValuesAreConsecutiveOnceTheMultiplesOfElevenAreSkipped()
    {
        var expected = 10;
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            while (expected % 11 == 0)
            {
                expected++;
            }

            Assert.Equal(expected, BicCheckDigit.ValueOf(letter));
            expected++;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("CSQU30543")]
    [InlineData("CSQU3054383")]
    [InlineData("CSQU30543-")]
    [InlineData("csqu305438")]
    public void RefusesPrefixesThatAreNotTenUppercaseAlphanumerics(string prefix)
    {
        Assert.False(BicCheckDigit.TryCompute(prefix, out _));
        Assert.Throws<ArgumentException>(() => BicCheckDigit.Compute(prefix));
    }

    [Fact]
    public void AlmostEverySingleCharacterMistakeBreaksTheCheckDigit()
    {
        // Es la propiedad que hace util al digito de control frente al OCR. Una
        // sustitucion sobrevive solo si el caracter nuevo vale lo mismo que el
        // viejo modulo 11: como los pesos son potencias de 2, que son invertibles
        // modulo 11, la posicion no influye.
        const string valid = "CSQU3054383";
        var survivors = 0;
        var mutations = 0;

        for (var position = 0; position < 10; position++)
        {
            foreach (var replacement in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            {
                if (replacement == valid[position])
                {
                    continue;
                }

                var mutated = valid.ToCharArray();
                mutated[position] = replacement;
                mutations++;

                if (BicCheckDigit.IsValid(mutated))
                {
                    survivors++;
                }
            }
        }

        Assert.Equal(350, mutations);

        // 23 de 350: el 93,4 % de los errores de un solo caracter se detectan sin
        // ninguna otra evidencia.
        Assert.Equal(23, survivors);
    }

    [Theory]
    [InlineData("CSQU3054383", true)]
    [InlineData("CSQU3054384", false)]
    [InlineData("CSQU305438", false)]
    [InlineData("MSKU1234565", true)]
    public void ValidatesCompleteCodes(string code, bool expected)
        => Assert.Equal(expected, BicCheckDigit.IsValid(code));
}
