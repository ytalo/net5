using OpenCvSharp;
using Video2BIC.Core.Text;

namespace Video2BIC.Core.Ocr;

/// <summary>
/// Reconocedor de texto sobre un recorte de imagen.
/// </summary>
/// <remarks>
/// La abstraccion permite cambiar de motor sin tocar el resto: un modelo CRNN en
/// ONNX, el binario de Tesseract, un servicio remoto o un doble de prueba con
/// respuestas fijas. Es lo que mantiene el pipeline y las pruebas independientes
/// de que haya o no un modelo entrenado a mano.
/// </remarks>
public interface ITextRecognizer : IDisposable
{
    /// <summary>Descripcion legible del motor, para logs y diagnostico.</summary>
    string Description { get; }

    /// <summary>
    /// <c>true</c> si el motor localiza por su cuenta las lineas de texto dentro de
    /// la imagen que recibe. Si es <c>false</c> hay que pasarle recortes de una
    /// sola linea, previamente localizados.
    /// </summary>
    bool PerformsLayoutAnalysis { get; }

    /// <summary>
    /// Reconoce el texto de una imagen.
    /// </summary>
    /// <param name="image">
    /// Recorte en escala de grises o BGR. La imagen es propiedad del llamante y no
    /// debe conservarse mas alla de la llamada.
    /// </param>
    /// <returns>
    /// Los trozos reconocidos, con posiciones relativas a <paramref name="image"/>.
    /// Lista vacia si no se reconocio nada.
    /// </returns>
    IReadOnlyList<TextFragment> Recognize(Mat image);
}
