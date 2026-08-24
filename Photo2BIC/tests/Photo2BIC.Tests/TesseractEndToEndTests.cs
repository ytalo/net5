using Photo2BIC.Core.Engines;
using Photo2BIC.Core.Engines.Local;
using Photo2BIC.Core.Fusion;
using Photo2BIC.Core.Imaging;
using Photo2BIC.Core.Pipeline;
using Video2BIC.Core.Ocr;
using Xunit;

namespace Photo2BIC.Tests;

/// <summary>
/// Marca una prueba que necesita el binario de Tesseract instalado. Sin el, la
/// prueba se salta con una explicacion en vez de fallar: la suite tiene que pasar
/// en una maquina limpia.
/// </summary>
public sealed class RequiresTesseractFactAttribute : FactAttribute
{
    public RequiresTesseractFactAttribute()
    {
        if (!TesseractTextRecognizer.IsAvailable())
        {
            Skip = "Tesseract no esta instalado (apt install tesseract-ocr).";
        }
    }
}

/// <summary>
/// El recorrido completo con un OCR de verdad: fotografia, variantes, motores y
/// consenso.
/// </summary>
/// <remarks>
/// Las demas pruebas usan motores simulados para poder fijar lo que lee cada uno.
/// Estas comprueban lo que aquellas no pueden: que las piezas encajan de verdad
/// contra un reconocedor real.
/// </remarks>
public class TesseractEndToEndTests : IDisposable
{
    private const string Code = "CSQU3054383";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "photo2bic-e2e", Guid.NewGuid().ToString("n"));

    public TesseractEndToEndTests() => Directory.CreateDirectory(_directory);

    [RequiresTesseractFact]
    public async Task ReadsTheCodeOfASyntheticContainerDoor()
    {
        var path = SyntheticDoor.Write(Path.Combine(_directory, "puerta.jpg"), Code);

        using var reader = new PhotoBicReader(Engines());
        var result = await reader.ReadAsync(path);

        Assert.Equal(Code, result.Verdict.Best?.Code.Value);
    }

    [RequiresTesseractFact]
    public async Task AlsoReadsTheSizeAndTypeCodePaintedBelowIt()
    {
        var path = SyntheticDoor.Write(Path.Combine(_directory, "puerta.jpg"), Code, "22G1");

        using var reader = new PhotoBicReader(Engines());
        var result = await reader.ReadAsync(path);

        Assert.Equal("22G1", result.SizeType?.Value);
    }

    /// <summary>
    /// El codigo va pintado en blanco sobre panel oscuro tan a menudo como al
    /// reves, y hay que leerlo igual en los dos casos.
    /// </summary>
    [RequiresTesseractFact]
    public async Task ReadsItWithEitherPolarity()
    {
        using var reader = new PhotoBicReader(Engines(), ownsEngines: true);

        foreach (var lightOnDark in new[] { true, false })
        {
            var path = SyntheticDoor.Write(
                Path.Combine(_directory, $"puerta-{lightOnDark}.jpg"), Code, "22G1",
                lightTextOnDarkPanel: lightOnDark);

            var result = await reader.ReadAsync(path);

            Assert.Equal(Code, result.Verdict.Best?.Code.Value);
        }
    }

    /// <summary>
    /// La razon de generar variantes: sobre una fotografia sola, unas funcionan y
    /// otras no, y no se sabe cual de antemano.
    /// </summary>
    [RequiresTesseractFact]
    public async Task DifferentPreprocessingVariantsSucceedOnTheSamePhotograph()
    {
        var path = SyntheticDoor.Write(Path.Combine(_directory, "puerta.jpg"), Code);

        using var reader = new PhotoBicReader(Engines());
        var result = await reader.ReadAsync(path);

        var variants = result.Evidence
            .Where(evidence => evidence.Code.Value == Code)
            .Select(evidence => evidence.Variant)
            .Distinct()
            .ToList();

        Assert.NotEmpty(variants);
        Assert.All(result.Results, engine => Assert.NotEqual(OcrEngineStatus.Unavailable, engine.Status));
    }

    /// <summary>
    /// Una escena sin ningun codigo no debe producir ninguno. Es la prueba que de
    /// verdad protege: un falso positivo manda un contenedor al muelle equivocado.
    /// </summary>
    [RequiresTesseractFact]
    public async Task DoesNotInventACodeOnAPanelWithoutOne()
    {
        var path = SyntheticDoor.Write(Path.Combine(_directory, "vacia.jpg"), string.Empty, "MAX GROSS 30480 KG");

        using var reader = new PhotoBicReader(Engines());
        var result = await reader.ReadAsync(path);

        Assert.False(result.HasCode);
    }

    /// <summary>
    /// Los dos ajustes de Tesseract comparten familia, asi que aunque coincidan no
    /// llegan a la confianza de dos motores independientes.
    /// </summary>
    [RequiresTesseractFact]
    public async Task AgreementWithinTheSameFamilyDoesNotReachIndependentConfidence()
    {
        var path = SyntheticDoor.Write(Path.Combine(_directory, "puerta.jpg"), Code);

        using var reader = new PhotoBicReader(Engines());
        var result = await reader.ReadAsync(path);

        var single = Assert.Single(result.Verdict.Best!.Families);
        Assert.Equal("tesseract", single);

        // Las mismas lecturas repartidas en familias distintas dan mas confianza.
        var independent = new BicConsensus().Decide(result.Evidence
            .Select((evidence, index) => evidence with { Family = $"familia{index}" })
            .ToList());

        Assert.True(independent.Best!.Score > result.Verdict.Best.Score);
    }

    [RequiresTesseractFact]
    public async Task ReadsAWholeFolderInOneGo()
    {
        SyntheticDoor.Write(Path.Combine(_directory, "lote", "a.jpg"), Code);
        SyntheticDoor.Write(Path.Combine(_directory, "lote", "b.jpg"), "MSKU1234565");

        using var reader = new PhotoBicReader(Engines());

        var codes = new List<string?>();
        foreach (var photo in PhotoLoader.Enumerate(Path.Combine(_directory, "lote")))
        {
            var result = await reader.ReadAsync(photo);
            codes.Add(result.Verdict.Best?.Code.Value);
        }

        Assert.Equal([Code, "MSKU1234565"], codes);
    }

    private static IReadOnlyList<IPhotoOcrEngine> Engines() =>
    [
        new TesseractPhotoEngine(TesseractLayout.Lines),
        new TesseractPhotoEngine(TesseractLayout.Sparse),
    ];

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
