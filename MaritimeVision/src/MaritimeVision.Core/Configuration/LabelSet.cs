namespace MaritimeVision.Core.Configuration;

/// <summary>
/// Conjunto de etiquetas de un modelo, indexadas por identificador de clase.
/// </summary>
public sealed class LabelSet
{
    private static readonly string[] CocoNames =
    [
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
        "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog",
        "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella",
        "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball", "kite",
        "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket", "bottle",
        "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich", "orange",
        "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch", "potted plant",
        "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard", "cell phone",
        "microwave", "oven", "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors",
        "teddy bear", "hair drier", "toothbrush",
    ];

    private readonly string[] _labels;
    private readonly Dictionary<string, int> _byName;

    public LabelSet(IEnumerable<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        _labels = labels
            .Select(label => label.Trim())
            .Where(label => label.Length > 0 && !label.StartsWith('#'))
            .ToArray();

        if (_labels.Length == 0)
        {
            throw new ArgumentException("El conjunto de etiquetas no puede estar vacio.", nameof(labels));
        }

        _byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _labels.Length; i++)
        {
            _byName.TryAdd(_labels[i], i);
        }
    }

    /// <summary>Numero de clases.</summary>
    public int Count => _labels.Length;

    /// <summary>Etiquetas en orden de identificador de clase.</summary>
    public IReadOnlyList<string> Labels => _labels;

    /// <summary>Las 80 clases de COCO, en el orden estandar.</summary>
    public static LabelSet Coco { get; } = new(CocoNames);

    /// <summary>
    /// Perfil de clases para un modelo entrenado sobre terminal portuaria.
    /// Solo es util si el modelo cargado se entreno con estas mismas clases.
    /// </summary>
    public static LabelSet MaritimeContainer { get; } = new([
        "container", "container-truck", "chassis", "reach-stacker", "gantry-crane", "ship", "person",
    ]);

    /// <summary>Carga las etiquetas de un fichero de texto, una por linea.</summary>
    /// <exception cref="FileNotFoundException">Si el fichero no existe.</exception>
    public static LabelSet FromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No se encontro el fichero de etiquetas '{path}'.", path);
        }

        return new LabelSet(File.ReadAllLines(path));
    }

    /// <summary>
    /// Genera etiquetas sinteticas (<c>class_0</c>, <c>class_1</c>, ...) cuando el
    /// modelo declara mas clases de las que hay en el fichero de etiquetas.
    /// </summary>
    public static LabelSet Generic(int count)
        => new(Enumerable.Range(0, count).Select(index => $"class_{index}"));

    /// <summary>Nombre de una clase; devuelve <c>class_N</c> si el indice se sale del rango.</summary>
    public string GetLabel(int classId)
        => classId >= 0 && classId < _labels.Length ? _labels[classId] : $"class_{classId}";

    /// <summary>Busca el identificador de una clase por nombre. Devuelve -1 si no existe.</summary>
    public int GetClassId(string name)
        => _byName.TryGetValue(name.Trim(), out var id) ? id : -1;

    /// <summary>
    /// Traduce una lista de nombres o indices a identificadores de clase.
    /// Acepta indistintamente <c>"container"</c> y <c>"0"</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Si algun elemento no corresponde a ninguna clase.</exception>
    public IReadOnlySet<int> Resolve(IEnumerable<string> namesOrIndices)
    {
        ArgumentNullException.ThrowIfNull(namesOrIndices);

        var resolved = new HashSet<int>();
        var unknown = new List<string>();

        foreach (var raw in namesOrIndices)
        {
            var token = raw.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            if (int.TryParse(token, out var index))
            {
                if (index >= 0 && index < _labels.Length)
                {
                    resolved.Add(index);
                }
                else
                {
                    unknown.Add(token);
                }

                continue;
            }

            var classId = GetClassId(token);
            if (classId >= 0)
            {
                resolved.Add(classId);
            }
            else
            {
                unknown.Add(token);
            }
        }

        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"Clases desconocidas: {string.Join(", ", unknown)}. Disponibles: {string.Join(", ", _labels)}.",
                nameof(namesOrIndices));
        }

        return resolved;
    }

}
