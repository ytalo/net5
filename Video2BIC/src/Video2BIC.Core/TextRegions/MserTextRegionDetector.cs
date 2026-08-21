using OpenCvSharp;

namespace Video2BIC.Core.TextRegions;

/// <summary>
/// Localiza lineas de texto agrupando caracteres detectados con MSER, sin modelo
/// entrenado.
/// </summary>
/// <remarks>
/// <para>
/// MSER (<i>maximally stable extremal regions</i>) busca manchas cuyo contorno
/// apenas cambia al mover el umbral de binarizacion. Un caracter pintado sobre
/// chapa es exactamente eso: una region uniforme y bien separada del fondo. La
/// propiedad que lo hace preferible a un filtro morfologico es que <b>no depende
/// del tamano</b>: la misma configuracion sirve para un contenedor que ocupa 80
/// pixeles y para otro que ocupa 900, mientras que un nucleo morfologico esta
/// atado al tamano de caracter para el que se eligio.
/// </para>
/// <para>
/// Se busca dos veces, sobre la imagen y sobre su negativo, porque el codigo va
/// pintado en blanco sobre pintura oscura tan a menudo como en negro sobre
/// pintura clara.
/// </para>
/// <para>
/// El corrugado del panel es la principal fuente de falsos positivos: son crestas
/// verticales largas y regulares. La geometria de caracter las descarta de entrada
/// (una cresta de 3x300 pixeles no tiene la proporcion de una letra), y lo que
/// sobrevive tiene que ademas alinearse con otros dos caracteres para llegar a ser
/// una linea.
/// </para>
/// </remarks>
public sealed class MserTextRegionDetector : ITextRegionDetector
{
    private readonly TextRegionOptions _options;

    public MserTextRegionDetector(TextRegionOptions? options = null)
    {
        _options = options ?? new TextRegionOptions();
        _options.Validate();
    }

    /// <inheritdoc />
    public string Description => "Regiones de texto por MSER y agrupacion de caracteres en lineas";

    /// <inheritdoc />
    public IReadOnlyList<TextRegion> Detect(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Empty() || image.Width < 16 || image.Height < 16)
        {
            return Array.Empty<TextRegion>();
        }

        using var gray = ToGray(image);

        var characters = FindCharacters(gray);
        if (characters.Count < _options.MinCharactersPerLine)
        {
            return Array.Empty<TextRegion>();
        }

