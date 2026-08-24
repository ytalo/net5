using System.Diagnostics;
using OpenCvSharp;
using Photo2BIC.Core.Engines;
using Photo2BIC.Core.Fusion;
using Photo2BIC.Core.Imaging;
using Video2BIC.Core.Bic;

namespace Photo2BIC.Core.Pipeline;

/// <summary>
/// Lee el codigo BIC de la fotografia de un contenedor.
/// </summary>
/// <remarks>
/// <para>
/// Encadena las cuatro etapas: preparar la fotografia, generar variantes de
/// preprocesado, lanzar sobre ellas todos los motores disponibles y combinar lo
/// que dijeron.
/// </para>
/// <para>
/// Los motores se ejecutan en paralelo. Lo que se gana es sobre todo solapar la
/// latencia de los servicios remotos -tres llamadas de dos segundos en serie son
/// seis segundos, y a la vez son dos-, no repartir CPU: cada motor local
/// serializa internamente sus propias variantes.
/// </para>
/// <para>
/// Nunca se lanza por culpa de un motor. Que falte Tesseract, que caduque una
/// clave de Azure o que Textract devuelva un 403 se registra como un resultado
/// mas y el analisis continua con lo que quede. Tener varios motores solo sirve
/// si la ausencia de uno no se lleva por delante a los demas.
/// </para>
/// </remarks>
public sealed class PhotoBicReader : IDisposable
{
    private readonly IReadOnlyList<IPhotoOcrEngine> _engines;
    private readonly Photo2BicOptions _options;
    private readonly PhotoVariantFactory _variants;
    private readonly BicConsensus _consensus;
    private readonly bool _ownsEngines;

    private bool _disposed;

    /// <param name="engines">Motores a usar. Al menos uno.</param>
    /// <param name="options">Configuracion; <c>null</c> usa la de por defecto.</param>
    /// <param name="ownsEngines">
    /// Liberar los motores al liberar el lector. <c>false</c> cuando los gestiona
    /// el llamante, por ejemplo al procesar una carpeta entera con los mismos.
    /// </param>
    public PhotoBicReader(
        IReadOnlyList<IPhotoOcrEngine> engines,
        Photo2BicOptions? options = null,
        bool ownsEngines = true)
    {
        ArgumentNullException.ThrowIfNull(engines);

        if (engines.Count == 0)
        {
            throw new ArgumentException("Hace falta al menos un motor de reconocimiento.", nameof(engines));
        }

        _engines = engines;
        _ownsEngines = ownsEngines;
        _options = options ?? new Photo2BicOptions();
        _options.Validate();

        _variants = new PhotoVariantFactory(_options.Variants);
        _consensus = new BicConsensus(_options.Consensus, new BicCodeParser(_options.Parser));
    }

    /// <summary>Motores en uso.</summary>
    public IReadOnlyList<IPhotoOcrEngine> Engines => _engines;

    /// <summary>Configuracion en uso.</summary>
    public Photo2BicOptions Options => _options;

    /// <summary>Motores disponibles en esta maquina, con el motivo de los que no lo estan.</summary>
    public IReadOnlyList<(IPhotoOcrEngine Engine, bool Available, string Reason)> Diagnose()
        => _engines.Select(engine => (engine, engine.IsAvailable(out var reason), reason)).ToList();

    /// <summary>Lee el codigo de una fotografia del disco.</summary>
    /// <param name="path">Ruta del fichero.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <exception cref="FileNotFoundException">Si el fichero no existe.</exception>
    /// <exception cref="InvalidOperationException">Si el fichero no es una imagen legible.</exception>
    public async Task<PhotoReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var photo = PhotoLoader.Load(path, _options.MaxWidth);
        return await ReadAsync(photo, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lee el codigo de una imagen ya cargada.
    /// </summary>
    /// <param name="photo">Fotografia BGR o en escala de grises; no se modifica.</param>
    /// <param name="source">Etiqueta que identifica la fotografia en el informe.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    public async Task<PhotoReadResult> ReadAsync(
        Mat photo, string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var stopwatch = Stopwatch.StartNew();

        if (photo.Empty())
        {
            return new PhotoReadResult(source, 0, 0, BicVerdict.None, null, [], [], stopwatch.Elapsed);
        }

        using var timeout = CreateTimeout(cancellationToken);
        var token = timeout?.Token ?? cancellationToken;

        using var variants = _variants.Create(photo);
        var results = await RunEnginesAsync(variants, token).ConfigureAwait(false);

        var evidence = _consensus.Extract(results);
        var verdict = _consensus.Decide(evidence);
        var sizeType = FindSizeType(results, verdict);

        return new PhotoReadResult(
            source,
            photo.Width,
            photo.Height,
            verdict,
            sizeType,
            results,
            evidence,
            stopwatch.Elapsed);
    }

