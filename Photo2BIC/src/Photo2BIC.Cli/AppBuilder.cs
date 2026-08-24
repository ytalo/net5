using MaritimeVision.Core.Detectors;
using Photo2BIC.Core.Configuration;
using Photo2BIC.Core.Engines;
using Photo2BIC.Core.Pipeline;

namespace Photo2BIC.Cli;

/// <summary>
/// Traduce la linea de comandos a configuracion y motores.
/// </summary>
/// <remarks>
/// Separado del punto de entrada para que las opciones se puedan construir y
/// comprobar sin ejecutar nada.
/// </remarks>
public static class AppBuilder
{
    /// <summary>Configuracion de los motores, mezclando entorno y linea de comandos.</summary>
    /// <remarks>
    /// Las credenciales solo salen del entorno. De la linea de comandos vienen las
    /// rutas y los ajustes, que no son secretos y ganan mucho en comodidad.
    /// </remarks>
    public static EngineSettings BuildEngineSettings(CommandLine command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var settings = EngineSettings.FromEnvironment();

        if (command.GetString("onnx-model") is { } model)
        {
            settings.OnnxModelPath = model;
        }

        if (command.GetString("onnx-charset") is { } charset)
        {
            settings.OnnxCharsetPath = charset;
        }

        if (command.GetString("tesseract") is { } executable)
        {
            settings.TesseractExecutable = executable;
        }

        if (command.GetString("proveedor") is { } provider)
        {
            settings.OnnxProvider = Enum.TryParse<ExecutionProvider>(provider, ignoreCase: true, out var parsed)
                ? parsed
                : throw new CommandLineException(
                    $"--proveedor no admite '{provider}'. Valores validos: " +
                    $"{string.Join(", ", Enum.GetNames<ExecutionProvider>())}.");
        }

        var timeout = command.GetInt("timeout-nube", 30);
        if (timeout <= 0)
        {
            throw new CommandLineException("--timeout-nube debe ser positivo.");
        }

        settings.CloudTimeout = TimeSpan.FromSeconds(timeout);

        return settings;
    }

    /// <summary>Motores pedidos.</summary>
    /// <exception cref="CommandLineException">Si se pide un motor que no existe.</exception>
    public static IReadOnlyList<IPhotoOcrEngine> BuildEngines(CommandLine command, EngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            return EngineRegistry.Build(settings, command.GetList("engines"));
        }
        catch (ArgumentException ex)
        {
            throw new CommandLineException(ex.Message);
        }
    }

    /// <summary>Configuracion del analisis.</summary>
    public static Photo2BicOptions BuildOptions(CommandLine command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var options = new Photo2BicOptions
        {
            MaxWidth = command.GetInt("max-ancho", 1600),
            MaxVariantsPerEngine = command.GetInt("variantes", 4),
            MaxConcurrency = command.GetInt("paralelismo", 4),
            Timeout = TimeSpan.FromSeconds(command.GetInt("timeout", 120)),
        };

        options.Consensus.MinScore = command.GetFloat("min-confianza", 0.6f);
        options.Consensus.MinFamilies = command.GetInt("min-familias", 1);
        options.Consensus.AmbiguityMargin = command.GetFloat("margen-ambiguedad", 0.15f);

        options.Parser.MaxCorrections = command.GetInt("max-correcciones", 2);

        // Sin exigir el digito de control cualquier matricula o rotulo publicitario
        // se convierte en un codigo plausible. Solo tiene sentido para diagnosticar
        // por que no se lee un contenedor concreto, y por eso se avisa.
        if (command.HasFlag("sin-digito-control"))
        {
            options.Parser.RequireValidCheckDigit = false;
        }

        foreach (var pair in command.GetList("peso"))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new CommandLineException(
                    $"--peso espera pares familia=valor, no '{pair}'. Ejemplo: --peso azure=1,tesseract=0.6");
            }

            var family = pair[..separator].Trim();
            var raw = pair[(separator + 1)..].Trim();

            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var weight))
            {
                throw new CommandLineException($"--peso espera un numero para '{family}', no '{raw}'.");
            }

            options.Consensus.FamilyWeights[family] = weight;
        }

        try
        {
            options.Validate();
        }
        catch (InvalidOperationException ex)
        {
            throw new CommandLineException(ex.Message);
        }

        return options;
    }
}
