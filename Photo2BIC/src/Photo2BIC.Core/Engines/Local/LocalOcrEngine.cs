using System.Diagnostics;
using OpenCvSharp;
using Video2BIC.Core.Imaging;
using Video2BIC.Core.Ocr;
using Video2BIC.Core.TextRegions;

namespace Photo2BIC.Core.Engines.Local;

/// <summary>
/// Base de los motores que se ejecutan en la propia maquina.
/// </summary>
/// <remarks>
/// <para>
/// El reconocedor se construye la primera vez que se usa, no en el constructor.
/// Es deliberado: cargar un modelo ONNX de 40 MB o comprobar que existe cuesta, y
/// la aplicacion instancia todos los motores conocidos para poder listarlos aunque
/// el usuario solo vaya a usar uno. Ademas permite que <see cref="IsAvailable"/>
/// responda «falta el modelo» en vez de lanzar una excepcion al arrancar.
/// </para>
/// <para>
/// El acceso al reconocedor esta serializado porque ni una sesion de ONNX Runtime
/// ni el estado interno de estas clases son seguros para uso concurrente. No es
/// una perdida real: el paralelismo que importa es entre motores distintos -sobre
/// todo entre los locales y la latencia de los remotos-, no entre dos variantes
/// del mismo motor compitiendo por los mismos nucleos.
/// </para>
/// </remarks>
public abstract class LocalOcrEngine : IPhotoOcrEngine
{
    private readonly ITextRegionDetector _regions;
    private readonly TextPreprocessOptions _preprocess;
    private readonly Lock _gate = new();

    private ITextRecognizer? _recognizer;
    private bool _disposed;

    /// <param name="regions">
    /// Localizador de lineas; <c>null</c> usa MSER, que no necesita modelo
    /// entrenado.
    /// </param>
    /// <param name="preprocess">Preparacion de cada recorte de linea.</param>
    protected LocalOcrEngine(ITextRegionDetector? regions = null, TextPreprocessOptions? preprocess = null)
    {
        _regions = regions ?? new MserTextRegionDetector();
        _preprocess = preprocess ?? new TextPreprocessOptions();
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Family { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public OcrEngineKind Kind => OcrEngineKind.Local;

    /// <inheritdoc />
    /// <remarks>
    /// Un motor local solo cuesta tiempo de CPU, asi que se le pueden pasar todas
    /// las variantes que la configuracion genere.
    /// </remarks>
    public virtual int MaxVariants => int.MaxValue;

    /// <summary>Localizador de lineas en uso.</summary>
    protected ITextRegionDetector Regions => _regions;

    /// <inheritdoc />
    public abstract bool IsAvailable(out string reason);

    /// <summary>Construye el reconocedor subyacente. Se llama una sola vez.</summary>
    protected abstract ITextRecognizer CreateRecognizer();

    /// <inheritdoc />
    public Task<OcrEngineResult> RecognizeAsync(
        Mat photo, string variant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsAvailable(out var reason))
        {
            return Task.FromResult(OcrEngineResult.Unavailable(this, variant, reason));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _recognizer ??= CreateRecognizer();

                var fragments = LocalTextReader.Read(
                    photo, _recognizer, _regions, _preprocess, cancellationToken);

                return Task.FromResult(OcrEngineResult.Success(this, variant, fragments, stopwatch.Elapsed));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (NativeLoadFailure.IsNativeLoadFailure(ex))
        {
            // Un binario nativo que no carga es un problema de la maquina, no de la
            // fotografia. Se trata como los demas fallos -restar una evidencia- para
            // que los motores de nube, que no dependen de nada nativo, sigan su curso.
            return Task.FromResult(OcrEngineResult.Failed(
                this, variant, NativeLoadFailure.Describe(ex), stopwatch.Elapsed));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException
                                       or OpenCVException or ArgumentException)
        {
            // Un motor que falla resta una evidencia, no tumba el analisis: el
            // sentido de tener varios es precisamente sobrevivir a que uno se caiga.
            return Task.FromResult(OcrEngineResult.Failed(this, variant, ex.Message, stopwatch.Elapsed));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Libera el reconocedor subyacente.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!disposing)
        {
            return;
        }

        lock (_gate)
        {
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }
}
