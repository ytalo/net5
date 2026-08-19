namespace MaritimeVision.Cli;

/// <summary>Error de uso de la linea de comandos.</summary>
public sealed class CommandLineException(string message) : Exception(message);

/// <summary>
/// Analizador minimo de argumentos <c>--clave valor</c> y modificadores
/// <c>--bandera</c>, con alias cortos de una letra.
/// </summary>
/// <remarks>
/// Se implementa a mano en vez de usar <c>System.CommandLine</c> para no arrastrar
/// un paquete en prerelease a una herramienta que solo necesita esto.
/// </remarks>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _consumed = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["s"] = "source",
        ["o"] = "output",
        ["m"] = "model",
        ["l"] = "labels",
        ["c"] = "classes",
        ["h"] = "help",
    };

    private CommandLine(string command) => Command = command;

    /// <summary>Subcomando invocado.</summary>
    public string Command { get; }

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
                throw new CommandLineException($"Argumento inesperado: '{token}'.");
            }

            var trimmed = token.TrimStart('-');
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

            // Un token seguido de algo que no empieza por '-' es una opcion con
            // valor; en caso contrario es una bandera. Los numeros negativos no se
            // usan como valores en esta CLI, asi que la regla no tiene excepciones.
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

    /// <summary>Valor obligatorio de una opcion.</summary>
    /// <exception cref="CommandLineException">Si falta la opcion.</exception>
    public string GetRequiredString(string name)
        => GetString(name) ?? throw new CommandLineException($"Falta la opcion obligatoria --{name}.");

    /// <summary>Valor entero de una opcion.</summary>
    /// <exception cref="CommandLineException">Si el valor no es un entero.</exception>
    public int GetInt(string name, int fallback)
    {
        var raw = GetString(name);
        if (raw is null)
        {
            return fallback;
        }

        return int.TryParse(raw, out var value)
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

        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new CommandLineException($"--{name} espera un numero, no '{raw}'.");
    }

    /// <summary>Valor de enumerado, aceptando alias definidos por el llamante.</summary>
    /// <exception cref="CommandLineException">Si el valor no corresponde a ninguna opcion.</exception>
    public TEnum GetEnum<TEnum>(string name, TEnum fallback, IReadOnlyDictionary<string, TEnum>? aliases = null)
        where TEnum : struct, Enum
    {
        var raw = GetString(name);
        if (raw is null)
        {
            return fallback;
        }

        if (aliases is not null && aliases.TryGetValue(raw, out var aliased))
        {
            return aliased;
        }

        return Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value)
            ? value
            : throw new CommandLineException(
                $"--{name} no admite '{raw}'. Valores validos: " +
                $"{string.Join(", ", aliases?.Keys ?? Enum.GetNames<TEnum>().AsEnumerable())}.");
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
    /// Opciones que se indicaron pero que ningun comando llego a leer. Sirve para
    /// avisar de erratas en lugar de ignorarlas en silencio.
    /// </summary>
    public IReadOnlyList<string> UnknownOptions()
        => _values.Keys.Concat(_flags)
            .Where(name => !_consumed.Contains(name))
            .OrderBy(name => name)
            .ToList();

    private static string Expand(string name)
        => Aliases.TryGetValue(name, out var expanded) ? expanded : name;
}
