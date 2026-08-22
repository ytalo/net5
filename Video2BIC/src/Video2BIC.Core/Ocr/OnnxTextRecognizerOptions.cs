using MaritimeVision.Core.Detectors;

namespace Video2BIC.Core.Ocr;

/// <summary>Configuracion de <see cref="OnnxTextRecognizer"/>.</summary>
public sealed class OnnxTextRecognizerOptions
{
    /// <summary>Ruta del modelo de reconocimiento exportado a ONNX. Obligatoria.</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Alfabeto del modelo. Por defecto, los 36 caracteres alfanumericos en
    /// mayusculas, que es lo unico que puede aparecer en un codigo BIC.
    /// </summary>
    public CtcCharset Charset { get; set; } = CtcCharset.Alphanumeric;

    /// <summary>
    /// Alto al que se escala el recorte. Solo se usa si el modelo declara ejes
    /// dinamicos; si el modelo fija el alto, manda el modelo.
    /// </summary>
    public int InputHeight { get; set; } = 32;

    /// <summary>Ancho maximo del recorte escalado, en pixeles.</summary>
    public int MaxInputWidth { get; set; } = 320;

    /// <summary>Ancho minimo, para que un recorte muy estrecho no colapse.</summary>
    public int MinInputWidth { get; set; } = 32;

    /// <summary>Media que se resta a cada canal, tras llevar los valores a [0, 1].</summary>
    public float Mean { get; set; } = 0.5f;

    /// <summary>Desviacion por la que se divide cada canal.</summary>
    public float StdDev { get; set; } = 0.5f;

    /// <summary>
    /// Normalizar la salida con softmax. <c>null</c> lo decide observando los
    /// valores: si ya parecen probabilidades, no se toca.
    /// </summary>
    public bool? ApplySoftmax { get; set; }

    /// <summary>Confianza minima por debajo de la cual se descarta la lectura.</summary>
    public float MinConfidence { get; set; } = 0.3f;

    /// <summary>Proveedor de ejecucion preferido.</summary>
    public ExecutionProvider Provider { get; set; } = ExecutionProvider.Cpu;

    /// <summary>Identificador del dispositivo para los proveedores de GPU.</summary>
    public int DeviceId { get; set; }

    /// <summary>Hilos intra-operacion; 0 deja decidir a ONNX Runtime.</summary>
    public int IntraOpThreads { get; set; }

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun parametro es invalido.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
        {
            throw new InvalidOperationException("Hay que indicar la ruta del modelo de OCR.");
        }

        if (!File.Exists(ModelPath))
        {
            throw new InvalidOperationException($"No se encontro el modelo de OCR '{ModelPath}'.");
        }

        if (InputHeight <= 0)
        {
            throw new InvalidOperationException("InputHeight debe ser positivo.");
        }

        if (MinInputWidth <= 0 || MaxInputWidth < MinInputWidth)
        {
            throw new InvalidOperationException("El rango de anchos de entrada es incoherente.");
        }

        if (Math.Abs(StdDev) < float.Epsilon)
        {
            throw new InvalidOperationException("StdDev no puede ser cero.");
        }

        if (MinConfidence is < 0f or > 1f)
        {
            throw new InvalidOperationException("MinConfidence debe estar en [0, 1].");
        }
    }
}
