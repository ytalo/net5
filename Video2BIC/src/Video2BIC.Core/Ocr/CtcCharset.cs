namespace Video2BIC.Core.Ocr;

/// <summary>Posicion de la clase «en blanco» dentro de la salida del modelo.</summary>
public enum BlankPosition
{
    /// <summary>Indice 0. Es lo que usan PaddleOCR y la mayoria de CRNN publicados.</summary>
    First = 0,

    /// <summary>Ultimo indice. Es el convenio de <c>torch.nn.CTCLoss</c> por defecto.</summary>
    Last = 1,
}

/// <summary>
/// Alfabeto de un reconocedor CTC: la correspondencia entre el indice de clase que
/// emite el modelo y el caracter que representa.
/// </summary>
/// <remarks>
/// Restringir el alfabeto a mayusculas y digitos no es una simplificacion: en un
/// codigo BIC no hay minusculas ni signos, y quitarlos del alfabeto elimina de
/// raiz confusiones como <c>O</c> con <c>o</c> o <c>1</c> con <c>l</c>.
/// </remarks>
public sealed class CtcCharset
{
    private readonly string _characters;

    /// <summary>
    /// Construye el alfabeto.
    /// </summary>
    /// <param name="characters">Caracteres en el orden de clase del modelo, sin el blanco.</param>
    /// <param name="blank">Donde coloca el modelo la clase en blanco.</param>
    /// <exception cref="ArgumentException">Si el alfabeto esta vacio o tiene repetidos.</exception>
    public CtcCharset(string characters, BlankPosition blank = BlankPosition.First)
    {
        ArgumentNullException.ThrowIfNull(characters);

        if (characters.Length == 0)
        {
            throw new ArgumentException("El alfabeto no puede estar vacio.", nameof(characters));
        }

        if (characters.Distinct().Count() != characters.Length)
        {
            throw new ArgumentException("El alfabeto tiene caracteres repetidos.", nameof(characters));
        }

        _characters = characters;
        Blank = blank;
    }

    /// <summary>Alfabeto por defecto: los 36 caracteres que pueden aparecer en un codigo BIC.</summary>
    public static CtcCharset Alphanumeric { get; } = new("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ");

    /// <summary>Donde esta la clase en blanco.</summary>
    public BlankPosition Blank { get; }

    /// <summary>Caracteres del alfabeto, sin el blanco.</summary>
    public string Characters => _characters;

    /// <summary>Numero de clases que debe emitir el modelo: alfabeto mas el blanco.</summary>
    public int ClassCount => _characters.Length + 1;

    /// <summary>Indice de la clase en blanco.</summary>
    public int BlankIndex => Blank == BlankPosition.First ? 0 : _characters.Length;

    /// <summary>
    /// Caracter de una clase, o <c>'\0'</c> si el indice es el blanco o se sale del
    /// rango.
    /// </summary>
    public char this[int classIndex]
    {
        get
        {
            if (classIndex == BlankIndex || classIndex < 0 || classIndex >= ClassCount)
            {
                return '\0';
            }

            return Blank == BlankPosition.First ? _characters[classIndex - 1] : _characters[classIndex];
        }
    }

    /// <summary>
    /// Carga el alfabeto de un fichero de texto. Admite los dos formatos habituales:
    /// un caracter por linea (PaddleOCR) o todos los caracteres en una sola linea.
    /// </summary>
    /// <exception cref="FileNotFoundException">Si el fichero no existe.</exception>
    /// <exception cref="ArgumentException">Si el fichero no aporta ningun caracter.</exception>
    public static CtcCharset FromFile(string path, BlankPosition blank = BlankPosition.First)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No se encontro el alfabeto '{path}'.", path);
        }

        var lines = File.ReadAllLines(path)
            .Select(line => line.TrimEnd('\r', '\n'))
            .Where(line => line.Length > 0)
            .ToList();

        var characters = lines.Count == 1
            ? lines[0]
            : new string(lines.Select(line => line[0]).ToArray());

        return new CtcCharset(characters, blank);
    }
}
