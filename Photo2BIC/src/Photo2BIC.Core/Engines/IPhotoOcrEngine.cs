using OpenCvSharp;

namespace Photo2BIC.Core.Engines;

/// <summary>Donde se ejecuta el reconocimiento.</summary>
public enum OcrEngineKind
{
    /// <summary>En la propia maquina: un binario instalado o un modelo cargado en memoria.</summary>
    Local = 0,

    /// <summary>En un servicio remoto, al que hay que enviarle la fotografia.</summary>
    Cloud = 1,
}

/// <summary>
/// Un motor de reconocimiento de texto sobre una fotografia completa.
/// </summary>
/// <remarks>
/// <para>
/// Es una abstraccion mas alta que <c>ITextRecognizer</c> de Video2BIC, que lee
/// un recorte de una sola linea ya localizada. Aqui la entrada es la fotografia
/// entera y cada motor resuelve a su manera el problema completo: Tesseract
/// necesita que se le localicen las lineas antes, un modelo CRNN tambien, y los
/// servicios de nube hacen deteccion y reconocimiento en la misma llamada. Esa
/// diferencia es justo lo que se quiere poder comparar.
/// </para>
/// <para>
/// La operacion es asincrona porque la mitad de las implementaciones son
/// llamadas de red. Los motores locales devuelven una tarea ya completada; el
/// coste de envolver trabajo sincrono es despreciable al lado de un OCR.
/// </para>
/// </remarks>
public interface IPhotoOcrEngine : IDisposable
{
    /// <summary>
    /// Identificador corto y estable, en minusculas: es lo que el usuario escribe
    /// en <c>--engines tesseract-lines,azure</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Familia tecnologica a la que pertenece.
    /// </summary>
    /// <remarks>
    /// Dos motores de la misma familia -Tesseract por lineas y Tesseract sobre el
    /// panel entero, por ejemplo- se equivocan en las mismas letras por las mismas
    /// razones. El consenso los agrupa por familia para no confundir «dos motores
    /// coinciden» con «el mismo motor ha dicho lo mismo dos veces».
    /// </remarks>
    string Family { get; }

    /// <summary>Descripcion legible, para el informe y el diagnostico.</summary>
    string Description { get; }

    /// <summary>Local o remoto.</summary>
    OcrEngineKind Kind { get; }

    /// <summary>
    /// Numero maximo de variantes de preprocesado que conviene enviarle.
    /// </summary>
    /// <remarks>
    /// Un motor local se puede ejecutar sobre todas las variantes que haga falta,
    /// porque solo cuesta tiempo de CPU. Un servicio remoto se factura por llamada
    /// y ademas ya hace su propio preprocesado, asi que declara 1: se le manda la
    /// fotografia una vez y se acabo.
    /// </remarks>
    int MaxVariants { get; }

    /// <summary>
    /// Comprueba si el motor puede funcionar en esta maquina.
    /// </summary>
    /// <param name="reason">
    /// Que falta, cuando no esta disponible: el binario, el modelo o la variable de
    /// entorno con la credencial. El mensaje se le ensena al usuario tal cual, asi
    /// que dice como resolverlo.
    /// </param>
    bool IsAvailable(out string reason);

    /// <summary>
    /// Reconoce el texto de una fotografia.
    /// </summary>
    /// <param name="photo">
    /// Imagen BGR o en escala de grises. Es propiedad del llamante y no debe
    /// conservarse mas alla de la llamada.
    /// </param>
    /// <param name="variant">
    /// Nombre de la variante de preprocesado que se esta reconociendo, para poder
    /// atribuir la lectura en el informe.
    /// </param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>
    /// El resultado, incluso cuando la llamada falla: un motor caido no debe tumbar
    /// el analisis, solo restar una evidencia.
    /// </returns>
    Task<OcrEngineResult> RecognizeAsync(Mat photo, string variant, CancellationToken cancellationToken = default);
}
