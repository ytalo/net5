using OpenCvSharp;
using Photo2BIC.Core.Engines;
using Photo2BIC.Core.Pipeline;
using Xunit;

namespace Photo2BIC.Tests;

/// <summary>
/// El pipeline completo, con motores simulados: no depende de que haya un OCR
/// instalado ni de credenciales.
/// </summary>
public class PhotoBicReaderTests
{
    private const string Real = "CSQU3054383";
    private const string Wrong = "CSQU3054380";

    [Fact]
    public async Task ReadsTheCodeThatTheEnginesAgreeOn()
    {
        using var photo = SyntheticPhoto.Door(Real);

        var result = await ReadAsync(photo,
            new ScriptedEngine("azure", "azure", [Real]) { MaxVariants = 1 },
            new ScriptedEngine("google", "google", [Real]) { MaxVariants = 1 });

        Assert.Equal(Real, result.Verdict.Best!.Code.Value);
        Assert.True(result.IsConfirmed);
        Assert.Equal(["azure", "google"], result.SupportingEngines);
    }

    /// <summary>
    /// La razon de ser de tener varios motores: que uno se caiga no puede llevarse
    /// por delante el analisis.
    /// </summary>
    [Fact]
    public async Task AFailingEngineDoesNotBringDownTheAnalysis()
    {
        using var photo = SyntheticPhoto.Door(Real);

        var result = await ReadAsync(photo,
            new ScriptedEngine("azure", "azure", []) { Throws = true, MaxVariants = 1 },
            new ScriptedEngine("google", "google", [Real]) { MaxVariants = 1 });

        Assert.Equal(Real, result.Verdict.Best!.Code.Value);
        Assert.Equal("azure", Assert.Single(result.Failures).Engine);
    }

    /// <summary>
    /// Un motor sin configurar se anota una vez, no una por variante: el informe
    /// tiene que decir «falta la clave», no repetirlo seis veces.
    /// </summary>
    [Fact]
    public async Task AnUnavailableEngineIsReportedOnceAndIsNeverCalled()
    {
        using var photo = SyntheticPhoto.Door(Real);

        var absent = new ScriptedEngine("azure", "azure", [Real]) { UnavailableReason = "falta la clave" };

        var result = await ReadAsync(photo, absent, new ScriptedEngine("google", "google", [Real]));

        var unavailable = Assert.Single(result.Unavailable);
        Assert.Equal("azure", unavailable.Engine);
        Assert.Equal("falta la clave", unavailable.Error);
        Assert.Equal(0, absent.Calls);
    }

    [Fact]
    public async Task RunsALocalEngineOverSeveralPreprocessingVariants()
    {
        using var photo = SyntheticPhoto.Door(Real);

        var engine = new ScriptedEngine("tesseract-lineas", "tesseract", [Real]);
        await ReadAsync(photo, engine);

        Assert.Equal(3, engine.Calls);
        Assert.Equal("original", engine.Variants[0]);
        Assert.Equal(3, engine.Variants.Distinct().Count());
    }

    /// <summary>
    /// A un servicio remoto se le manda la fotografia una vez: se factura por
    /// llamada y ya hace su propio preprocesado.
    /// </summary>
    [Fact]
    public async Task SendsASingleVariantToACloudEngine()
    {
        using var photo = SyntheticPhoto.Door(Real);

        var engine = new ScriptedEngine("azure", "azure", [Real])
        {
            Kind = OcrEngineKind.Cloud,
            MaxVariants = 1,
        };

        await ReadAsync(photo, engine);

        Assert.Equal(1, engine.Calls);
        Assert.Equal("original", Assert.Single(engine.Variants));
    }

    [Fact]
    public async Task AlsoReadsTheSizeAndTypeCodeWhenItIsThere()
    {
        using var photo = SyntheticPhoto.Door(Real);

        var result = await ReadAsync(photo,
            new ScriptedEngine("azure", "azure", [$"{Real} 22G1"]) { MaxVariants = 1 });

        Assert.Equal("22G1", result.SizeType?.Value);
        Assert.Contains("6058", result.SizeType!.LengthDescription);
    }

    /// <summary>
    /// Un codigo cuyo digito de control no cuadra no es un codigo, por mucho que
    /// tenga la forma correcta y el motor se fie de el.
    /// </summary>
    [Fact]
    public async Task RejectsACodeWhoseCheckDigitDoesNotAddUp()
    {
        using var photo = SyntheticPhoto.Door(Real);

        var result = await ReadAsync(photo,
            new ScriptedEngine("azure", "azure", [Wrong]) { MaxVariants = 1 });

        Assert.False(result.HasCode);
    }

    [Fact]
    public async Task ReportsWhichEnginesDidNotSupportTheChosenCode()
    {
        using var photo = SyntheticPhoto.Door(Real);

        var result = await ReadAsync(photo,
            new ScriptedEngine("azure", "azure", [Real]) { MaxVariants = 1 },
            new ScriptedEngine("google", "google", [Real]) { MaxVariants = 1 },
            new ScriptedEngine("ocrspace", "ocrspace", ["MAX GROSS 30480 KG"]) { MaxVariants = 1 });

        Assert.Equal(["ocrspace"], result.DissentingEngines);
    }

    [Fact]
    public async Task ReturnsAnEmptyResultForAnEmptyImage()
    {
        using var photo = new Mat();

        var result = await ReadAsync(photo, new ScriptedEngine("azure", "azure", [Real]));

        Assert.False(result.HasCode);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task WritesAJsonReportWithTheEvidenceOfEachEngine()
    {
        using var photo = SyntheticPhoto.Door(Real);

        var result = await ReadAsync(photo,
            new ScriptedEngine("azure", "azure", [Real]) { MaxVariants = 1 },
            new ScriptedEngine("google", "google", [Real]) { MaxVariants = 1 });

        var json = result.ToJson();

        Assert.Contains("\"confirmado\": true", json);
        Assert.Contains(Real, json);
        Assert.Contains("\"evidencias\"", json);
        Assert.Contains("\"azure\"", json);

        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(Real, document.RootElement.GetProperty("codigo").GetProperty("valor").GetString());
    }

    [Fact]
    public void RefusesToBeBuiltWithoutEngines()
        => Assert.Throws<ArgumentException>(() => new PhotoBicReader([]));

    [Fact]
    public void ListsWhichEnginesAreReadyAndWhyTheOthersAreNot()
    {
        using var reader = new PhotoBicReader([
            new ScriptedEngine("azure", "azure", []) { UnavailableReason = "falta la clave" },
            new ScriptedEngine("google", "google", []),
        ]);

        var diagnosis = reader.Diagnose();

        Assert.False(diagnosis[0].Available);
        Assert.Equal("falta la clave", diagnosis[0].Reason);
        Assert.True(diagnosis[1].Available);
    }

    private static async Task<PhotoReadResult> ReadAsync(Mat photo, params IPhotoOcrEngine[] engines)
    {
        var options = new Photo2BicOptions { MaxVariantsPerEngine = 3 };
        using var reader = new PhotoBicReader(engines, options);

        return await reader.ReadAsync(photo, "prueba.jpg", CancellationToken.None);
    }
}
