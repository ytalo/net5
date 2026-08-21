using Video2BIC.Core.Ocr;
using Xunit;

namespace Video2BIC.Tests;

public class CtcCharsetTests : IDisposable
{
    private readonly string _workDirectory =
        Path.Combine(Path.GetTempPath(), "video2bic-tests", Guid.NewGuid().ToString("n"));

    public CtcCharsetTests() => Directory.CreateDirectory(_workDirectory);

    [Fact]
    public void TheDefaultAlphabetCoversExactlyWhatACodeCanContain()
    {
        var charset = CtcCharset.Alphanumeric;

        Assert.Equal(36, charset.Characters.Length);
        Assert.Equal(37, charset.ClassCount);
        Assert.All("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789",
            character => Assert.Contains(character, charset.Characters));
    }

    [Fact]
    public void MapsClassIndicesWithTheBlankFirst()
    {
        var charset = new CtcCharset("ABC");

        Assert.Equal(0, charset.BlankIndex);
        Assert.Equal('\0', charset[0]);
        Assert.Equal('A', charset[1]);
        Assert.Equal('C', charset[3]);
        Assert.Equal('\0', charset[4]);
    }

    [Fact]
    public void MapsClassIndicesWithTheBlankLast()
    {
        var charset = new CtcCharset("ABC", BlankPosition.Last);

        Assert.Equal(3, charset.BlankIndex);
        Assert.Equal('A', charset[0]);
        Assert.Equal('C', charset[2]);
        Assert.Equal('\0', charset[3]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAB")]
    public void RefusesAnUnusableAlphabet(string characters)
        => Assert.Throws<ArgumentException>(() => new CtcCharset(characters));

    [Fact]
    public void LoadsAnAlphabetWithOneCharacterPerLine()
    {
        // Es el formato de PaddleOCR.
        var path = Path.Combine(_workDirectory, "por-linea.txt");
        File.WriteAllLines(path, ["A", "B", "C", "1", "2"]);

        Assert.Equal("ABC12", CtcCharset.FromFile(path).Characters);
    }

    [Fact]
    public void LoadsAnAlphabetWrittenInASingleLine()
    {
        var path = Path.Combine(_workDirectory, "una-linea.txt");
        File.WriteAllText(path, "0123456789ABCDEF\n");

        Assert.Equal("0123456789ABCDEF", CtcCharset.FromFile(path).Characters);
    }

    [Fact]
    public void ComplainsAboutAMissingAlphabetFile()
        => Assert.Throws<FileNotFoundException>(
            () => CtcCharset.FromFile(Path.Combine(_workDirectory, "no-existe.txt")));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza best-effort.
        }
    }
}
