using Video2BIC.Core.Imaging;

namespace Video2BIC.Core.Recognition;

/// <summary>Configuracion de <see cref="ContainerBicRecognizer"/>.</summary>
public sealed class BicRecognizerOptions
{
    /// <summary>
    /// Margen anadido alrededor de la caja del detector, en proporcion a su tamano.
    /// </summary>
    public double BoxMargin { get; set; } = 0.03d;

    /// <summary>
    /// Ancho minimo de la caja del contenedor, en pixeles, para molestarse en leer.
    /// Por debajo de esto los caracteres miden dos o tres pixeles y no hay OCR que
    /// valga.
    /// </summary>
    public int MinBoxWidth { get; set; } = 120;

    /// <summary>Alto minimo de la caja del contenedor, en pixeles.</summary>
    public int MinBoxHeight { get; set; } = 80;

    /// <summary>Alto al que se amplia el recorte del contenedor antes de buscar texto.</summary>
    public int RoiHeight { get; set; } = 480;

    /// <summary>Ampliacion maxima del recorte.</summary>
    public double MaxRoiScale { get; set; } = 4d;

    /// <summary>
    /// Numero maximo de regiones de texto que se reconocen por lectura. Limita el
    /// coste cuando el contenedor esta lleno de rotulos.
    /// </summary>
    public int MaxRegionsPerRead { get; set; } = 8;

    /// <summary>
    /// Usar el localizador de regiones aunque el motor de OCR haga su propio
    /// analisis de disposicion. Cuesta mas, pero recorta las lineas antes de
    /// reconocerlas y eso mejora bastante los rotulos pequenos.
    /// </summary>
    public bool AlwaysUseRegionDetector { get; set; }

    /// <summary>Preprocesado aplicado a cada recorte de linea.</summary>
    public TextPreprocessOptions Preprocess { get; init; } = new();

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun parametro es imposible.</exception>
    public void Validate()
    {
        if (BoxMargin is < 0d or > 1d)
        {
            throw new InvalidOperationException("BoxMargin debe estar en [0, 1].");
        }

        if (MinBoxWidth <= 0 || MinBoxHeight <= 0)
        {
            throw new InvalidOperationException("Los tamanos minimos de caja deben ser positivos.");
        }

        if (RoiHeight <= 0)
        {
            throw new InvalidOperationException("RoiHeight debe ser positivo.");
        }

        if (MaxRoiScale < 1d)
        {
            throw new InvalidOperationException("MaxRoiScale no puede ser menor que 1.");
        }

        if (MaxRegionsPerRead < 1)
        {
            throw new InvalidOperationException("MaxRegionsPerRead debe ser positivo.");
        }
    }
}
