using System.Net;
using OpenCvSharp;
using Photo2BIC.Core.Configuration;
using Photo2BIC.Core.Engines;
using Photo2BIC.Core.Engines.Cloud;
using Photo2BIC.Core.Engines.Local;
using Xunit;

namespace Photo2BIC.Tests;

/// <summary>Construccion del juego de motores.</summary>
public class EngineRegistryTests
{
    [Fact]
    public void BuildsEveryKnownEngineWhenNoneIsRequested()
    {
        var engines = EngineRegistry.Build(new EngineSettings());

        try
        {
            Assert.Equal(EngineRegistry.KnownEngines, engines.Select(engine => engine.Name).ToList());
        }
        finally
        {
            Dispose(engines);
        }
    }

    /// <summary>
    /// Los motores se construyen aunque no esten configurados: «Azure esta ahi,
    /// solo te falta la clave» es informacion util que se pierde si el motor no
    /// llega a existir.
    /// </summary>
    [Fact]
    public void BuildsTheUnconfiguredEnginesTooSoTheyCanExplainWhatIsMissing()
    {
        var engines = EngineRegistry.Build(new EngineSettings(), ["azure"]);

        try
        {
            var azure = Assert.Single(engines);
            Assert.False(azure.IsAvailable(out var reason));
            Assert.Contains("AZURE_VISION_ENDPOINT", reason);
        }
        finally
        {
            Dispose(engines);
        }
    }

    [Theory]
    [InlineData("local", new[] { "tesseract-lineas", "tesseract-disperso", "onnx" })]
    [InlineData("nube", new[] { "azure", "google", "textract", "ocrspace" })]
    [InlineData("tesseract", new[] { "tesseract-lineas", "tesseract-disperso" })]
    public void UnderstandsTheGroupShortcuts(string shortcut, string[] expected)
    {
        var engines = EngineRegistry.Build(new EngineSettings(), [shortcut]);

        try
        {
            Assert.Equal(expected, engines.Select(engine => engine.Name).ToList());
        }
        finally
        {
            Dispose(engines);
        }
    }

    [Fact]
    public void SaysWhichEnginesExistWhenAnUnknownOneIsAskedFor()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => EngineRegistry.Build(new EngineSettings(), ["tesserakt"]));

        Assert.Contains("tesserakt", ex.Message);
        Assert.Contains("tesseract-lineas", ex.Message);
    }

    [Fact]
    public void MarksTheCloudEnginesAsCloudAndTheLocalOnesAsLocal()
    {
        var engines = EngineRegistry.Build(new EngineSettings());

        try
        {
            Assert.Equal(
                OcrEngineKind.Local,
                engines.Single(engine => engine.Name == "tesseract-lineas").Kind);

            Assert.Equal(OcrEngineKind.Cloud, engines.Single(engine => engine.Name == "azure").Kind);
        }
        finally
        {
            Dispose(engines);
        }
    }

    /// <summary>
    /// Los dos ajustes de Tesseract comparten familia: no son dos opiniones
    /// independientes y el consenso tiene que saberlo.
    /// </summary>
    [Fact]
    public void GivesTheTwoTesseractSetupsTheSameFamily()
    {
        var engines = EngineRegistry.Build(new EngineSettings(), ["tesseract"]);

        try
        {
            Assert.Equal(["tesseract", "tesseract"], engines.Select(engine => engine.Family).ToList());
            Assert.Equal(2, engines.Select(engine => engine.Name).Distinct().Count());
        }
        finally
        {
            Dispose(engines);
        }
    }

    /// <summary>Un servicio remoto recibe la fotografia una vez: se factura por llamada.</summary>
    [Fact]
    public void LimitsTheCloudEnginesToASingleVariant()
    {
        var engines = EngineRegistry.Build(new EngineSettings());

        try
        {
            foreach (var engine in engines.Where(engine => engine.Kind == OcrEngineKind.Cloud))
            {
                Assert.Equal(1, engine.MaxVariants);
            }
        }
        finally
        {
            Dispose(engines);
        }
    }

    private static void Dispose(IEnumerable<IPhotoOcrEngine> engines)
    {
        foreach (var engine in engines)
        {
            engine.Dispose();
        }
    }
}

