using System.Text;
using System.Text.Json;
using Photo2BIC.Core.Configuration;
using Photo2BIC.Core.Engines;
using Photo2BIC.Core.Imaging;
using Photo2BIC.Core.Pipeline;
using Video2BIC.Core.Bic;

namespace Photo2BIC.Cli;

/// <summary>Punto de entrada de la herramienta de linea de comandos.</summary>
public static class Program
{
    /// <summary>Analisis correcto.</summary>
    private const int ExitOk = 0;

    /// <summary>Error de ejecucion.</summary>
    private const int ExitError = 1;

    /// <summary>Error de uso.</summary>
    private const int ExitUsage = 2;

    /// <summary>No se leyo ningun codigo. No es un fallo, pero un guion necesita distinguirlo.</summary>
    private const int ExitNoCode = 3;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = CommandLine.Parse(args);

            if (command.Command.Length == 0 || command.HasFlag("help"))
            {
                PrintUsage();
                return command.Command.Length == 0 && args.Length > 0 ? ExitUsage : ExitOk;
            }

            var exitCode = command.Command.ToLowerInvariant() switch
            {
                "read" or "leer" => await RunReadAsync(command).ConfigureAwait(false),
                "batch" or "lote" => await RunBatchAsync(command).ConfigureAwait(false),
                "engines" or "motores" => RunEngines(command),
                "check" or "validar" => RunCheck(command),
                "sample" or "ejemplo" => RunSample(command),
                _ => UnknownCommand(command.Command),
            };

