using System.Diagnostics;
using MaritimeVision.Core.Detectors;
using MaritimeVision.Core.Video;
using MaritimeVision.Web;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<VisionOptions>(builder.Configuration.GetSection(VisionOptions.SectionName));
builder.Services.AddSingleton<ContentPathResolver>();
builder.Services.AddSingleton<VisionSessionStore>();
builder.Services.AddSingleton<VisionPipelineFactory>();

var maxUploadBytes = builder.Configuration
    .GetSection(VisionOptions.SectionName)
    .GetValue("MaxUploadMegabytes", 512) * 1024L * 1024L;

builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadBytes);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var visionOptions = app.Services.GetRequiredService<IOptions<VisionOptions>>().Value;
Directory.CreateDirectory(visionOptions.UploadDirectory);

// Estado del modelo, para que la interfaz pueda avisar antes de intentar reproducir.
app.MapGet("/api/status", (VisionPipelineFactory factory) => Results.Ok(new
{
    modelAvailable = factory.IsModelAvailable,
    message = factory.ModelStatus,
}));

app.MapGet("/api/sessions", (VisionSessionStore store) => Results.Ok(
    store.List().Select(session => new
    {
        id = session.Id,
        name = session.DisplayName,
        createdAt = session.CreatedAt,
    })));

// Recepcion de la secuencia por subida de fichero.
app.MapPost("/api/sessions/upload", async (
    HttpRequest request,
    VisionSessionStore store,
    IOptions<VisionOptions> options,
    ILogger<Program> logger) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Se esperaba un formulario multiparte con el campo 'video'." });
    }

    var form = await request.ReadFormAsync();
    var file = form.Files["video"];

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No se recibio ningun fichero de video." });
    }

    // El nombre lo elige el cliente: se conserva solo la extension y se descarta
    // el resto para que no pueda escribir fuera del directorio de subidas.
    var extension = Path.GetExtension(file.FileName);
    if (extension.Length is 0 or > 8 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
    {
        extension = ".mp4";
    }

    var storedPath = Path.Combine(options.Value.UploadDirectory, $"{Guid.NewGuid():n}{extension}");
    await using (var target = File.Create(storedPath))
    {
        await file.CopyToAsync(target);
    }

    logger.LogInformation("Video recibido: {Name} ({Bytes} bytes).", file.FileName, file.Length);

    var session = store.Add(storedPath, Path.GetFileName(file.FileName), isUpload: true);
    return Results.Ok(new { id = session.Id, name = session.DisplayName });
}).DisableAntiforgery();

// Recepcion de la secuencia por URL o camara.
app.MapPost("/api/sessions/source", (
    SourceRequest body,
    VisionSessionStore store,
    IOptions<VisionOptions> options) =>
{
    if (string.IsNullOrWhiteSpace(body.Source))
    {
        return Results.BadRequest(new { error = "Indica una ruta, una URL o un indice de camara." });
    }

    var source = body.Source.Trim();
    var isNetwork = source.Contains("://", StringComparison.Ordinal);

    if (isNetwork && !options.Value.AllowNetworkSources)
    {
        return Results.BadRequest(new
        {
            error = "Los origenes de red estan desactivados. " +
                    "Activa MaritimeVision:AllowNetworkSources solo en un despliegue de confianza.",
        });
    }

    if (!isNetwork && !int.TryParse(source, out _) && !File.Exists(source))
    {
        return Results.BadRequest(new { error = $"No se encontro el fichero '{source}'." });
    }

    var session = store.Add(source, source, isUpload: false);
    return Results.Ok(new { id = session.Id, name = session.DisplayName });
});

// Reproduccion anotada como flujo MJPEG.
app.MapGet("/api/sessions/{id}/stream", async (
    string id,
    HttpContext context,
    VisionSessionStore store,
    VisionPipelineFactory factory,
    IOptions<VisionOptions> options,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var session = store.Find(id);
    if (session is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (!factory.IsModelAvailable)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = factory.ModelStatus }, cancellationToken);
        return;
    }

    // El detector y el lector se construyen antes de fijar las cabeceras: si algo
    // esta mal configurado conviene responder con un error legible, y no con un
    // flujo multiparte que se corta a la primera trama.
    VideoSource video;
    YoloDetector detector;
    try
    {
        video = VideoSource.Open(session.Source);
    }
    catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message }, cancellationToken);
        return;
    }

    try
    {
        detector = factory.CreateDetector();

        // Adelanta los errores de configuracion del modelo a este punto, donde
        // todavia se puede responder con un JSON legible.
        detector.Warmup();
    }
    catch (Exception ex) when (ex is InvalidOperationException
                                   or FileNotFoundException
                                   or NotSupportedException)
    {
        video.Dispose();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message }, cancellationToken);
        return;
    }

    using var _video = video;
    using var _detector = detector;

    context.Response.ContentType = MjpegWriter.ContentType;
    context.Response.Headers.CacheControl = "no-store";

    var pipeline = factory.CreatePipeline(detector, video.Info.Fps);

    var writer = new MjpegWriter(context.Response.Body, options.Value.JpegQuality);
    var targetFrameTime = video.Info.Fps > 0d ? TimeSpan.FromSeconds(1d / video.Info.Fps) : TimeSpan.Zero;
    var clock = Stopwatch.StartNew();
    var frameIndex = 0;

    try
    {
        foreach (var annotated in pipeline.Process(video, cancellationToken))
        {
            await writer.WriteFrameAsync(annotated.Frame, cancellationToken);
            frameIndex++;

            // Si la inferencia va mas rapida que el video se espera, para que la
            // reproduccion mantenga la velocidad real en vez de acelerarse.
            var due = targetFrameTime * frameIndex;
            var ahead = due - clock.Elapsed;
            if (ahead > TimeSpan.Zero)
            {
                await Task.Delay(ahead, cancellationToken);
            }
        }

        await writer.WriteEndAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
        // El navegador cerro la pestana o cambio de video: es lo normal al terminar.
        logger.LogDebug("Reproduccion {SessionId} cancelada por el cliente.", id);
    }

    logger.LogInformation("Reproduccion {SessionId} terminada: {Stats}", id, pipeline.Stats);
});

// Resumen del ultimo analisis, para mostrarlo junto al video.
app.MapGet("/api/sessions/{id}/summary", (
    string id,
    VisionSessionStore store,
    VisionPipelineFactory factory) =>
{
    var session = store.Find(id);
    if (session is null)
    {
        return Results.NotFound();
    }

    using var video = VideoSource.Open(session.Source);
    return Results.Ok(new
    {
        width = video.Info.Width,
        height = video.Info.Height,
        fps = Math.Round(video.Info.Fps, 2),
        frames = video.Info.FrameCount,
        durationSeconds = video.Info.Duration?.TotalSeconds,
        isLive = video.Info.IsLive,
        model = factory.ModelStatus,
    });
});

app.Run();

/// <summary>Cuerpo de la peticion para dar de alta una secuencia por ruta o URL.</summary>
/// <param name="Source">Ruta local, URL <c>rtsp://</c> / <c>http(s)://</c>, o indice de camara.</param>
internal sealed record SourceRequest(string Source);

/// <summary>Expuesto para que las pruebas de integracion puedan arrancar la aplicacion.</summary>
public partial class Program;