/// <summary>Lectura de la configuracion del entorno.</summary>
public class EngineSettingsTests
{
    [Fact]
    public void ReadsEveryCredentialFromTheEnvironment()
    {
        var environment = new Dictionary<string, string?>
        {
            ["AZURE_VISION_ENDPOINT"] = "https://recurso.cognitiveservices.azure.com",
            ["AZURE_VISION_KEY"] = "clave-azure",
            ["GOOGLE_VISION_API_KEY"] = "clave-google",
            ["AWS_ACCESS_KEY_ID"] = "AKIDEXAMPLE",
            ["AWS_SECRET_ACCESS_KEY"] = "secreta",
            ["AWS_REGION"] = "eu-west-1",
            ["OCRSPACE_API_KEY"] = "helloworld",
        };

        var settings = EngineSettings.FromEnvironment(name =>
            environment.TryGetValue(name, out var value) ? value : null);

        Assert.Equal("https://recurso.cognitiveservices.azure.com", settings.Azure.Endpoint);
        Assert.Equal("clave-google", settings.Google.ApiKey);
        Assert.Equal("eu-west-1", settings.Aws.Region);
        Assert.True(settings.Aws.Credentials.IsComplete);
        Assert.Equal("helloworld", settings.OcrSpace.ApiKey);
    }

    [Fact]
    public void FallsBackToTheOtherAwsRegionVariable()
    {
        var settings = EngineSettings.FromEnvironment(name =>
            name == "AWS_DEFAULT_REGION" ? "ap-southeast-1" : null);

        Assert.Equal("ap-southeast-1", settings.Aws.Region);
    }

    /// <summary>
    /// Una variable definida pero vacia es lo mismo que no tenerla. Pasa a menudo
    /// en los ficheros de entorno de los contenedores.
    /// </summary>
    [Fact]
    public void TreatsAnEmptyVariableAsAbsent()
    {
        var settings = EngineSettings.FromEnvironment(_ => "   ");

        Assert.Equal(string.Empty, settings.Azure.Key);
        Assert.Equal("tesseract", settings.TesseractExecutable);
    }

    [Fact]
    public void UsesTheDefaultsWhenTheEnvironmentIsEmpty()
    {
        var settings = EngineSettings.FromEnvironment(_ => null);

        Assert.Equal("tesseract", settings.TesseractExecutable);
        Assert.Equal("us-east-1", settings.Aws.Region);
        Assert.Equal(string.Empty, settings.OnnxModelPath);
    }
}

/// <summary>Disponibilidad y diagnostico de cada motor.</summary>
public class EngineAvailabilityTests
{
    [Fact]
    public void TheOnnxEngineExplainsThatTheModelIsMissing()
    {
        using var engine = new OnnxPhotoEngine(new Video2BIC.Core.Ocr.OnnxTextRecognizerOptions());

        Assert.False(engine.IsAvailable(out var reason));
        Assert.Contains("--onnx-model", reason);
    }

    [Fact]
    public void TheOnnxEngineSaysWhichModelFileItCouldNotFind()
    {
        using var engine = new OnnxPhotoEngine(new Video2BIC.Core.Ocr.OnnxTextRecognizerOptions
        {
            ModelPath = "/no/existe/modelo.onnx",
        });

        Assert.False(engine.IsAvailable(out var reason));
        Assert.Contains("modelo.onnx", reason);
    }

    [Fact]
    public void TheTesseractEngineExplainsHowToInstallIt()
    {
        using var engine = new TesseractPhotoEngine(
            TesseractLayout.Lines,
            new Video2BIC.Core.Ocr.TesseractOptions { Executable = "tesseract-que-no-existe" });

        Assert.False(engine.IsAvailable(out var reason));
        Assert.Contains("apt install tesseract-ocr", reason);
    }

    [Fact]
    public void TheGoogleEngineAcceptsEitherAnApiKeyOrAnOAuthToken()
    {
        using var withKey = new GoogleVisionEngine(new GoogleVisionOptions { ApiKey = "clave" });
        using var withToken = new GoogleVisionEngine(new GoogleVisionOptions { AccessToken = "token" });
        using var without = new GoogleVisionEngine(new GoogleVisionOptions());

        Assert.True(withKey.IsAvailable(out _));
        Assert.True(withToken.IsAvailable(out _));
        Assert.False(without.IsAvailable(out _));
    }

    [Fact]
    public void TheAzureEngineRejectsAnEndpointThatIsNotAUrl()
    {
        using var engine = new AzureVisionEngine(new AzureVisionOptions
        {
            Endpoint = "mi-recurso",
            Key = "clave",
        });

        Assert.False(engine.IsAvailable(out var reason));
        Assert.Contains("URL absoluta", reason);
    }

    /// <summary>
    /// Un motor sin configurar responde <c>Unavailable</c>, no lanza. Es lo que
    /// permite tenerlos todos registrados sin que ninguno estorbe.
    /// </summary>
    [Fact]
    public async Task AnUnconfiguredEngineAnswersUnavailableInsteadOfThrowing()
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383");
        using var engine = new AzureVisionEngine(new AzureVisionOptions());

