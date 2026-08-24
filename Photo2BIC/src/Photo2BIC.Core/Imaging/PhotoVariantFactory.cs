using OpenCvSharp;

namespace Photo2BIC.Core.Imaging;

/// <summary>
/// Genera varias versiones preprocesadas de la misma fotografia.
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza que sustituye a los multiples fotogramas del video. Un contenedor
/// grabado se ve decenas de veces y basta con votar; en una fotografia solo hay
/// una oportunidad, asi que hay que fabricar variedad: la misma imagen igualada
/// de contraste, binarizada de dos maneras distintas, con la polaridad invertida
/// y ampliada. Cada preprocesado falla de forma diferente y ese desacuerdo es
/// justo lo que el consenso necesita para decidir.
/// </para>
/// <para>
/// No es teorico: el codigo de un contenedor esta pintado en blanco sobre panel
/// oscuro tan a menudo como en negro sobre panel claro, y un umbral global se
/// hunde en cuanto media puerta esta a pleno sol y la otra media a la sombra,
/// que es la situacion normal en una terminal.
/// </para>
/// </remarks>
public sealed class PhotoVariantFactory
{
    private readonly PhotoVariantOptions _options;

    public PhotoVariantFactory(PhotoVariantOptions? options = null)
    {
        _options = options ?? new PhotoVariantOptions();
        _options.Validate();
    }

    /// <summary>Configuracion en uso.</summary>
    public PhotoVariantOptions Options => _options;

    /// <summary>
    /// Construye el juego de variantes.
    /// </summary>
    /// <param name="photo">Fotografia BGR o en escala de grises; no se modifica.</param>
    /// <returns>
    /// Las variantes, con <c>original</c> siempre en primer lugar. El llamante es
    /// responsable de liberarlas; <see cref="PhotoVariantSet"/> lo hace en bloque.
    /// </returns>
    public PhotoVariantSet Create(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        var variants = new List<PhotoVariant>();

        if (photo.Empty())
        {
            return new PhotoVariantSet(variants);
        }

        try
        {
            variants.Add(new PhotoVariant(
                "original",
                "La fotografia tal cual, sin tocar",
                photo.Clone()));

            using var gray = ToGray(photo);

            // Las binarizaciones y el enfoque parten del contraste ya igualado: es
            // el punto donde el trazo esta mas separado del fondo, y por tanto donde
            // cualquier umbral toma mejores decisiones. Se calcula una sola vez y la
            // variante 'clahe' es una copia suya.
            using var equalized = _options.Clahe ? ApplyClahe(gray) : gray.Clone();

            if (_options.Clahe)
            {
                variants.Add(new PhotoVariant(
                    "clahe",
                    "Contraste igualado por bloques; recupera el rotulo a contraluz o en sombra dura",
                    equalized.Clone()));
            }

            if (_options.Otsu)
            {
                variants.Add(new PhotoVariant(
                    "otsu",
                    "Umbral global de Otsu; el clasico para un panel con luz uniforme",
                    Threshold(equalized)));
            }

            if (_options.Adaptive)
            {
                variants.Add(new PhotoVariant(
                    "adaptativa",
                    "Umbral adaptativo por vecindario; aguanta media puerta al sol y media a la sombra",
                    AdaptiveThreshold(equalized)));
            }

            if (_options.Inverted)
            {
                variants.Add(new PhotoVariant(
                    "invertida",
                    "Polaridad invertida; el codigo pintado en blanco pasa a ser tinta oscura",
                    Invert(equalized)));
            }

            if (_options.Sharpen)
            {
                variants.Add(new PhotoVariant(
                    "enfocada",
                    "Mascara de enfoque; devuelve el borde a los caracteres de una foto movida",
                    Sharpen(equalized)));
            }

            if (_options.Upscale && photo.Width < _options.UpscaleBelowWidth)
            {
                variants.Add(new PhotoVariant(
                    "ampliada",
                    "Ampliada al doble; el reconocedor necesita un alto minimo de caracter",
                    Upscale(equalized)));
            }

            return new PhotoVariantSet(variants);
        }
        catch
        {
            foreach (var variant in variants)
            {
                variant.Dispose();
            }

            throw;
        }
    }

    private static Mat ToGray(Mat photo)
    {
        if (photo.Channels() == 1)
        {
            return photo.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(photo, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private Mat ApplyClahe(Mat gray)
    {
        var result = new Mat();
        using var clahe = Cv2.CreateCLAHE(_options.ClaheClipLimit, new Size(8, 8));
        clahe.Apply(gray, result);
        return result;
    }

    private static Mat Threshold(Mat gray)
    {
        var result = new Mat();
        Cv2.Threshold(gray, result, 0d, 255d, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // La tinta es siempre la clase minoritaria de la fotografia de un panel: el
        // rotulo ocupa una fraccion pequena de la puerta. Si tras el umbral hay mas
        // pixeles oscuros que claros, lo que quedo en negro es el fondo y hay que
        // invertir para dejar tinta oscura sobre fondo claro, que es lo que espera
        // cualquier OCR.
        if (Cv2.CountNonZero(result) * 2 < result.Rows * result.Cols)
        {
            Cv2.BitwiseNot(result, result);
        }

        return result;
    }

    private static Mat AdaptiveThreshold(Mat gray)
    {
        var result = new Mat();

        // El bloque se dimensiona con la imagen: tiene que ser bastante mas grande
        // que el trazo de un caracter y bastante mas pequeno que el panel, o el
        // umbral local acaba siendo global.
        var block = Math.Max(3, Math.Min(gray.Width, gray.Height) / 16);
        if (block % 2 == 0)
        {
            block++;
        }

        Cv2.AdaptiveThreshold(
            gray, result, 255d, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, block, 12d);

        return result;
    }

    private static Mat Invert(Mat gray)
    {
        var result = new Mat();
        Cv2.BitwiseNot(gray, result);
        return result;
    }

    private static Mat Sharpen(Mat gray)
    {
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(0, 0), 3d);

        var result = new Mat();
        Cv2.AddWeighted(gray, 1.6d, blurred, -0.6d, 0d, result);
        return result;
    }

    private static Mat Upscale(Mat gray)
    {
        var result = new Mat();
        Cv2.Resize(gray, result, new Size(gray.Width * 2, gray.Height * 2), interpolation: InterpolationFlags.Cubic);
        return result;
    }
}

/// <summary>Juego de variantes que se libera en bloque.</summary>
public sealed class PhotoVariantSet(IReadOnlyList<PhotoVariant> variants) : IReadOnlyList<PhotoVariant>, IDisposable
{
    private bool _disposed;

    /// <inheritdoc />
    public PhotoVariant this[int index] => variants[index];

    /// <inheritdoc />
    public int Count => variants.Count;

    /// <inheritdoc />
    public IEnumerator<PhotoVariant> GetEnumerator() => variants.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Las <paramref name="count"/> primeras variantes. El orden en que las genera
    /// la fabrica va de la mas fiel a la mas agresiva, asi que quedarse con las
    /// primeras es quedarse con las que casi siempre bastan.
    /// </summary>
    public IReadOnlyList<PhotoVariant> Take(int count)
        => count >= variants.Count ? variants : variants.Take(Math.Max(1, count)).ToList();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var variant in variants)
        {
            variant.Dispose();
        }
    }
}
