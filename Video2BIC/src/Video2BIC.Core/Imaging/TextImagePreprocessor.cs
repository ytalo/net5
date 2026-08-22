using OpenCvSharp;

namespace Video2BIC.Core.Imaging;

/// <summary>Opciones de <see cref="TextImagePreprocessor"/>.</summary>
public sealed class TextPreprocessOptions
{
    /// <summary>
    /// Alto minimo del recorte de linea, en pixeles. Una linea mas baja se amplia
    /// hasta aqui; 0 desactiva la ampliacion.
    /// </summary>
    /// <remarks>
    /// Es un minimo y no un objetivo a proposito. Reducir una linea que ya viene
    /// grande solo tira informacion: el recorte de un contenedor cercano llega con
    /// caracteres de cien pixeles y encogerlos a la altura "estandar" es la forma
    /// mas rapida de convertir una lectura facil en una imposible.
    /// </remarks>
    public int MinHeight { get; set; } = 64;

    /// <summary>
    /// Alto maximo del recorte de linea, en pixeles. Una linea mas alta se reduce
    /// hasta aqui; 0 no impone limite.
    /// </summary>
    public int MaxHeight { get; set; }

    /// <summary>Igualar el histograma por bloques (CLAHE).</summary>
    public bool EnhanceContrast { get; set; } = true;

    /// <summary>
    /// Binarizar con umbral adaptativo. Ayuda a Tesseract; a un modelo CRNN
    /// entrenado sobre fotografias suele perjudicarle, porque le llega una imagen
    /// que no se parece a las de su entrenamiento.
    /// </summary>
    public bool Binarize { get; set; }

    /// <summary>
    /// Dejar siempre texto oscuro sobre fondo claro, invirtiendo si hace falta.
    /// El codigo de un contenedor esta pintado en blanco tan a menudo como en negro.
    /// </summary>
    public bool NormalizePolarity { get; set; } = true;

    /// <summary>Corregir la inclinacion de la linea, hasta el limite indicado.</summary>
    public bool Deskew { get; set; } = true;

    /// <summary>Inclinacion maxima que se corrige, en grados.</summary>
    public double MaxSkewDegrees { get; set; } = 12d;
}

/// <summary>
/// Deja un recorte de linea en las condiciones en las que un OCR acierta:
/// escala de grises, contraste igualado, texto oscuro sobre fondo claro y
/// horizontal.
/// </summary>
public static class TextImagePreprocessor
{
    /// <summary>
    /// Prepara el recorte.
    /// </summary>
    /// <param name="image">Recorte de una linea, BGR o en escala de grises.</param>
    /// <param name="options">Configuracion; <c>null</c> usa la de por defecto.</param>
    /// <returns>Un <see cref="Mat"/> nuevo de un canal, propiedad del llamante.</returns>
    public static Mat Prepare(Mat image, TextPreprocessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new TextPreprocessOptions();

        if (image.Empty())
        {
            return new Mat();
        }

        var gray = new Mat();
        try
        {
            if (image.Channels() == 1)
            {
                image.CopyTo(gray);
            }
            else
            {
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            }

            var height = ResolveHeight(gray.Height, options);
            if (height != gray.Height)
            {
                var scale = (double)height / gray.Height;
                Cv2.Resize(
                    gray,
                    gray,
                    new Size(Math.Max(1, (int)Math.Round(gray.Width * scale)), height),
                    interpolation: scale > 1d ? InterpolationFlags.Cubic : InterpolationFlags.Area);
            }

            if (options.EnhanceContrast)
            {
                using var clahe = Cv2.CreateCLAHE(clipLimit: 3d, tileGridSize: new Size(8, 8));
                clahe.Apply(gray, gray);
            }

            if (options.Deskew)
            {
                Deskew(gray, options.MaxSkewDegrees);
            }

            if (options.NormalizePolarity && IsLightTextOnDarkBackground(gray))
            {
                Cv2.BitwiseNot(gray, gray);
            }

            if (options.Binarize)
            {
                Cv2.AdaptiveThreshold(
                    gray,
                    gray,
                    255d,
                    AdaptiveThresholdTypes.GaussianC,
                    ThresholdTypes.Binary,
                    blockSize: OddBlockSize(gray.Height),
                    c: 12d);
            }

            return gray;
        }
        catch
        {
            gray.Dispose();
            throw;
        }
    }

    /// <summary>Alto al que hay que llevar la linea, o el suyo propio si ya sirve.</summary>
    private static int ResolveHeight(int height, TextPreprocessOptions options)
    {
        if (height <= 0)
        {
            return height;
        }

        if (options.MinHeight > 0 && height < options.MinHeight)
        {
            return options.MinHeight;
        }

        if (options.MaxHeight > 0 && height > options.MaxHeight)
        {
            return options.MaxHeight;
        }

        return height;
    }

    /// <summary>
    /// Decide si el recorte tiene texto claro sobre fondo oscuro.
    /// </summary>
    /// <remarks>
    /// El criterio es que la tinta es siempre la clase minoritaria de un recorte de
    /// linea: un rotulo ocupa una fraccion de su caja, nunca la mayoria. Se separan
    /// las dos clases con Otsu y se mira cual tiene menos pixeles; si es la clara,
    /// el texto es claro. Comparar el centro con el borde, que es lo inmediato, no
    /// sirve aqui: el recorte va ajustado a la linea y el borde tambien es texto.
    /// </remarks>
    internal static bool IsLightTextOnDarkBackground(Mat gray)
    {
        if (gray.Empty())
        {
            return false;
        }

        using var binary = new Mat();
        Cv2.Threshold(gray, binary, 0d, 255d, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        var bright = Cv2.CountNonZero(binary);
        var total = gray.Rows * gray.Cols;

        return bright * 2 < total;
    }

    /// <summary>
    /// Endereza la linea rotandola sobre su centro segun el angulo del rectangulo
    /// minimo que envuelve a los pixeles de trazo.
    /// </summary>
    private static void Deskew(Mat gray, double maxDegrees)
    {
        using var binary = new Mat();
        Cv2.Threshold(gray, binary, 0d, 255d, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // El rectangulo minimo se calcula sobre los pixeles claros; si el texto es
        // oscuro sobre fondo claro hay que mirar el negativo.
        if (Cv2.CountNonZero(binary) > binary.Rows * binary.Cols / 2)
        {
            Cv2.BitwiseNot(binary, binary);
        }

        using var points = new Mat();
        Cv2.FindNonZero(binary, points);

        if (points.Empty() || points.Rows < 12)
        {
            return;
        }

        var rotated = Cv2.MinAreaRect(points);
        var angle = (double)rotated.Angle;

        // El angulo llega en [0, 90) con OpenCV 4.5 o posterior y en (-90, 0] con las
        // versiones anteriores. Llevarlo a (-45, 45] vale para las dos y deja el
        // valor comparable con el limite de inclinacion.
        if (angle > 45d)
        {
            angle -= 90d;
        }
        else if (angle < -45d)
        {
            angle += 90d;
        }

        if (Math.Abs(angle) < 0.5d || Math.Abs(angle) > maxDegrees)
        {
            return;
        }

        var center = new Point2f(gray.Width / 2f, gray.Height / 2f);
        using var rotation = Cv2.GetRotationMatrix2D(center, angle, 1d);
        Cv2.WarpAffine(
            gray,
            gray,
            rotation,
            gray.Size(),
            InterpolationFlags.Cubic,
            BorderTypes.Replicate);
    }

    private static int OddBlockSize(int height)
    {
        var size = Math.Max(3, height / 2);
        return size % 2 == 0 ? size + 1 : size;
    }
}
