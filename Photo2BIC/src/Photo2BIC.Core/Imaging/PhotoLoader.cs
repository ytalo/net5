using OpenCvSharp;

namespace Photo2BIC.Core.Imaging;

/// <summary>
/// Carga la fotografia del disco y la deja en un tamano razonable.
/// </summary>
/// <remarks>
/// Un movil actual entrega imagenes de 4000 pixeles de ancho. Eso no mejora el
/// OCR -el rotulo de un contenedor ya se lee con holgura a 1600- y en cambio
/// multiplica por seis el coste de cada motor y roza los limites de tamano de
/// los servicios de nube. Reducir de entrada es la optimizacion mas barata del
/// recorrido.
/// </remarks>
public static class PhotoLoader
{
    /// <summary>Extensiones que se consideran fotografias al recorrer una carpeta.</summary>
    public static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"];

    /// <summary>
    /// Carga una fotografia.
    /// </summary>
    /// <param name="path">Ruta del fichero.</param>
    /// <param name="maxWidth">
    /// Ancho maximo; una imagen mas ancha se reduce conservando la proporcion.
    /// 0 la deja como esta.
    /// </param>
    /// <returns>Un <see cref="Mat"/> BGR, propiedad del llamante.</returns>
    /// <exception cref="FileNotFoundException">Si el fichero no existe.</exception>
    /// <exception cref="InvalidOperationException">Si el fichero no es una imagen legible.</exception>
    public static Mat Load(string path, int maxWidth = 1600)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No se encontro la fotografia '{path}'.", path);
        }

        // ImreadModes.Color aplica la orientacion EXIF, que es imprescindible: una
        // foto hecha con el movil en vertical llega girada 90 grados y ningun OCR
        // lee texto tumbado.
        var photo = Cv2.ImRead(path, ImreadModes.Color);

        if (photo.Empty())
        {
            photo.Dispose();
            throw new InvalidOperationException(
                $"'{path}' no se pudo leer como imagen. Formatos admitidos: " +
                $"{string.Join(", ", SupportedExtensions)}.");
        }

        return Downscale(photo, maxWidth);
    }

    /// <summary>
    /// Reduce la imagen si supera el ancho indicado, liberando la original.
    /// </summary>
    /// <returns>La misma imagen si ya cabia, o una nueva reducida.</returns>
    public static Mat Downscale(Mat photo, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(photo);

        if (maxWidth <= 0 || photo.Width <= maxWidth || photo.Empty())
        {
            return photo;
        }

        var scale = (double)maxWidth / photo.Width;
        var reduced = new Mat();

        try
        {
            Cv2.Resize(
                photo,
                reduced,
                new Size(maxWidth, Math.Max(1, (int)Math.Round(photo.Height * scale))),
                interpolation: InterpolationFlags.Area);
        }
        catch
        {
            reduced.Dispose();
            throw;
        }

        photo.Dispose();
        return reduced;
    }

    /// <summary>
    /// Codifica la imagen para enviarla a un servicio remoto.
    /// </summary>
    /// <param name="image">Imagen a codificar.</param>
    /// <param name="quality">Calidad JPEG, en [1, 100].</param>
    /// <remarks>
    /// JPEG y no PNG: la fotografia de un contenedor es una escena natural, donde
    /// PNG apenas comprime y multiplicaria por diez el cuerpo de cada peticion. La
    /// calidad por defecto es alta a proposito -los artefactos de bloque alrededor
    /// de un caracter son exactamente el tipo de ruido que confunde a un OCR-.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Si la codificacion falla.</exception>
    public static byte[] EncodeJpeg(Mat image, int quality = 92)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Empty())
        {
            throw new InvalidOperationException("No se puede codificar una imagen vacia.");
        }

        // Los servicios esperan una imagen normal: un solo canal es valido en JPEG,
        // pero varios lo interpretan como escala de grises con perfil raro. Se envia
        // siempre en tres canales.
        using var bgr = image.Channels() == 1 ? image.CvtColor(ColorConversionCodes.GRAY2BGR) : image.Clone();

        if (!Cv2.ImEncode(".jpg", bgr, out var bytes, [(int)ImwriteFlags.JpegQuality, Math.Clamp(quality, 1, 100)]))
        {
            throw new InvalidOperationException("No se pudo codificar la fotografia en JPEG.");
        }

        return bytes;
    }

    /// <summary>Fotografias de una carpeta, ordenadas por nombre.</summary>
    /// <param name="directory">Carpeta a recorrer.</param>
    /// <param name="recursive">Incluir subcarpetas.</param>
    /// <exception cref="DirectoryNotFoundException">Si la carpeta no existe.</exception>
    public static IReadOnlyList<string> Enumerate(string directory, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"No se encontro la carpeta '{directory}'.");
        }

        var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        return Directory.EnumerateFiles(directory, "*", search)
            .Where(file => SupportedExtensions.Contains(
                Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
    }
}
