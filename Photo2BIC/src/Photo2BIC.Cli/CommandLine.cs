using System.Globalization;

namespace Photo2BIC.Cli;

/// <summary>Error de uso de la linea de comandos.</summary>
public sealed class CommandLineException(string message) : Exception(message);

/// <summary>
/// Analizador de argumentos <c>--clave valor</c>, modificadores <c>--bandera</c> y
/// argumentos posicionales, con alias cortos de una letra.
/// </summary>
/// <remarks>
/// Se implementa a mano, igual que en MaritimeVision y Video2BIC, para no
/// arrastrar <c>System.CommandLine</c> a una herramienta que solo necesita esto.
/// </remarks>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _consumed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = [];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["e"] = "engines",
        ["j"] = "json",
        ["m"] = "onnx-model",
        ["d"] = "detalle",
        ["r"] = "recursivo",
        ["h"] = "help",
    };

    private CommandLine(string command) => Command = command;

    /// <summary>Subcomando invocado.</summary>
    public string Command { get; }

    /// <summary>Argumentos sueltos, en el orden en que aparecieron.</summary>
    public IReadOnlyList<string> Positional => _positional;

    /// <summary>Analiza los argumentos del proceso.</summary>
    /// <exception cref="CommandLineException">Si la sintaxis es invalida.</exception>
    public static CommandLine Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : string.Empty;
        var parsed = new CommandLine(command);
        var index = command.Length > 0 ? 1 : 0;

        while (index < args.Length)
        {
            var token = args[index];

            if (!token.StartsWith('-'))
            {
                parsed._positional.Add(token);
                index++;
                continue;
            }

            var trimmed = token.TrimStart('-');
            if (trimmed.Length == 0)
            {
                throw new CommandLineException($"Argumento inesperado: '{token}'.");
            }

            var separator = trimmed.IndexOf('=');

            // Forma --clave=valor.
            if (separator > 0)
            {
                parsed._values[Expand(trimmed[..separator])] = trimmed[(separator + 1)..];
                index++;
                continue;
            }

            var name = Expand(trimmed);
            var next = index + 1 < args.Length ? args[index + 1] : null;

            if (next is not null && !next.StartsWith('-'))
            {
                parsed._values[name] = next;
                index += 2;
            }
            else
            {
                parsed._flags.Add(name);
                index++;
            }
        }

        return parsed;
    }

    /// <summary>Valor de una opcion, o <paramref name="fallback"/> si no se indico.</summary>
    public string? GetString(string name, string? fallback = null)
    {
        _consumed.Add(name);
        return _values.TryGetValue(name, out var value) ? value : fallback;
    }

    /// <summary>Valor entero de una opcion.</summary>
    /// <exception cref="CommandLineException">Si el valor no es un entero.</exception>
    public int GetInt(string name, int fallback)
    {
        var raw = GetString(name);
        if (raw is null)
        {
            return fallback;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new CommandLineException($"--{name} espera un entero, no '{raw}'.");
    }

    /// <summary>Valor decimal de una opcion.</summary>
    /// <exception cref="CommandLineException">Si el valor no es un numero.</exception>
    public float GetFloat(string name, float fallback)
    {
        var raw = GetString(name);
        if (raw is null)
        {
            return fallback;
        }

        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new CommandLineException($"--{name} espera un numero, no '{raw}'.");
    }

    /// <summary>Indica si una bandera esta presente.</summary>
    public bool HasFlag(string name)
    {
        _consumed.Add(name);
        return _flags.Contains(name);
    }

    /// <summary>Lista separada por comas, ya troceada y sin elementos vacios.</summary>
    public IReadOnlyList<string> GetList(string name)
    {
        var raw = GetString(name);
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Opciones indicadas que ningun comando llego a leer. Sirve para avisar de
    /// erratas en vez de ignorarlas en silencio.
    /// </summary>
    public IReadOnlyList<string> UnknownOptions()
        => _values.Keys.Concat(_flags)
            .Where(name => !_consumed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static string Expand(string name)
        => Aliases.TryGetValue(name, out var expanded) ? expanded : name;
}
