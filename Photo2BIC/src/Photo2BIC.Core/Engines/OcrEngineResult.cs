using Video2BIC.Core.Text;

namespace Photo2BIC.Core.Engines;

/// <summary>Como termino la llamada a un motor de reconocimiento.</summary>
public enum OcrEngineStatus
{
    /// <summary>El motor respondio, con o sin texto.</summary>
    Ok = 0,

    /// <summary>
    /// El motor no esta configurado en esta maquina: falta el binario, el modelo o
    /// las credenciales. No es un error de ejecucion, es una capacidad ausente.
    /// </summary>
    Unavailable = 1,

    /// <summary>El motor estaba disponible pero la llamada fallo.</summary>
    Failed = 2,

    /// <summary>No se llego a llamar: presupuesto agotado, cancelacion o filtro del usuario.</summary>
    Skipped = 3,
}

/// <summary>
/// Lo que un motor leyo en una variante de la fotografia.
/// </summary>
/// <param name="Engine">Nombre del motor, tal y como se escribe en <c>--engines</c>.</param>
/// <param name="Family">
/// Familia tecnologica del motor. Dos motores de la misma familia comparten los
/// aciertos y tambien los errores, asi que el consenso no puede contarlos como
/// evidencias independientes.
/// </param>
/// <param name="Variant">Variante de preprocesado sobre la que se ejecuto.</param>
/// <param name="Fragments">Trozos de texto reconocidos, en orden de lectura.</param>
/// <param name="Duration">Tiempo que tardo la llamada.</param>
/// <param name="Status">Resultado de la llamada.</param>
/// <param name="Error">Motivo, cuando el estado no es <see cref="OcrEngineStatus.Ok"/>.</param>
public sealed record OcrEngineResult(
    string Engine,
    string Family,
    string Variant,
    IReadOnlyList<TextFragment> Fragments,
    TimeSpan Duration,
    OcrEngineStatus Status = OcrEngineStatus.Ok,
    string? Error = null)
{
    /// <summary>Resultado correcto con los trozos indicados.</summary>
    public static OcrEngineResult Success(
        IPhotoOcrEngine engine, string variant, IReadOnlyList<TextFragment> fragments, TimeSpan duration)
        => new(engine.Name, engine.Family, variant, fragments, duration);

    /// <summary>El motor no esta configurado en esta maquina.</summary>
    public static OcrEngineResult Unavailable(IPhotoOcrEngine engine, string variant, string reason)
        => new(engine.Name, engine.Family, variant, [], TimeSpan.Zero, OcrEngineStatus.Unavailable, reason);

    /// <summary>La llamada fallo.</summary>
    public static OcrEngineResult Failed(IPhotoOcrEngine engine, string variant, string reason, TimeSpan duration)
        => new(engine.Name, engine.Family, variant, [], duration, OcrEngineStatus.Failed, reason);

    /// <summary>Todo el texto reconocido, unido en una sola cadena legible.</summary>
    public string JoinedText => string.Join(' ', Fragments.Select(fragment => fragment.Text.Trim()));

    /// <summary>Confianza media de los trozos, ponderada por su longitud.</summary>
    public float MeanConfidence
    {
        get
        {
            var characters = 0;
            var weighted = 0d;

            foreach (var fragment in Fragments)
            {
                var length = fragment.Normalized.Length;
                if (length == 0)
                {
                    continue;
                }

                weighted += fragment.Confidence * length;
                characters += length;
            }

            return characters == 0 ? 0f : (float)(weighted / characters);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Status switch
    {
        OcrEngineStatus.Ok => $"{Engine}/{Variant}: '{JoinedText}' ({Duration.TotalMilliseconds:F0} ms)",
        _ => $"{Engine}/{Variant}: {Status} - {Error}",
    };
}
