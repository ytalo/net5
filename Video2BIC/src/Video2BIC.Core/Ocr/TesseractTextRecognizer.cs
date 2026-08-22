using System.Diagnostics;
using System.Globalization;
using OpenCvSharp;
using Video2BIC.Core.Text;

namespace Video2BIC.Core.Ocr;

/// <summary>Configuracion de <see cref="TesseractTextRecognizer"/>.</summary>
public sealed class TesseractOptions
{
    /// <summary>Ejecutable a invocar. Debe estar en el <c>PATH</c> o ser una ruta completa.</summary>
    public string Executable { get; set; } = "tesseract";

    /// <summary>Idioma de los datos entrenados.</summary>
    public string Language { get; set; } = "eng";

    /// <summary>
    /// Modo de segmentacion de pagina (<c>--psm</c>).
    /// </summary>
    /// <remarks>
    /// Por defecto 7, «una sola linea de texto»: se localizan las lineas antes y se
    /// le pasa cada una recortada, que es lo que mejor funciona sobre chapa
    /// corrugada. Con 11 («texto disperso») se le entrega el panel entero y se deja
    /// que busque el solo; cuesta un unico proceso en vez de uno por linea, pero
    /// acierta bastante menos.
    /// </remarks>
    public int PageSegmentationMode { get; set; } = 7;

    /// <summary>
    /// Caracteres admitidos. Restringirlo a mayusculas y digitos elimina de un
    /// plumazo las confusiones con minusculas y signos de puntuacion.
    /// </summary>
    public string CharacterWhitelist { get; set; } = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>Confianza minima por palabra, en [0, 1].</summary>
    public float MinConfidence { get; set; } = 0.35f;

    /// <summary>Tiempo maximo por llamada.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20d);

    /// <summary>Modos de <c>--psm</c> en los que Tesseract analiza la disposicion del texto.</summary>
    public bool AnalyzesLayout => PageSegmentationMode is 1 or 3 or 4 or 5 or 6 or 11 or 12;
}

/// <summary>
/// Reconocedor apoyado en el binario de Tesseract.
/// </summary>
/// <remarks>
/// <para>
/// Existe para que la aplicacion sea utilizable el primer dia, sin tener que
/// entrenar ni exportar un modelo: Tesseract se instala con el gestor de paquetes
/// del sistema y ya reconoce mayusculas y digitos razonablemente sobre rotulos
/// grandes y bien contrastados, que es exactamente lo que es un codigo BIC.
/// </para>
/// <para>
/// Se invoca por proceso y no por enlace nativo a proposito: evita arrastrar un
/// paquete con binarios nativos por plataforma y hace que la ausencia de Tesseract
/// sea un aviso claro en vez de un <c>DllNotFoundException</c>.
/// </para>
/// <para>
/// Para video en tiempo real conviene <see cref="OnnxTextRecognizer"/>: lanzar un
/// proceso por recorte cuesta decenas de milisegundos solo en arrancarlo.
/// </para>
/// </remarks>
public sealed class TesseractTextRecognizer : ITextRecognizer
{
    private readonly TesseractOptions _options;
    private readonly string _workDirectory;
    private bool _disposed;

    public TesseractTextRecognizer(TesseractOptions? options = null)
    {
        _options = options ?? new TesseractOptions();

        if (_options.MinConfidence is < 0f or > 1f)
        {
            throw new ArgumentException("MinConfidence debe estar en [0, 1].", nameof(options));
        }

        _workDirectory = Path.Combine(Path.GetTempPath(), "video2bic", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_workDirectory);
    }

    /// <inheritdoc />
    public string Description => $"Tesseract '{_options.Executable}' psm {_options.PageSegmentationMode}, " +
                                 $"idioma {_options.Language}";

    /// <inheritdoc />
    public bool PerformsLayoutAnalysis => _options.AnalyzesLayout;

