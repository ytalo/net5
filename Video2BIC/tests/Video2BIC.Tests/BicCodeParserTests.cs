using Video2BIC.Core.Bic;
using Video2BIC.Core.Text;
using Xunit;

namespace Video2BIC.Tests;

/// <summary>
/// El analizador es lo que separa «el OCR devolvio algo» de «este contenedor es el
/// CSQU3054383». Las pruebas cubren las tres situaciones reales: texto limpio,
/// texto sucio y texto que no contiene ningun codigo.
/// </summary>
public class BicCodeParserTests
{
    private readonly BicCodeParser _parser = new();

    [Fact]
    public void FindsACleanCodeInsideSurroundingText()
    {
        var candidates = _parser.Parse("MAERSK CSQU3054383 22G1 MAX GROSS 30480KG");

        var best = Assert.Single(candidates, candidate => candidate.Code.Value == "CSQU3054383");
        Assert.True(best.IsLiteral);
        Assert.True(best.HasValidCheckDigit);
        Assert.Equal(1f, best.Confidence, 3);
    }

    [Fact]
    public void RebuildsACodeSplitAcrossLines()
    {
        // Es como aparece en la puerta de un contenedor: propietario y categoria en
        // una linea, numero de serie en otra y digito de control en un recuadro.
        IReadOnlyList<TextFragment> fragments =
        [
            new("MSKU", 0.9f, 0, 0, 80, 20),
            new("123456", 0.9f, 0, 30, 100, 20),
            new("5", 0.8f, 110, 30, 20, 20),
        ];

        var candidates = _parser.Parse(fragments);

        Assert.Contains(candidates, candidate => candidate.Code.Value == "MSKU1234565");
    }

    [Fact]
    public void RepairsTheGlyphsAnOcrConfusesWhenTheCheckDigitAgrees()
    {
        // 0 por O y 5 por S son las dos confusiones mas frecuentes sobre chapa.
        var candidates = _parser.Parse("C5QU3O54383", 0.8f);

        var repaired = Assert.Single(candidates, candidate => candidate.Code.Value == "CSQU3054383");
        Assert.Equal(2, repaired.Corrections);
        Assert.True(repaired.HasValidCheckDigit);

        // La confianza baja respecto a la del OCR porque hubo que corregir.
        Assert.True(repaired.Confidence < 0.8f);
    }

    [Fact]
    public void RepairsALetterReadAsADigitInTheOwnerCode()
    {
        var candidates = _parser.Parse("M5KU1234565");

        var repaired = Assert.Single(candidates, candidate => candidate.Code.Value == "MSKU1234565");
        Assert.Equal(1, repaired.Corrections);
    }

    [Fact]
    public void RepairsTheCategoryIdentifierReadAsAV()
    {
        var candidates = _parser.Parse("MSKV1234565");
        Assert.Contains(candidates, candidate => candidate.Code.Value == "MSKU1234565");
    }

    [Fact]
    public void DoesNotInventCodesOutOfUnrelatedText()
    {
        foreach (var text in new[]
                 {
                     "MAX GROSS 30480 KG TARE 3700 KG",
                     "HAZARDOUS CLASS 3 UN1203 FLAMMABLE",
                     "0123456789012345678901234567890",
                     "AAAAAAAAAAAAAAAAAAAA",
                 })
        {
            Assert.Empty(_parser.Parse(text));
        }
    }

    [Fact]
    public void RefusesToRepairBeyondTheAllowedBudget()
    {
        // Tres caracteres cambiados (5 por S, O por 0 y B por 8): con el presupuesto
        // por defecto de dos, la lectura se descarta en vez de forzar un codigo.
        const string degraded = "C5QU3O543B3";

        var strict = new BicCodeParser(new BicParserOptions { MaxCorrections = 2 });
        Assert.DoesNotContain(strict.Parse(degraded), candidate => candidate.Code.Value == "CSQU3054383");

        var lenient = new BicCodeParser(new BicParserOptions { MaxCorrections = 3 });
        Assert.Contains(lenient.Parse(degraded), candidate => candidate.Code.Value == "CSQU3054383");
    }

    [Fact]
    public void RejectsAWellFormedCodeWhoseCheckDigitDoesNotAgree()
    {
        // La forma es correcta y aun asi no se propone: el digito de control es lo
        // unico que distingue una lectura buena de una plausible.
        Assert.Empty(_parser.Parse("CSQU3054384"));
    }

    [Fact]
    public void PermissiveModeReportsTheMalformedReadingForDiagnosis()
    {
        var permissive = new BicCodeParser(new BicParserOptions { RequireValidCheckDigit = false });

        var candidates = permissive.Parse("CSQU3054384");

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate => Assert.True(candidate.Confidence < 0.5f));
    }

    [Fact]
    public void PrefersTheLiteralReadingOverARepairedOne()
    {
        // Dos codigos validos en el mismo texto: el que no necesito correcciones
        // tiene que quedar por delante.
        var candidates = _parser.Parse("CSQU3054383 M5KU1234565");

        Assert.Equal("CSQU3054383", candidates[0].Code.Value);
        Assert.True(candidates[0].IsLiteral);
    }

    [Fact]
    public void WeightsTheConfidenceOfAJoinByFragmentLength()
    {
        IReadOnlyList<TextFragment> fragments =
        [
            new("MSKU123456", 1.0f, 0, 0, 200, 20),
            new("5", 0.2f, 0, 30, 20, 20),
        ];

        var candidate = Assert.Single(_parser.Parse(fragments));

        // (1.0 * 10 + 0.2 * 1) / 11 = 0.927
        Assert.Equal(0.927f, candidate.Confidence, 2);
    }

    [Fact]
    public void ReturnsAtMostTheConfiguredNumberOfCandidates()
    {
        var parser = new BicCodeParser(new BicParserOptions
        {
            MaxCandidates = 2,
            RequireValidCheckDigit = false,
        });

        Assert.True(parser.Parse("CSQU3054384").Count <= 2);
    }

    [Fact]
    public void HandlesTextShorterThanACode()
    {
        Assert.Empty(_parser.Parse("MSKU"));
        Assert.Empty(_parser.Parse(string.Empty));
        Assert.Empty(_parser.Parse((string?)null));
    }

    [Fact]
    public void RejectsAnImpossibleConfiguration()
        => Assert.Throws<InvalidOperationException>(
            () => new BicCodeParser(new BicParserOptions { MaxCorrections = -1 }));
}