            WarnAboutUnknownOptions(command);
            return exitCode;
        }
        catch (CommandLineException ex)
        {
            Console.Error.WriteLine($"Error de uso: {ex.Message}");
            Console.Error.WriteLine("Ejecuta 'photo2bic --help' para ver las opciones disponibles.");
            return ExitUsage;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Analisis cancelado.");
            return ExitError;
        }
        catch (Exception ex) when (NativeLoadFailure.IsNativeLoadFailure(ex))
        {
            // Sin OpenCV no se puede ni abrir la fotografia, asi que aqui no queda
            // analisis que salvar. Lo que si se puede es explicarlo en vez de dejar
            // un volcado de pila del cargador de bibliotecas.
            Console.Error.WriteLine($"Error: {NativeLoadFailure.Describe(ex)}");
            return ExitError;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException
                                       or InvalidOperationException or ArgumentException or IOException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }
    }

    /// <summary>Lee el codigo BIC de una fotografia.</summary>
    private static async Task<int> RunReadAsync(CommandLine command)
    {
        var path = command.Positional.FirstOrDefault()
                   ?? command.GetString("foto")
                   ?? throw new CommandLineException(
                       "'read' necesita la fotografia: photo2bic read contenedor.jpg");

        var settings = AppBuilder.BuildEngineSettings(command);
        var options = AppBuilder.BuildOptions(command);
        var engines = AppBuilder.BuildEngines(command, settings);

        using var reader = new PhotoBicReader(engines, options);

        PrintHeader(reader, path);
        WarnIfNoEngineAvailable(reader);

        var result = await reader.ReadAsync(path, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine($"Fotografia:  {result.Width}x{result.Height} px, " +
                          $"analizada en {result.Duration.TotalSeconds:F1}s");

        Output.Report(result, command.HasFlag("detalle"));
        WriteJson(command, result);

        return result.HasCode ? ExitOk : ExitNoCode;
    }

    /// <summary>Lee todas las fotografias de una carpeta.</summary>
    private static async Task<int> RunBatchAsync(CommandLine command)
    {
        var directory = command.Positional.FirstOrDefault()
                        ?? command.GetString("carpeta")
                        ?? throw new CommandLineException(
                            "'batch' necesita la carpeta: photo2bic batch fotos/");

        var photos = PhotoLoader.Enumerate(directory, command.HasFlag("recursivo"));

        if (photos.Count == 0)
        {
            Console.Error.WriteLine($"No hay fotografias en '{directory}'.");
            return ExitNoCode;
        }

        var settings = AppBuilder.BuildEngineSettings(command);
        var options = AppBuilder.BuildOptions(command);
        var engines = AppBuilder.BuildEngines(command, settings);

        // Los motores se comparten entre todas las fotografias: una sesion de ONNX
        // Runtime tarda mas en cargarse que en resolver una imagen, y recrearla por
        // foto seria el grueso del tiempo total.
        using var reader = new PhotoBicReader(engines, options);

        PrintHeader(reader, directory);
        WarnIfNoEngineAvailable(reader);
        Console.WriteLine($"Fotografias: {photos.Count}");
        Console.WriteLine();

        var results = new List<PhotoReadResult>(photos.Count);

        foreach (var photo in photos)
        {
            PhotoReadResult result;

            try
            {
                result = await reader.ReadAsync(photo, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                // Una foto ilegible en medio de una carpeta de mil no puede parar el
                // lote: se anota y se sigue.
                Console.Error.WriteLine($"[aviso] {Path.GetFileName(photo)}: {ex.Message}");
                continue;
            }

            results.Add(result);
            Console.WriteLine($"  {result}");
        }

        PrintBatchSummary(results);
        WriteBatchJson(command, results);
        WriteBatchCsv(command, results);

        return results.Any(result => result.HasCode) ? ExitOk : ExitNoCode;
    }

    /// <summary>Lista los motores y dice cuales estan listos para usarse.</summary>
    private static int RunEngines(CommandLine command)
    {
        var settings = AppBuilder.BuildEngineSettings(command);
        var engines = AppBuilder.BuildEngines(command, settings);

        try
        {
            var diagnosis = engines
                .Select(engine => (Engine: engine, Available: engine.IsAvailable(out var reason), Reason: reason))
                .ToList();

            Console.WriteLine("Motores de reconocimiento de texto:");
            Console.WriteLine();
            Output.Engines(diagnosis);

            Console.WriteLine();
            foreach (var (engine, _, _) in diagnosis)
            {
                Console.WriteLine($"  {engine.Name,-20} {engine.Description}");
            }

            Console.WriteLine();
            Console.WriteLine("Configuracion por variables de entorno:");
            foreach (var (variable, purpose) in EngineSettings.EnvironmentVariables)
            {
                var value = Environment.GetEnvironmentVariable(variable);
                var state = string.IsNullOrWhiteSpace(value) ? "  " : "* ";
                Console.WriteLine($"  {state}{variable,-28} {purpose}");
            }

            Console.WriteLine();
            Console.WriteLine("  (*) definida en este entorno. Los valores no se muestran.");

            var available = diagnosis.Count(entry => entry.Available);
            Console.WriteLine();
            Console.WriteLine($"{available} de {diagnosis.Count} motores listos.");

            return available > 0 ? ExitOk : ExitNoCode;
        }
        finally
        {
            foreach (var engine in engines)
            {
                engine.Dispose();
            }
        }
    }

    /// <summary>Valida un codigo escrito a mano, o le calcula el digito de control.</summary>
    private static int RunCheck(CommandLine command)
    {
        var text = command.Positional.FirstOrDefault()
                   ?? command.GetString("codigo")
                   ?? throw new CommandLineException(
                       "'check' necesita el codigo: photo2bic check CSQU3054383");

        var normalized = new string(text.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        // Con diez caracteres se entiende que falta el digito de control y se calcula.
        if (normalized.Length == BicCheckDigit.PrefixLength)
        {
            if (!BicCheckDigit.TryCompute(normalized, out var digit))
            {
                Console.Error.WriteLine($"'{text}' no es un prefijo valido de codigo BIC.");
                return ExitError;
            }

            Console.WriteLine($"{normalized}{digit}");
            Console.WriteLine($"Digito de control calculado: {digit}");
            return ExitOk;
        }

        if (!BicCode.TryParse(normalized, out var code))
        {
            Console.Error.WriteLine(
                $"'{text}' no tiene la forma de un codigo BIC " +
                "(3 letras de propietario, categoria U/J/Z, 6 digitos de serie y 1 de control).");
            return ExitError;
        }

        Console.WriteLine($"Codigo:      {code.ToDisplayString()}");
        Console.WriteLine($"Propietario: {code.OwnerCode}");
        Console.WriteLine($"Categoria:   {code.CategoryIdentifier}");
        Console.WriteLine($"Serie:       {code.SerialNumber}");
        Console.WriteLine($"Control:     {code.CheckDigit} (calculado {code.ExpectedCheckDigit})");
        Console.WriteLine(code.HasValidCheckDigit ? "Valido." : "INVALIDO: el digito de control no cuadra.");

        return code.HasValidCheckDigit ? ExitOk : ExitError;
    }

    /// <summary>
    /// Genera una fotografia sintetica de la puerta de un contenedor.
    /// </summary>
    /// <remarks>
    /// Para poder probar la aplicacion sin conseguir una fotografia: las de una
    /// terminal real ni se versionan ni son de uno.
    /// </remarks>
    private static int RunSample(CommandLine command)
    {
        var path = command.Positional.FirstOrDefault()
                   ?? command.GetString("salida")
                   ?? "ejemplo.jpg";

        var code = command.GetString("codigo") ?? "CSQU3054383";
        var normalized = new string(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        // Se exige un codigo valido: una escena de prueba con el digito de control
        // mal haria que la aplicacion no leyera nada y pareciera rota.
        if (!BicCode.TryParse(normalized, out var parsed) || !parsed.HasValidCheckDigit)
        {
            throw new CommandLineException(
                $"'{code}' no es un codigo BIC valido. " +
                "Calcula el digito de control con 'photo2bic check " +
                $"{(normalized.Length >= 10 ? normalized[..10] : "CSQU305438")}'.");
        }

        var written = SyntheticDoor.Write(
            path,
            parsed.Value,
            command.GetString("tamano-tipo", "22G1"),
            command.GetInt("ancho", 900),
            command.GetInt("alto", 600),
            !command.HasFlag("panel-claro"));

        Console.WriteLine($"Escrita '{written}' con el codigo {parsed.ToDisplayString()}.");
        Console.WriteLine($"Pruebala con: photo2bic read {written} --detalle");

        return ExitOk;
    }

    private static void PrintHeader(PhotoBicReader reader, string source)
    {
        var diagnosis = reader.Diagnose();
        var ready = diagnosis.Where(entry => entry.Available).Select(entry => entry.Engine.Name).ToList();

        Console.WriteLine($"Entrada:     {source}");
        Console.WriteLine($"Motores:     {(ready.Count > 0 ? string.Join(", ", ready) : "ninguno")} " +
                          $"({ready.Count} de {diagnosis.Count})");
    }

    private static void WarnIfNoEngineAvailable(PhotoBicReader reader)
    {
        if (reader.Diagnose().Any(entry => entry.Available))
        {
            return;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("[aviso] No hay ningun motor configurado, asi que no se va a leer nada.");
        Console.Error.WriteLine("        Lo mas rapido es instalar Tesseract:");
        Console.Error.WriteLine("          apt install tesseract-ocr | brew install tesseract");
        Console.Error.WriteLine("        Ejecuta 'photo2bic engines' para ver que le falta a cada uno.");
    }

    private static void PrintBatchSummary(IReadOnlyList<PhotoReadResult> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        var withCode = results.Count(result => result.HasCode);
        var confirmed = results.Count(result => result.IsConfirmed);
        var seconds = results.Sum(result => result.Duration.TotalSeconds);

        Console.WriteLine();
        Console.WriteLine($"{results.Count} fotografia(s) en {seconds:F1}s " +
                          $"({seconds / results.Count:F1}s por fotografia)");
        Console.WriteLine($"con codigo: {withCode} ({Percent(withCode, results.Count)}), " +
                          $"confirmados: {confirmed} ({Percent(confirmed, results.Count)})");

        // Cuantas veces acerto cada motor es la medida que de verdad interesa: es la
        // que dice si compensa pagar un servicio o si con lo local ya vale.
        var byEngine = results
            .SelectMany(result => result.SupportingEngines)
            .GroupBy(engine => engine, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ToList();

        if (byEngine.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Aportacion de cada motor al codigo elegido:");
        Output.Table(
            ["motor", "fotografias", "porcentaje"],
            byEngine
                .Select(group => new[]
                {
                    group.Key,
                    group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Percent(group.Count(), withCode),
                })
                .ToList());
    }

    private static string Percent(int part, int total)
        => total == 0 ? "0%" : $"{(double)part / total:P0}";

    private static void WriteJson(CommandLine command, PhotoReadResult result)
    {
        if (command.GetString("json") is not { } path)
        {
            return;
        }

        WriteFile(path, result.ToJson());
        Console.WriteLine();
        Console.WriteLine($"Informe JSON en '{path}'.");
    }

    private static void WriteBatchJson(CommandLine command, IReadOnlyList<PhotoReadResult> results)
    {
        if (command.GetString("json") is not { } path)
        {
            return;
        }

        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var result in results)
            {
                result.WriteTo(writer);
            }

            writer.WriteEndArray();
        }

        WriteFile(path, Encoding.UTF8.GetString(buffer.ToArray()));
        Console.WriteLine($"Informe JSON en '{path}'.");
    }

    private static void WriteBatchCsv(CommandLine command, IReadOnlyList<PhotoReadResult> results)
    {
        if (command.GetString("csv") is not { } path)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("fichero,codigo,confianza,confirmado,ambiguo,tamanoTipo,motores,segundos");

        foreach (var result in results)
        {
            var best = result.Verdict.Best;

            builder.Append(Csv(Path.GetFileName(result.Source))).Append(',');
            builder.Append(Csv(best?.Code.Value ?? string.Empty)).Append(',');
            builder.Append((best?.Score ?? 0f).ToString("F4", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',');
            builder.Append(result.IsConfirmed ? "si" : "no").Append(',');
            builder.Append(result.Verdict.Ambiguous ? "si" : "no").Append(',');
            builder.Append(Csv(result.SizeType?.Value ?? string.Empty)).Append(',');
            builder.Append(Csv(string.Join(' ', result.SupportingEngines))).Append(',');
            builder.AppendLine(result.Duration.TotalSeconds
                .ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }

        WriteFile(path, builder.ToString());
        Console.WriteLine($"Informe CSV en '{path}'.");
    }

    private static string Csv(string value)
        => value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static void WriteFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Encoding.UTF8);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Comando desconocido: '{command}'.");
        PrintUsage();
        return ExitUsage;
    }

    private static void WarnAboutUnknownOptions(CommandLine command)
    {
        foreach (var option in command.UnknownOptions())
        {
            Console.Error.WriteLine($"[aviso] La opcion --{option} no la usa el comando '{command.Command}'.");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            photo2bic - lee el codigo BIC (ISO 6346) de la fotografia de un contenedor
                        combinando varios motores de reconocimiento de texto.

            USO
              photo2bic read <fotografia> [opciones]     lee una fotografia
              photo2bic batch <carpeta> [opciones]       lee todas las de una carpeta
              photo2bic engines                          que motores hay y cuales estan listos
              photo2bic check <codigo>                   valida un codigo o le calcula el control
              photo2bic sample <fichero>                 genera una fotografia de prueba

            MOTORES
              Locales   tesseract-lineas   Tesseract sobre las lineas que localiza MSER
                        tesseract-disperso Tesseract sobre la fotografia entera
                        onnx               modelo CRNN/CTC propio en ONNX Runtime
              Nube      azure              Azure AI Vision (imageanalysis:analyze, read)
                        google             Google Cloud Vision (TEXT_DETECTION)
                        textract           Amazon Textract (DetectDocumentText)
                        ocrspace           OCR.space

              Las credenciales se leen de variables de entorno, nunca de la linea de
              comandos. 'photo2bic engines' dice cual le falta a cada motor.

            OPCIONES
              -e, --engines <lista>     motores a usar, separados por comas.
                                        Atajos: local, nube, tesseract, todos (por defecto)
              -j, --json <fichero>      informe completo en JSON
                  --csv <fichero>       resumen en CSV (solo en 'batch')
              -d, --detalle             ensena lo que leyo cada motor
              -r, --recursivo           en 'batch', entra en las subcarpetas

              -m, --onnx-model <ruta>   modelo CRNN/CTC en ONNX
                  --onnx-charset <ruta> alfabeto del modelo, un caracter por linea
                  --proveedor <nombre>  proveedor de ONNX Runtime (Cpu, Cuda, DirectMl...)
                  --tesseract <ruta>    ejecutable de Tesseract, si no esta en el PATH

                  --max-ancho <px>      ancho al que se reduce la foto (1600)
                  --variantes <n>       variantes de preprocesado por motor (4)
                  --paralelismo <n>     motores simultaneos (4)
                  --timeout <s>         limite del analisis de una foto (120)
                  --timeout-nube <s>    limite de cada llamada a un servicio (30)

                  --min-confianza <0-1> confianza combinada para confirmar (0,6)
                  --min-familias <n>    familias que deben coincidir para confirmar (1)
                  --margen-ambiguedad <0-1>  diferencia por debajo de la cual dos
                                        codigos se declaran empatados (0,15)
                  --peso <lista>        peso por familia: --peso azure=1,tesseract=0.6
                  --max-correcciones <n> caracteres reparables por lectura (2)
                  --sin-digito-control  acepta codigos con el control incorrecto.
                                        Solo para diagnosticar: dispara los falsos positivos

            OPCIONES DE 'sample'
                  --codigo <bic>        codigo a pintar (CSQU3054383)
                  --tamano-tipo <cod>   codigo de tamano y tipo (22G1)
                  --ancho <px> --alto <px>   tamano de la escena (900x600)
                  --panel-claro         codigo oscuro sobre panel claro

            EJEMPLOS
              photo2bic sample ejemplo.jpg && photo2bic read ejemplo.jpg --detalle
              photo2bic read puerta.jpg --detalle
              photo2bic read puerta.jpg --engines tesseract,azure --json informe.json
              photo2bic batch fotos/ --recursivo --csv resultados.csv --min-familias 2

            CODIGOS DE SALIDA
              0 correcto   1 error de ejecucion   2 error de uso   3 sin codigo leido
            """);
    }
}