    /// <summary>
    /// Comprueba que el binario esta instalado y responde. Devuelve la version en
    /// <paramref name="version"/> para poder mostrarla al arrancar.
    /// </summary>
    public static bool IsAvailable(string executable, out string version)
    {
        version = string.Empty;

        try
        {
            using var process = Start(executable, ["--version"], out var startError);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            version = output.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
            return process.ExitCode == 0 && startError is null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <inheritdoc cref="IsAvailable(string, out string)" />
    public static bool IsAvailable(string executable = "tesseract") => IsAvailable(executable, out _);

    /// <inheritdoc />
    public IReadOnlyList<TextFragment> Recognize(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (image.Empty() || image.Width < 4 || image.Height < 4)
        {
            return Array.Empty<TextFragment>();
        }

        var imagePath = Path.Combine(_workDirectory, $"{Guid.NewGuid():n}.png");

        try
        {
            if (!Cv2.ImWrite(imagePath, image))
            {
                throw new InvalidOperationException($"No se pudo escribir el recorte temporal '{imagePath}'.");
            }

            var arguments = new List<string>
            {
                imagePath,
                "stdout",
                "--psm", _options.PageSegmentationMode.ToString(CultureInfo.InvariantCulture),
                "-l", _options.Language,
            };

            if (_options.CharacterWhitelist.Length > 0)
            {
                arguments.Add("-c");
                arguments.Add($"tessedit_char_whitelist={_options.CharacterWhitelist}");
            }

            // El nombre de configuracion va el ultimo, despues de todas las opciones.
            // Tesseract interpreta como fichero de configuracion cualquier argumento
            // que siga al primero de ellos, asi que ponerlo antes hace que se ignoren
            // en silencio el --psm y el idioma.
            arguments.Add("tsv");

            using var process = Start(_options.Executable, arguments, out var startError)
                ?? throw new InvalidOperationException(
                    $"No se pudo ejecutar '{_options.Executable}'. " +
                    "Instala Tesseract o indica otro motor de OCR. " + startError);

            // La salida de error se drena en paralelo: leer las dos tuberias en
            // serie bloquearia el proceso si una de ellas llena su buffer.
            var errorTask = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit((int)_options.Timeout.TotalMilliseconds))
            {
                TryKill(process);
                throw new TimeoutException(
                    $"Tesseract no respondio en {_options.Timeout.TotalSeconds:F0} s.");
            }

            if (process.ExitCode != 0)
            {
                var error = errorTask.GetAwaiter().GetResult();
                throw new InvalidOperationException(
                    $"Tesseract termino con codigo {process.ExitCode}: {error.Trim()}");
            }

            return ParseTsv(output, _options.MinConfidence);
        }
        finally
        {
            TryDelete(imagePath);
        }
    }

    /// <summary>
    /// Interpreta la salida TSV de Tesseract quedandose con las palabras (nivel 5).
    /// </summary>
    /// <remarks>
    /// El nivel de palabra es el adecuado: un codigo BIC repartido en varias lineas
    /// llega como palabras sueltas en orden de lectura, que es justo lo que el
    /// analizador de codigos sabe recomponer.
    /// </remarks>
    internal static IReadOnlyList<TextFragment> ParseTsv(string tsv, float minConfidence)
    {
        var fragments = new List<TextFragment>();

        foreach (var line in tsv.Split('\n'))
        {
            var columns = line.TrimEnd('\r').Split('\t');

            // level page block par line word left top width height conf text
            if (columns.Length < 12 || columns[0] != "5")
            {
                continue;
            }

            var text = columns[11].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (!float.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
            {
                continue;
            }

            // Tesseract expresa la confianza en porcentaje, y con -1 cuando no
            // reconocio nada en esa casilla.
            var normalized = confidence < 0f ? 0f : confidence / 100f;
            if (normalized < minConfidence)
            {
                continue;
            }

            fragments.Add(new TextFragment(
                text,
                normalized,
                ParseInt(columns[6]),
                ParseInt(columns[7]),
                ParseInt(columns[8]),
                ParseInt(columns[9])));
        }

        return fragments;
    }

    private static int ParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static Process? Start(string executable, IReadOnlyList<string> arguments, out string? error)
    {
        error = null;

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // El proceso ya habia terminado por su cuenta.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Limpieza best-effort: no debe hacer fallar el analisis.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Limpieza best-effort.
        }
    }
}
