namespace Video2BIC.Core.Text;

/// <summary>
/// Un trozo de texto reconocido en la imagen, con su confianza y su posicion
/// relativa dentro del recorte del contenedor.
/// </summary>
/// <param name="Text">Texto tal cual lo devolvio el reconocedor.</param>
/// <param name="Confidence">Confianza en [0, 1]; 1 si el reconocedor no la aporta.</param>
/// <param name="Left">Borde izquierdo dentro del recorte, en pixeles.</param>
/// <param name="Top">Borde superior dentro del recorte, en pixeles.</param>
/// <param name="Width">Ancho de la region, en pixeles.</param>
/// <param name="Height">Alto de la region, en pixeles.</param>
/// <remarks>
/// La posicion importa: en la puerta de un contenedor el codigo aparece muchas
/// veces repartido en varias lineas (<c>MSKU</c> / <c>123456</c> / <c>7</c>), y
/// solo se puede recomponer si se conoce el orden de lectura.
/// </remarks>
public readonly record struct TextFragment(
    string Text,
    float Confidence,
    int Left = 0,
    int Top = 0,
    int Width = 0,
    int Height = 0)
{
    /// <summary>
    /// Texto en mayusculas y sin nada que no sea <c>A-Z</c> o <c>0-9</c>: la forma
    /// en la que el analizador de codigos busca.
    /// </summary>
    public string Normalized
    {
        get
        {
            if (string.IsNullOrEmpty(Text))
            {
                return string.Empty;
            }

            var buffer = new char[Text.Length];
            var length = 0;

            foreach (var character in Text)
            {
                var upper = char.ToUpperInvariant(character);
                if (char.IsAsciiLetterUpper(upper) || char.IsAsciiDigit(upper))
                {
                    buffer[length++] = upper;
                }
            }

            return new string(buffer, 0, length);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"'{Text}' {Confidence:P0} @({Left},{Top})";
}