        var result = await engine.RecognizeAsync(photo, "original");

        Assert.Equal(OcrEngineStatus.Unavailable, result.Status);
        Assert.Contains("AZURE_VISION_KEY", result.Error);
    }
}

/// <summary>Comportamiento HTTP comun a los motores remotos.</summary>
public class RestOcrEngineTests
{
    private const string EmptyAzureResponse = """{"readResult":{"blocks":[]}}""";

    [Fact]
    public async Task SendsTheImageAndTheKeyToTheServiceEndpoint()
    {
        using var handler = new StubHandler(StubHandler.Reply.Ok(EmptyAzureResponse));
        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new AzureVisionEngine(
            new AzureVisionOptions { Endpoint = "https://recurso.azure.com", Key = "clave-secreta" },
            handler);

        var result = await engine.RecognizeAsync(photo, "original");

        Assert.Equal(OcrEngineStatus.Ok, result.Status);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("imageanalysis:analyze", request.RequestUri!.ToString());
        Assert.Contains("features=read", request.RequestUri.Query);
        Assert.Equal("clave-secreta", request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single());
    }

    /// <summary>Un 429 o un 5xx merecen otro intento; el servicio puede estar saturado.</summary>
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RetriesTheTransientFailures(HttpStatusCode status)
    {
        using var handler = new StubHandler(
            StubHandler.Reply.Error(status),
            StubHandler.Reply.Ok(EmptyAzureResponse));

        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new AzureVisionEngine(
            new AzureVisionOptions { Endpoint = "https://recurso.azure.com", Key = "clave" },
            handler)
        {
            RetryDelay = TimeSpan.FromMilliseconds(1d),
        };

        var result = await engine.RecognizeAsync(photo, "original");

        Assert.Equal(OcrEngineStatus.Ok, result.Status);
        Assert.Equal(2, handler.Calls);
    }