        return GroupIntoLines(characters, image.Width, image.Height)
            .OrderByDescending(region => region.Score)
            .Take(_options.MaxRegions)
            // El orden de lectura se restablece al final: primero se elige que
            // lineas sobreviven al presupuesto, luego se ordenan como se leen.
            .OrderBy(region => region.Box.Y)
            .ThenBy(region => region.Box.X)
            .ToList();
    }

    private Mat ToGray(Mat image)
    {
        var gray = new Mat();

        if (image.Channels() == 1)
        {
            image.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        }

        if (!_options.EnhanceContrast)
        {
            return gray;
        }

        using var clahe = Cv2.CreateCLAHE(clipLimit: 2.5d, tileGridSize: new Size(8, 8));
        clahe.Apply(gray, gray);
        return gray;
    }

    /// <summary>Caja de un candidato a caracter, con la densidad de su region.</summary>
    private readonly record struct Character(Rect Box, float Fill)
    {
        public int CenterY => Box.Y + (Box.Height / 2);
    }

    private List<Character> FindCharacters(Mat gray)
    {
        var area = (long)gray.Width * gray.Height;
        var minHeight = Math.Max(4, (int)(gray.Height * _options.MinCharacterHeightRatio));
        var maxHeight = (int)(gray.Height * _options.MaxCharacterHeightRatio);

        // Los limites de area de MSER acotan la busqueda antes de generar regiones:
        // se derivan de los mismos limites de altura, suponiendo un caracter que
        // ocupa entre una decima parte y todo el ancho de su caja.
        using var mser = MSER.Create(
            delta: _options.Delta,
            minArea: Math.Max(16, minHeight * minHeight / 8),
            maxArea: (int)Math.Min(int.MaxValue, Math.Max(64, maxHeight * maxHeight * 2)));

        var candidates = new List<Character>();

        Collect(gray, candidates);

        // Segunda pasada sobre el negativo: MSER encuentra manchas oscuras sobre
        // fondo claro, y la mitad de los codigos estan pintados al reves.
        using (var inverted = new Mat())
        {
            Cv2.BitwiseNot(gray, inverted);
            Collect(inverted, candidates);
        }

        // El limite se aplica despues de quitar repetidos, no antes: MSER devuelve la
        // misma letra una vez por cada umbral en el que se mantiene estable, y
        // truncar la lista cruda dejaria fuera pasadas enteras.
        return Deduplicate(candidates).Take(_options.MaxCharacterCandidates).ToList();

        void Collect(Mat source, List<Character> into)
        {
            mser.DetectRegions(source, out var regions, out var boxes);

            for (var index = 0; index < boxes.Length; index++)
            {
                var box = boxes[index];

                if (box.Height < minHeight || box.Height > maxHeight || box.Width < 2)
                {
                    continue;
                }

                var aspect = (double)box.Width / box.Height;
                if (aspect < _options.MinCharacterAspect || aspect > _options.MaxCharacterAspect)
                {
                    continue;
                }

                var boxArea = (double)box.Width * box.Height;
                var fill = boxArea > 0d ? regions[index].Length / boxArea : 0d;
                if (fill < _options.MinCharacterFill || fill > _options.MaxCharacterFill)
                {
                    continue;
                }

                // Una region que abarca casi toda la imagen es el panel, no una letra.
                if (boxArea > area * 0.25d)
                {
                    continue;
                }

                into.Add(new Character(box, (float)fill));
            }
        }
    }

    /// <summary>
    /// Descarta las regiones repetidas. MSER devuelve el mismo caracter varias
    /// veces, una por cada umbral en el que se mantiene estable, y las dos pasadas
    /// (imagen y negativo) tambien se solapan.
    /// </summary>
    /// <remarks>
    /// La comparacion es por interseccion sobre union, no sobre la caja menor. La
    /// diferencia decide: dentro de la caja de una letra caben trozos de corrugado, y
    /// con el criterio de la caja menor esos trozos hacen que la letra entera se
    /// descarte como duplicada. Con la union solo son duplicados dos recuadros que
    /// ocupan practicamente el mismo sitio.
    /// </remarks>
    private static List<Character> Deduplicate(List<Character> candidates)
    {
        var kept = new List<Character>();

        foreach (var candidate in candidates.OrderByDescending(item => item.Box.Width * item.Box.Height))
        {
            var duplicate = false;

            foreach (var existing in kept)
            {
                if (IntersectionOverUnion(existing.Box, candidate.Box) > 0.5d)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    private static double IntersectionOverUnion(Rect first, Rect second)
    {
        var intersection = first & second;
        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return 0d;
        }

        var overlap = (double)intersection.Width * intersection.Height;
        var union = ((double)first.Width * first.Height)
                    + ((double)second.Width * second.Height)
                    - overlap;

        return union > 0d ? overlap / union : 0d;
    }

    /// <summary>
    /// Une en una linea los caracteres que comparten altura, estan alineados
    /// verticalmente y se tocan de lado.
    /// </summary>
    private List<TextRegion> GroupIntoLines(List<Character> characters, int width, int height)
    {
        var ordered = characters.OrderBy(character => character.Box.X).ToList();
        var groups = new DisjointSet(ordered.Count);

        // Cada caracter se enlaza solo con el vecino compatible mas cercano a su
        // derecha, no con todos los compatibles. Unir todos los pares haria que dos
        // lineas distintas acabasen en el mismo grupo en cuanto un caracter de una
        // cayese cerca de otro de la otra: la union es transitiva y una cadena de
        // saltos pequenos recorre el panel entero.
        for (var i = 0; i < ordered.Count; i++)
        {
            var nearest = -1;
            var nearestGap = int.MaxValue;

            for (var j = 0; j < ordered.Count; j++)
            {
                if (i == j || ordered[j].Box.X <= ordered[i].Box.X)
                {
                    continue;
                }

                if (!BelongToTheSameLine(ordered[i], ordered[j]))
                {
                    continue;
                }

                var gap = ordered[j].Box.X - ordered[i].Box.Right;
                if (gap < nearestGap)
                {
                    nearestGap = gap;
                    nearest = j;
                }
            }

            if (nearest >= 0)
            {
                groups.Union(i, nearest);
            }
        }

        var members = new Dictionary<int, List<Character>>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var root = groups.Find(index);
            if (!members.TryGetValue(root, out var list))
            {
                list = [];
                members[root] = list;
            }

            list.Add(ordered[index]);
        }

        var regions = new List<TextRegion>();

        foreach (var line in members.Values)
        {
            if (line.Count < _options.MinCharactersPerLine)
            {
                continue;
            }

            var box = line[0].Box;
            var covered = 0d;
            var tallest = 0;

            foreach (var character in line)
            {
                box |= character.Box;
                covered += (double)character.Box.Width * character.Box.Height;
                tallest = Math.Max(tallest, character.Box.Height);
            }

            // Una linea de texto no puede ser mucho mas alta que sus propias letras.
            // Si lo es, la cadena de enlaces ha ido derivando en vertical y el grupo
            // mezcla dos rotulos distintos.
            if (box.Height > tallest * 1.8d)
            {
                continue;
            }

            var boxArea = (double)box.Width * box.Height;
            var density = boxArea > 0d ? Math.Clamp(covered / boxArea, 0d, 1d) : 0d;

            if (density < _options.MinLineDensity)
            {
                continue;
            }

            regions.Add(new TextRegion(Pad(box, width, height), (float)density));
        }

        return regions;
    }

    private bool BelongToTheSameLine(in Character first, in Character second)
    {
        var tallest = (double)Math.Max(first.Box.Height, second.Box.Height);
        var shortest = (double)Math.Min(first.Box.Height, second.Box.Height);

        if (shortest <= 0d || tallest / shortest > _options.MaxHeightRatioInLine)
        {
            return false;
        }

        if (Math.Abs(first.CenterY - second.CenterY) > tallest * _options.MaxVerticalOffsetRatio)
        {
            return false;
        }

        // Dos caracteres de la misma linea estan uno al lado del otro, no uno encima
        // del otro. Sin esta comprobacion, dos rotulos apilados con la misma altura
        // se enlazarian: se solapan por completo en horizontal, que es justo lo que
        // el hueco negativo mide como "muy cerca".
        var horizontalOverlap = Math.Min(first.Box.Right, second.Box.Right)
                                - Math.Max(first.Box.X, second.Box.X);
        var narrowest = Math.Min(first.Box.Width, second.Box.Width);

        if (horizontalOverlap > narrowest * 0.5d)
        {
            return false;
        }

        return -horizontalOverlap <= tallest * _options.MaxGapRatio;
    }

    private Rect Pad(Rect box, int width, int height)
    {
        var padding = _options.Padding;
        var left = Math.Max(0, box.X - padding);
        var top = Math.Max(0, box.Y - padding);
        var right = Math.Min(width, box.X + box.Width + padding);
        var bottom = Math.Min(height, box.Y + box.Height + padding);
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Conjuntos disjuntos con compresion de caminos, para agrupar caracteres.</summary>
    private sealed class DisjointSet
    {
        private readonly int[] _parent;

        public DisjointSet(int size)
        {
            _parent = new int[size];
            for (var index = 0; index < size; index++)
            {
                _parent[index] = index;
            }
        }

        public int Find(int item)
        {
            while (_parent[item] != item)
            {
                _parent[item] = _parent[_parent[item]];
                item = _parent[item];
            }

            return item;
        }

        public void Union(int first, int second)
        {
            var rootFirst = Find(first);
            var rootSecond = Find(second);

            if (rootFirst != rootSecond)
            {
                _parent[rootSecond] = rootFirst;
            }
        }
    }
}
