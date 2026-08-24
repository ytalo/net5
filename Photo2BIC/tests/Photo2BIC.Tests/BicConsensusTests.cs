using Photo2BIC.Core.Engines;
using Photo2BIC.Core.Fusion;
using Video2BIC.Core.Bic;
using Video2BIC.Core.Text;
using Xunit;

namespace Photo2BIC.Tests;

/// <summary>
/// El consenso entre motores, que es lo que sustituye a la votacion entre
/// fotogramas del video.
/// </summary>
public class BicConsensusTests
{
    // CSQU3054383 y MSKU1234565 son codigos con el digito de control correcto.
    private const string Real = "CSQU3054383";
    private const string Other = "MSKU1234565";

    /// <summary>
    /// La propiedad central: dos motores independientes que coinciden valen mas
    /// que uno solo repitiendose, aunque el segundo caso tenga mas lecturas.
    /// </summary>
    [Fact]
    public void TwoIndependentFamiliesBeatOneFamilyRepeatingItself()
    {
        var consensus = new BicConsensus();

        var independent = consensus.Decide([
            Evidence("azure", "azure", "original", Real, 0.7f),
            Evidence("tesseract-lineas", "tesseract", "otsu", Real, 0.7f),
        ]);

        var repeated = consensus.Decide([
            Evidence("tesseract-lineas", "tesseract", "original", Real, 0.7f),
            Evidence("tesseract-lineas", "tesseract", "otsu", Real, 0.7f),
            Evidence("tesseract-disperso", "tesseract", "clahe", Real, 0.7f),
        ]);

        Assert.True(independent.Best!.Score > repeated.Best!.Score);
    }

    [Fact]
    public void AgreementBetweenFamiliesRaisesTheConfidenceAboveTheBestSingleReading()
    {
        var consensus = new BicConsensus();

        var verdict = consensus.Decide([
            Evidence("azure", "azure", "original", Real, 0.6f),
            Evidence("google", "google", "original", Real, 0.6f),
        ]);

        Assert.True(verdict.Best!.Score > 0.6f);
        Assert.Equal(2, verdict.Best.Families.Count);
    }

    /// <summary>
    /// Anadir evidencia nunca puede bajar la confianza: es la propiedad que hace
    /// utilizable la combinacion.
    /// </summary>
    [Fact]
    public void MoreEvidenceNeverLowersTheConfidence()
    {
        var consensus = new BicConsensus();

        var single = consensus.Decide([Evidence("azure", "azure", "original", Real, 0.8f)]);

        var withMore = consensus.Decide([
            Evidence("azure", "azure", "original", Real, 0.8f),
            Evidence("ocrspace", "ocrspace", "original", Real, 0.2f),
        ]);

        Assert.True(withMore.Best!.Score >= single.Best!.Score);
    }

    /// <summary>
    /// Por mucha evidencia que se acumule, debajo sigue habiendo un OCR sobre una
    /// fotografia. Un 100 % invitaria a saltarse la revision justo en los casos en
    /// los que todos los motores se equivocan a la vez.
    /// </summary>
    [Fact]
    public void NeverReachesFullCertainty()
    {
        var consensus = new BicConsensus();

        var verdict = consensus.Decide(Enumerable
            .Range(0, 20)
            .Select(index => Evidence($"motor{index}", $"familia{index}", "original", Real, 0.99f))
            .ToList());

        Assert.True(verdict.Best!.Score < 1f);
        Assert.Equal(consensus.Options.MaxScore, verdict.Best.Score, 4);
    }

    [Fact]
    public void RejectsACeilingBelowTheConfirmationThreshold()
    {
        var options = new ConsensusOptions { MinScore = 0.8f, MaxScore = 0.5f };

        Assert.Throws<InvalidOperationException>(() => new BicConsensus(options));
    }

    [Fact]
    public void ChoosesTheCodeWithMoreSupportNotTheOneWithTheLoudestSingleReading()
    {
        var consensus = new BicConsensus();

        var verdict = consensus.Decide([
            Evidence("ocrspace", "ocrspace", "original", Other, 0.85f),
            Evidence("azure", "azure", "original", Real, 0.6f),
            Evidence("google", "google", "original", Real, 0.6f),
            Evidence("tesseract-lineas", "tesseract", "otsu", Real, 0.55f),
        ]);

        Assert.Equal(Real, verdict.Best!.Code.Value);
        Assert.Equal(Other, Assert.Single(verdict.Alternatives).Code.Value);
    }

    /// <summary>
    /// Dos codigos empatados no se resuelven a la brava: se marcan como ambiguos
    /// para que lo mire alguien.
    /// </summary>
    [Fact]
    public void FlagsTwoTiedCodesAsAmbiguousAndDoesNotConfirmThem()
    {
        var consensus = new BicConsensus();

        var verdict = consensus.Decide([
            Evidence("azure", "azure", "original", Real, 0.8f),
            Evidence("google", "google", "original", Other, 0.8f),
        ]);

        Assert.True(verdict.Ambiguous);
        Assert.False(verdict.Confirmed);
    }

    [Fact]
    public void DoesNotFlagAsAmbiguousWhenOneCodeIsClearlyAhead()
    {
        var consensus = new BicConsensus();

        var verdict = consensus.Decide([
            Evidence("azure", "azure", "original", Real, 0.9f),
            Evidence("google", "google", "original", Real, 0.9f),
            Evidence("ocrspace", "ocrspace", "original", Other, 0.2f),
        ]);

        Assert.False(verdict.Ambiguous);
        Assert.True(verdict.Confirmed);
    }

