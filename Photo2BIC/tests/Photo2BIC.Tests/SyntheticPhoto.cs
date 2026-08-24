using OpenCvSharp;
using Photo2BIC.Core.Imaging;

namespace Photo2BIC.Tests;

/// <summary>
/// Atajo a <see cref="SyntheticDoor"/>, que es donde vive de verdad la escena de
/// prueba: la genera tambien <c>photo2bic sample</c>, para que se pueda probar la
/// aplicacion sin conseguir ninguna fotografia.
/// </summary>
public static class SyntheticPhoto
{
    /// <inheritdoc cref="SyntheticDoor.Draw" />
    public static Mat Door(
        string code,
        string? sizeType = "22G1",
        int width = 900,
        int height = 600,
        bool lightTextOnDarkPanel = true)
        => SyntheticDoor.Draw(code, sizeType, width, height, lightTextOnDarkPanel);

    /// <inheritdoc cref="SyntheticDoor.Write" />
    public static string Write(string path, string code, string? sizeType = "22G1")
        => SyntheticDoor.Write(path, code, sizeType);
}
