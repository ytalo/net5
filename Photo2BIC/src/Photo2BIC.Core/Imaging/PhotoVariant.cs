using OpenCvSharp;

namespace Photo2BIC.Core.Imaging;

/// <summary>
/// Una version preprocesada de la fotografia, lista para pasarsela a un motor.
/// </summary>
/// <remarks>
/// Es duena de su <see cref="Image"/>: al liberarla se libera la matriz. La
/// variante <c>original</c> que produce <see cref="PhotoVariantFactory"/> es una
/// copia, no la imagen del llamante, para que el juego completo se pueda liberar
/// de una vez sin sorpresas.
/// </remarks>
public sealed class PhotoVariant(string name, string description, Mat image) : IDisposable
{
    private bool _disposed;

    /// <summary>Identificador corto: <c>original</c>, <c>clahe</c>, <c>otsu</c>...</summary>
    public string Name { get; } = name;

    /// <summary>Que se le hizo a la imagen y por que ayuda.</summary>
    public string Description { get; } = description;

    /// <summary>La imagen preprocesada.</summary>
    public Mat Image { get; } = image;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Image.Dispose();
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Image.Width}x{Image.Height})";
}
