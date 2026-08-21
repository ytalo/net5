using Video2BIC.Core.Ocr;
using Xunit;

namespace Video2BIC.Tests;

/// <summary>
/// El decodificador CTC se prueba con tensores sinteticos: no hace falta un modelo
/// entrenado para comprobar que colapsa repeticiones, respeta el blanco y calcula
/// una confianza razonable.
/// </summary>
public class CtcDecoderTests
{
    private static readonly CtcCharset Charset = CtcCharset.Alphanumeric;

    /// <summary>
    /// Construye la salida de un modelo a partir de la clase ganadora de cada
    /// franja, con la probabilidad indicada.
    /// </summary>
    private static float[] BuildScores(IReadOnlyList<int> classesPerStep, float peak = 0.9f)
    {
        var classCount = Charset.ClassCount;
        var scores = new float[classesPerStep.Count * classCount];
        var rest = (1f - peak) / (classCount - 1);

        for (var step = 0; step < classesPerStep.Count; step++)
        {
            for (var index = 0; index < classCount; index++)
            {
                scores[(step * classCount) + index] = index == classesPerStep[step] ? peak : rest;
            }
        }

        return scores;
    }

    private static int ClassOf(char character)
        => Charset.Characters.IndexOf(character, StringComparison.Ordinal) + 1;

    [Fact]
    public void DecodesASimpleSequence()
    {
        var scores = BuildScores([ClassOf('M'), ClassOf('S'), ClassOf('K'), ClassOf('U')]);

        var decoded = CtcDecoder.Decode(scores, Charset.ClassCount, Charset);

        Assert.Equal("MSKU", decoded.Text);
        Assert.Equal(0.9f, decoded.Confidence, 3);
    }

    [Fact]
    public void CollapsesTheRepetitionsOfASingleCharacter()
    {
        // Un caracter ancho ocupa varias franjas; sin colapsar saldria "MMMSSKU".
        var scores = BuildScores(
        [
            ClassOf('M'), ClassOf('M'), ClassOf('M'),
            ClassOf('S'), ClassOf('S'),
            ClassOf('K'), ClassOf('U'),
        ]);

        Assert.Equal("MSKU", CtcDecoder.Decode(scores, Charset.ClassCount, Charset).Text);
    }

    [Fact]
    public void TheBlankSeparatesTwoIdenticalCharacters()
    {
        // Sin el blanco intercalado, "OO" seria indistinguible de una "O" ancha. Es
        // exactamente el caso de un numero de serie con digitos repetidos.
        var scores = BuildScores([ClassOf('O'), Charset.BlankIndex, ClassOf('O')]);

        Assert.Equal("OO", CtcDecoder.Decode(scores, Charset.ClassCount, Charset).Text);
    }

    [Fact]
    public void IgnoresTheBlankFramesAtBothEnds()
    {
        var scores = BuildScores(
        [
            Charset.BlankIndex, Charset.BlankIndex,
            ClassOf('2'), Charset.BlankIndex, ClassOf('2'), ClassOf('G'), ClassOf('1'),
            Charset.BlankIndex, Charset.BlankIndex,
        ]);

        Assert.Equal("22G1", CtcDecoder.Decode(scores, Charset.ClassCount, Charset).Text);
    }

    [Fact]
    public void AveragesTheConfidenceOfTheEmittedCharactersOnly()
    {
        var classCount = Charset.ClassCount;
        var scores = new float[3 * classCount];

        FillStep(scores, classCount, 0, ClassOf('A'), 0.6f);
        FillStep(scores, classCount, 1, Charset.BlankIndex, 0.99f);   // el blanco no cuenta
        FillStep(scores, classCount, 2, ClassOf('B'), 0.8f);

        var decoded = CtcDecoder.Decode(scores, classCount, Charset);

        Assert.Equal("AB", decoded.Text);
        Assert.Equal(0.7f, decoded.Confidence, 3);
    }

    private static void FillStep(float[] scores, int classCount, int step, int winner, float peak)
    {
        var rest = (1f - peak) / (classCount - 1);
        for (var index = 0; index < classCount; index++)
        {
            scores[(step * classCount) + index] = index == winner ? peak : rest;
        }
    }

    [Fact]
    public void NormalisesLogitsWithSoftmaxWhenTheyAreNotProbabilities()
    {
        var classCount = Charset.ClassCount;
        var logits = new float[classCount];
        Array.Fill(logits, -5f);
        logits[ClassOf('7')] = 5f;

        var decoded = CtcDecoder.Decode(logits, classCount, Charset);

        Assert.Equal("7", decoded.Text);

        // Sin softmax la «confianza» seria 5, que no significa nada.
        Assert.InRange(decoded.Confidence, 0.9f, 1f);
    }

    [Fact]
    public void ReadsAnOutputLaidOutAsClassesByTimeSteps()
    {
        var classCount = Charset.ClassCount;
        var rowMajor = BuildScores([ClassOf('4'), ClassOf('5'), ClassOf('R'), ClassOf('1')]);
        var steps = rowMajor.Length / classCount;

        var transposed = new float[rowMajor.Length];
        for (var step = 0; step < steps; step++)
        {
            for (var index = 0; index < classCount; index++)
            {
                transposed[(index * steps) + step] = rowMajor[(step * classCount) + index];
            }
        }

        Assert.Equal("45R1", CtcDecoder.DecodeTransposed(transposed, classCount, Charset).Text);
    }

    [Fact]
    public void RefusesAnAlphabetThatDoesNotMatchTheModel()
    {
        var scores = BuildScores([ClassOf('A')]);
        var wrong = new CtcCharset("ABC");

        var exception = Assert.Throws<ArgumentException>(
            () => CtcDecoder.Decode(scores, Charset.ClassCount, wrong));

        Assert.Contains("alfabeto", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesAnOutputWhoseLengthIsNotAMultipleOfTheClassCount()
        => Assert.Throws<ArgumentException>(
            () => CtcDecoder.Decode(new float[5], Charset.ClassCount, Charset));

    [Fact]
    public void AnEmptySequenceDecodesToNothingWithZeroConfidence()
    {
        var scores = BuildScores([Charset.BlankIndex, Charset.BlankIndex]);
        var decoded = CtcDecoder.Decode(scores, Charset.ClassCount, Charset);

        Assert.Equal(string.Empty, decoded.Text);
        Assert.Equal(0f, decoded.Confidence);
    }
}