    /// <summary>
    /// Lanza cada motor sobre las variantes que le correspondan.
    /// </summary>
    private async Task<IReadOnlyList<OcrEngineResult>> RunEnginesAsync(
        PhotoVariantSet variants, CancellationToken cancellationToken)
    {
        if (variants.Count == 0)
        {
            return [];
        }

        using var slots = new SemaphoreSlim(_options.MaxConcurrency);

        var tasks = _engines
            .Select(engine => RunEngineAsync(engine, variants, slots, cancellationToken))
            .ToList();

        var byEngine = await Task.WhenAll(tasks).ConfigureAwait(false);

        return byEngine.SelectMany(results => results).ToList();
    }

    private async Task<IReadOnlyList<OcrEngineResult>> RunEngineAsync(
        IPhotoOcrEngine engine,
        PhotoVariantSet variants,
        SemaphoreSlim slots,
        CancellationToken cancellationToken)
    {
        // Un motor no configurado se anota una sola vez, no una por variante: el
        // informe tiene que decir «falta la clave de Azure», no repetirlo seis veces.
        if (!engine.IsAvailable(out var reason))
        {
            return [OcrEngineResult.Unavailable(engine, "-", reason)];
        }

        var budget = Math.Min(engine.MaxVariants, _options.MaxVariantsPerEngine);
        var results = new List<OcrEngineResult>();

        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var variant in variants.Take(budget))
            {
                cancellationToken.ThrowIfCancellationRequested();

                results.Add(await engine
                    .RecognizeAsync(variant.Image, variant.Name, cancellationToken)
                    .ConfigureAwait(false));
            }
        }
        catch (OperationCanceledException)
        {
            // Se conserva lo que diera tiempo a leer: media evidencia es mejor que
            // ninguna, y el consenso ya sabe trabajar con lo que haya.
            results.Add(new OcrEngineResult(
                engine.Name, engine.Family, "-", [], TimeSpan.Zero,
                OcrEngineStatus.Skipped, "se agoto el tiempo del analisis"));
        }
        finally
        {
            slots.Release();
        }

        return results;
    }

    /// <summary>
    /// Busca el codigo de tamano y tipo entre todo lo que se leyo.
    /// </summary>
    /// <remarks>
    /// Va justo debajo del BIC y no tiene digito de control, asi que no se puede
    /// validar igual: se acepta el primero que encaje en la tabla de la norma,
    /// empezando por lo que leyeron los motores que si acertaron el codigo. Es una
    /// heuristica, y por eso el resultado se marca aparte y no forma parte del
    /// veredicto.
    /// </remarks>
    private static SizeTypeCode? FindSizeType(IReadOnlyList<OcrEngineResult> results, BicVerdict verdict)
    {
        var supporting = new HashSet<string>(verdict.Best?.Engines ?? [], StringComparer.OrdinalIgnoreCase);

        var ordered = results
            .Where(result => result.Status == OcrEngineStatus.Ok && result.Fragments.Count > 0)
            .OrderByDescending(result => supporting.Contains(result.Engine))
            .ThenByDescending(result => result.MeanConfidence);

        foreach (var result in ordered)
        {
            if (SizeTypeCode.TryFind(result.Fragments, verdict.Best?.Code.Value, out var code))
            {
                return code;
            }
        }

        return null;
    }

    private CancellationTokenSource? CreateTimeout(CancellationToken cancellationToken)
    {
        if (_options.Timeout <= TimeSpan.Zero)
        {
            return null;
        }

        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(_options.Timeout);
        return source;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_ownsEngines)
        {
            return;
        }

        foreach (var engine in _engines)
        {
            engine.Dispose();
        }
    }
}