    [Fact]
    public void DoesNotConfirmWhenFewerFamiliesThanRequiredAgree()
    {
        var options = new ConsensusOptions { MinFamilies = 2 };
        var consensus = new BicConsensus(options);

        var verdict = consensus.Decide([
            Evidence("tesseract-lineas", "tesseract", "original", Real, 0.95f),
            Evidence("tesseract-disperso", "tesseract", "otsu", Real, 0.95f),
        ]);

        Assert.Equal(Real, verdict.Best!.Code.Value);
        Assert.False(verdict.Confirmed);
    }

    [Fact]
    public void DoesNotConfirmBelowTheMinimumScore()
    {
        var consensus = new BicConsensus(new ConsensusOptions { MinScore = 0.9f });

        var verdict = consensus.Decide([Evidence("azure", "azure", "original", Real, 0.5f)]);

        Assert.True(verdict.HasCode);
        Assert.False(verdict.Confirmed);
    }

    /// <summary>
    /// Un peso de cero silencia a una familia entera. Es la via para descartar un
    /// motor que se sabe malo sobre unas fotografias concretas sin desinstalarlo.
    /// </summary>
    [Fact]
    public void AFamilyWeightedZeroContributesNothing()
    {
        var options = new ConsensusOptions();
        options.FamilyWeights["ocrspace"] = 0f;

        var verdict = new BicConsensus(options).Decide([
            Evidence("azure", "azure", "original", Real, 0.5f),
            Evidence("ocrspace", "ocrspace", "original", Real, 0.9f),
        ]);

        Assert.Equal(0.5f, verdict.Best!.Score, 3);
    }

    [Fact]
    public void ReportsWhichEnginesAndFamiliesSupportTheChosenCode()
    {
        var verdict = new BicConsensus().Decide([
            Evidence("tesseract-disperso", "tesseract", "original", Real, 0.6f),
            Evidence("tesseract-lineas", "tesseract", "otsu", Real, 0.7f),
            Evidence("azure", "azure", "original", Real, 0.8f),
        ]);

        Assert.Equal(["azure", "tesseract-disperso", "tesseract-lineas"], verdict.Best!.Engines);
        Assert.Equal(["azure", "tesseract"], verdict.Best.Families);
        Assert.Equal(3, verdict.Best.Readings);
    }

    [Fact]
    public void RemembersWhetherAnyEngineReadItWithoutCorrections()
    {
        var verdict = new BicConsensus().Decide([
            Evidence("azure", "azure", "original", Real, 0.6f, corrections: 2),
            Evidence("google", "google", "original", Real, 0.5f, corrections: 0),
        ]);

        Assert.True(verdict.Best!.HasLiteralReading);
        Assert.Equal(0, verdict.Best.MinCorrections);
    }

    [Fact]
    public void ReturnsNoVerdictWhenNoEngineProposedAnything()
    {
        var verdict = new BicConsensus().Decide([]);

        Assert.False(verdict.HasCode);
        Assert.False(verdict.Confirmed);
        Assert.Empty(verdict.Alternatives);
    }

    /// <summary>
    /// El texto que no lleva codigo no debe producir ninguno. Un rotulo de carga
    /// maxima esta lleno de digitos y es el falso positivo mas facil de cometer.
    /// </summary>
    [Fact]
    public void ExtractsNothingFromAPlateWithoutACode()
    {
        var consensus = new BicConsensus();

        var evidence = consensus.Extract(Result("azure", "azure", "MAX GROSS 30480 KG TARE 2200 KG"));

        Assert.Empty(evidence);
    }

    [Fact]
    public void RebuildsACodeSplitAcrossSeveralLines()
    {
        var consensus = new BicConsensus();

        var result = new OcrEngineResult(
            "azure", "azure", "original",
            [
                new TextFragment("CSQU", 0.9f, 0, 0, 100, 40),
                new TextFragment("305438", 0.9f, 0, 50, 140, 40),
                new TextFragment("3", 0.9f, 150, 50, 30, 40),
            ],
            TimeSpan.Zero);

        var evidence = consensus.Extract(result);

        Assert.Equal(Real, Assert.Single(evidence).Code.Value);
    }

    [Fact]
    public void IgnoresTheEnginesThatFailedOrWereNotAvailable()
    {
        var consensus = new BicConsensus();

        var evidence = consensus.Extract([
            new OcrEngineResult("azure", "azure", "-", [], TimeSpan.Zero,
                OcrEngineStatus.Unavailable, "falta la clave"),
            new OcrEngineResult("google", "google", "original",
                [new TextFragment(Real, 0.9f)], TimeSpan.Zero, OcrEngineStatus.Failed, "503"),
        ]);

        Assert.Empty(evidence);
    }

    private static BicEvidence Evidence(
        string engine, string family, string variant, string code, float confidence, int corrections = 0)
    {
        Assert.True(BicCode.TryParse(code, out var parsed));
        return new BicEvidence(engine, family, variant, new BicCandidate(parsed, confidence, corrections, code));
    }

    private static OcrEngineResult Result(string engine, string family, string text)
        => new(engine, family, "original", [new TextFragment(text, 0.9f)], TimeSpan.Zero);
}