    /// <summary>
    /// Una clave invalida no se arregla insistiendo: reintentarlo solo gasta cuota
    /// y retrasa el aviso.
    /// </summary>
    [Fact]
    public async Task DoesNotRetryAnAuthenticationFailure()
    {
        using var handler = new StubHandler(
            StubHandler.Reply.Error(HttpStatusCode.Unauthorized, """{"error":{"code":"401"}}"""));

        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new AzureVisionEngine(
            new AzureVisionOptions { Endpoint = "https://recurso.azure.com", Key = "mala" },
            handler);

        var result = await engine.RecognizeAsync(photo, "original");

        Assert.Equal(OcrEngineStatus.Failed, result.Status);
        Assert.Equal(1, handler.Calls);
        Assert.Contains("401", result.Error);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfRetries()
    {
        using var handler = new StubHandler(StubHandler.Reply.Error(HttpStatusCode.ServiceUnavailable));
        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new AzureVisionEngine(
            new AzureVisionOptions { Endpoint = "https://recurso.azure.com", Key = "clave" },
            handler)
        {
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromMilliseconds(1d),
        };

        var result = await engine.RecognizeAsync(photo, "original");

        Assert.Equal(OcrEngineStatus.Failed, result.Status);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task ReadsTheCodeThroughTheWholeCloudPath()
    {
        using var handler = new StubHandler(StubHandler.Reply.Ok(
            """
            {"readResult":{"blocks":[{"lines":[{"text":"CSQU 305438 3","words":[
              {"text":"CSQU","boundingPolygon":[{"x":10,"y":10},{"x":90,"y":10},
               {"x":90,"y":50},{"x":10,"y":50}],"confidence":0.97},
              {"text":"305438","boundingPolygon":[{"x":100,"y":10},{"x":220,"y":10},
               {"x":220,"y":50},{"x":100,"y":50}],"confidence":0.95},
              {"text":"3","boundingPolygon":[{"x":230,"y":10},{"x":250,"y":10},
               {"x":250,"y":50},{"x":230,"y":50}],"confidence":0.93}
            ]}]}]}}
            """));

        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new AzureVisionEngine(
            new AzureVisionOptions { Endpoint = "https://recurso.azure.com", Key = "clave" },
            handler);

        var result = await engine.RecognizeAsync(photo, "original");
        var evidence = new Photo2BIC.Core.Fusion.BicConsensus().Extract(result);

        Assert.Equal("CSQU3054383", Assert.Single(evidence).Code.Value);
    }

    /// <summary>
    /// La peticion a Textract va firmada, y la firma cubre el cuerpo: es lo unico
    /// que separa una llamada valida de un 403 sin explicacion.
    /// </summary>
    [Fact]
    public async Task SignsTheTextractRequestWithTheExpectedHeaders()
    {
        using var handler = new StubHandler(StubHandler.Reply.Ok("""{"Blocks":[]}"""));
        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new AwsTextractEngine(
            new AwsTextractOptions
            {
                Credentials = new AwsCredentials("AKIDEXAMPLE", "secreta"),
                Region = "eu-west-1",
            },
            handler,
            () => new DateTimeOffset(2024, 1, 15, 9, 0, 0, TimeSpan.Zero));

        var result = await engine.RecognizeAsync(photo, "original");

        Assert.Equal(OcrEngineStatus.Ok, result.Status);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Textract.DetectDocumentText", request.Headers.GetValues("x-amz-target").Single());
        Assert.Equal("20240115T090000Z", request.Headers.GetValues("x-amz-date").Single());

        var authorization = request.Headers.GetValues("authorization").Single();
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20240115/eu-west-1/textract/", authorization);
        Assert.Contains("Signature=", authorization);
    }

    [Fact]
    public async Task SendsTheImageToTextractAsBase64InsideTheDocument()
    {
        using var handler = new StubHandler(StubHandler.Reply.Ok("""{"Blocks":[]}"""));
        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new AwsTextractEngine(
            new AwsTextractOptions
            {
                Credentials = new AwsCredentials("AKIDEXAMPLE", "secreta"),
                Region = "eu-west-1",
            },
            handler);

        await engine.RecognizeAsync(photo, "original");

        var body = Assert.Single(handler.Bodies);
        Assert.StartsWith("{\"Document\":{\"Bytes\":\"", body);
        Assert.Contains("/9j/", body);
    }

    [Fact]
    public async Task SendsTheApiKeyToGoogleAsAQueryParameter()
    {
        using var handler = new StubHandler(StubHandler.Reply.Ok("""{"responses":[{}]}"""));
        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new GoogleVisionEngine(new GoogleVisionOptions { ApiKey = "clave" }, handler);

        await engine.RecognizeAsync(photo, "original");

        var request = Assert.Single(handler.Requests);
        Assert.Contains("key=clave", request.RequestUri!.Query);
        Assert.Contains("TEXT_DETECTION", Assert.Single(handler.Bodies));
    }

    [Fact]
    public async Task SendsTheOAuthTokenToGoogleAsABearerHeader()
    {
        using var handler = new StubHandler(StubHandler.Reply.Ok("""{"responses":[{}]}"""));
        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new GoogleVisionEngine(new GoogleVisionOptions { AccessToken = "token" }, handler);

        await engine.RecognizeAsync(photo, "original");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("token", request.Headers.Authorization.Parameter);
        Assert.DoesNotContain("key=", request.RequestUri!.Query);
    }

    [Fact]
    public async Task ReportsAServiceErrorAsAFailedResultInsteadOfThrowing()
    {
        using var handler = new StubHandler(StubHandler.Reply.Ok(
            """{"responses":[{"error":{"code":7,"message":"This API method requires billing"}}]}"""));

        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new GoogleVisionEngine(new GoogleVisionOptions { ApiKey = "clave" }, handler);

        var result = await engine.RecognizeAsync(photo, "original");

        Assert.Equal(OcrEngineStatus.Failed, result.Status);
        Assert.Contains("billing", result.Error);
    }

    [Fact]
    public async Task ReportsUnreadableJsonAsAFailedResult()
    {
        using var handler = new StubHandler(StubHandler.Reply.Ok("esto no es json"));
        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new AzureVisionEngine(
            new AzureVisionOptions { Endpoint = "https://recurso.azure.com", Key = "clave" },
            handler);

        var result = await engine.RecognizeAsync(photo, "original");

        Assert.Equal(OcrEngineStatus.Failed, result.Status);
    }

    [Fact]
    public async Task SendsTheOcrSpaceRequestAsAMultipartFormWithTheImage()
    {
        using var handler = new StubHandler(StubHandler.Reply.Ok(
            """{"ParsedResults":[{"ParsedText":"","TextOverlay":{"Lines":[]}}],"IsErroredOnProcessing":false}"""));

        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using var engine = new OcrSpaceEngine(new OcrSpaceOptions { ApiKey = "helloworld" }, handler);

        await engine.RecognizeAsync(photo, "original");

        var request = Assert.Single(handler.Requests);
        Assert.Contains("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("helloworld", body);
        Assert.Contains("isOverlayRequired", body);
    }
}
