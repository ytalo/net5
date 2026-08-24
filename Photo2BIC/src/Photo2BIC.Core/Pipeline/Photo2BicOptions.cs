using Photo2BIC.Core.Fusion;
using Photo2BIC.Core.Imaging;
using Video2BIC.Core.Bic;

namespace Photo2BIC.Core.Pipeline;

/// <summary>Configuracion de <see cref="PhotoBicReader"/>.</summary>
public sealed class Photo2BicOptions
{
    /// <summary>
    /// Ancho maximo al que se reduce la fotografia antes de analizarla.
    /// </summary>
    /// <remarks>
    /// Mil seiscientos pixeles bastan de sobra: el rotulo de un contenedor
    /// fotografiado de cerca deja caracteres de mas de cien pixeles, y a partir de
    /// treinta ya no mejora el reconocimiento. Lo que si crece con la resolucion es
    /// el coste de cada motor y el tamano de cada peticion a los servicios.
    /// </remarks>
    public int MaxWidth { get; set; } = 1600;

    /// <summary>Variantes de preprocesado que se generan.</summary>
    public PhotoVariantOptions Variants { get; } = new();

    /// <summary>
    /// Variantes que se le pasan como maximo a un motor local. Cada motor recorta
    /// ademas por su propio <c>MaxVariants</c>.
    /// </summary>
    public int MaxVariantsPerEngine { get; set; } = 4;

    /// <summary>Consenso entre motores.</summary>
    public ConsensusOptions Consensus { get; } = new();

    /// <summary>Analisis de codigos sobre el texto reconocido.</summary>
    public BicParserOptions Parser { get; } = new();

    /// <summary>
    /// Motores que se ejecutan a la vez.
    /// </summary>
    /// <remarks>
    /// Lo que se gana aqui es sobre todo solapar la latencia de los servicios
    /// remotos, que es tiempo de espera y no de CPU. Cada motor local serializa
    /// internamente sus propias llamadas, asi que subir esto no los hace competir
    /// entre si por los nucleos mas alla de uno por motor.
    /// </remarks>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Tiempo maximo del analisis completo de una fotografia. <c>Zero</c> no impone
    /// limite.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2d);

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun valor es imposible.</exception>
    public void Validate()
    {
        if (MaxWidth < 0)
        {
            throw new InvalidOperationException("MaxWidth no puede ser negativo.");
        }

        if (MaxVariantsPerEngine < 1)
        {
            throw new InvalidOperationException("MaxVariantsPerEngine debe ser positivo.");
        }

        if (MaxConcurrency < 1)
        {
            throw new InvalidOperationException("MaxConcurrency debe ser positivo.");
        }

        if (Timeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Timeout no puede ser negativo.");
        }

        Variants.Validate();
        Consensus.Validate();
        Parser.Validate();
    }
}
