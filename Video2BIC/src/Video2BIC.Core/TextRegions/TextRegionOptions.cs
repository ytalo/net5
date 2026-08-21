namespace Video2BIC.Core.TextRegions;

/// <summary>Configuracion de <see cref="MserTextRegionDetector"/>.</summary>
/// <remarks>
/// Todos los limites de tamano son proporciones del recorte, no pixeles: el mismo
/// contenedor ocupa 80 pixeles al fondo del muelle y 900 cuando pasa por delante
/// de la camara, y los umbrales absolutos solo funcionarian a una distancia.
/// </remarks>
public sealed class TextRegionOptions
{
    /// <summary>Alto minimo de un caracter, como fraccion del alto del recorte.</summary>
    public double MinCharacterHeightRatio { get; set; } = 0.03d;

    /// <summary>Alto maximo de un caracter, como fraccion del alto del recorte.</summary>
    public double MaxCharacterHeightRatio { get; set; } = 0.35d;

    /// <summary>
    /// Relacion ancho/alto minima de un caracter. Un <c>1</c> o una <c>I</c> son muy
    /// estrechos; por debajo de esto ya no es un caracter, es una cresta del
    /// corrugado o el canto de un panel.
    /// </summary>
    public double MinCharacterAspect { get; set; } = 0.15d;

    /// <summary>Relacion ancho/alto maxima de un caracter.</summary>
    public double MaxCharacterAspect { get; set; } = 2d;

    /// <summary>
    /// Proporcion minima de la caja que ocupa la region. Una region muy vacia es un
    /// contorno suelto o una sombra, no una letra.
    /// </summary>
    public double MinCharacterFill { get; set; } = 0.15d;

    /// <summary>
    /// Proporcion maxima de la caja que ocupa la region.
    /// </summary>
    /// <remarks>
    /// Es el filtro que mas trabaja contra el corrugado: un trozo de cresta es un
    /// rectangulo macizo y llena mas del 90 % de su caja, mientras que una letra,
    /// por gruesa que sea, deja huecos y ronda la mitad.
    /// </remarks>
    public double MaxCharacterFill { get; set; } = 0.85d;

    /// <summary>Caracteres minimos para dar por buena una linea.</summary>
    /// <remarks>
    /// Con tres se admite el digito de control aislado en su recuadro y el prefijo
    /// de propietario suelto; con menos, cualquier remache en fila seria una linea.
    /// </remarks>
    public int MinCharactersPerLine { get; set; } = 3;

    /// <summary>
    /// Diferencia maxima de altura entre dos caracteres de la misma linea, como
    /// factor. Un rotulo esta escrito con un solo tamano de letra.
    /// </summary>
    public double MaxHeightRatioInLine { get; set; } = 1.8d;

    /// <summary>
    /// Desalineacion vertical maxima entre dos caracteres vecinos, en proporcion a
    /// su altura.
    /// </summary>
    public double MaxVerticalOffsetRatio { get; set; } = 0.6d;

    /// <summary>
    /// Hueco horizontal maximo entre dos caracteres vecinos, en proporcion a su
    /// altura. Da margen para el espacio entre el codigo y el digito de control.
    /// </summary>
    public double MaxGapRatio { get; set; } = 2d;

    /// <summary>
    /// Densidad minima de una linea: proporcion de su caja que ocupan las cajas de
    /// sus caracteres.
    /// </summary>
    /// <remarks>
    /// Una linea de texto real queda practicamente teselada por sus letras y ronda
    /// el 0,8. Un grupo de tres trozos de corrugado que casualmente se alinean deja
    /// casi toda la caja vacia, y es lo que este umbral descarta.
    /// </remarks>
    public double MinLineDensity { get; set; } = 0.25d;

    /// <summary>Margen que se anade a cada linea antes de recortarla, en pixeles.</summary>
    public int Padding { get; set; } = 4;

    /// <summary>Lineas devueltas como maximo, las de mayor densidad de caracteres.</summary>
    public int MaxRegions { get; set; } = 12;

    /// <summary>Candidatos a caracter que se procesan como maximo, para acotar el coste.</summary>
    public int MaxCharacterCandidates { get; set; } = 600;

    /// <summary>Parametro <c>delta</c> de MSER: cuanto mas alto, menos regiones y mas estables.</summary>
    public int Delta { get; set; } = 8;

    /// <summary>
    /// Igualar el histograma por bloques (CLAHE) antes de buscar. Ayuda con el
    /// contraluz y las sombras duras de una terminal a pleno sol.
    /// </summary>
    public bool EnhanceContrast { get; set; } = true;

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun rango es incoherente.</exception>
    public void Validate()
    {
        if (MinCharacterHeightRatio <= 0d
            || MaxCharacterHeightRatio <= MinCharacterHeightRatio
            || MaxCharacterHeightRatio > 1d)
        {
            throw new InvalidOperationException("El rango de alturas de caracter es incoherente.");
        }

        if (MinCharacterAspect <= 0d || MaxCharacterAspect <= MinCharacterAspect)
        {
            throw new InvalidOperationException("El rango de relaciones de aspecto es incoherente.");
        }

        if (MinCharacterFill < 0d || MaxCharacterFill <= MinCharacterFill || MaxCharacterFill > 1d)
        {
            throw new InvalidOperationException("El rango de densidad de caracter es incoherente.");
        }

        if (MinCharactersPerLine < 1)
        {
            throw new InvalidOperationException("MinCharactersPerLine debe ser positivo.");
        }

        if (MaxHeightRatioInLine < 1d)
        {
            throw new InvalidOperationException("MaxHeightRatioInLine no puede ser menor que 1.");
        }

        if (MaxVerticalOffsetRatio <= 0d || MaxGapRatio <= 0d)
        {
            throw new InvalidOperationException("Los margenes de agrupacion deben ser positivos.");
        }

        if (MinLineDensity is < 0d or > 1d)
        {
            throw new InvalidOperationException("MinLineDensity debe estar en [0, 1].");
        }

        if (MaxRegions < 1)
        {
            throw new InvalidOperationException("MaxRegions debe ser positivo.");
        }

        if (MaxCharacterCandidates < 1)
        {
            throw new InvalidOperationException("MaxCharacterCandidates debe ser positivo.");
        }

        if (Padding < 0)
        {
            throw new InvalidOperationException("Padding no puede ser negativo.");
        }

        if (Delta < 1)
        {
            throw new InvalidOperationException("Delta debe ser positivo.");
        }
    }
}
