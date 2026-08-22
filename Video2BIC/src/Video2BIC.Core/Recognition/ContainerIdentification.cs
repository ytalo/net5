using Video2BIC.Core.Bic;

namespace Video2BIC.Core.Recognition;

/// <summary>
/// El resultado que persigue la aplicacion: un contenedor visto en el video, con
/// su codigo BIC y la evidencia que lo sostiene.
/// </summary>
/// <param name="TrackId">Identidad del contenedor durante el seguimiento.</param>
/// <param name="Code">Codigo BIC atribuido.</param>
/// <param name="Confidence">Confianza agregada de todas las lecturas, en [0, 1].</param>
/// <param name="Observations">Lecturas que coincidieron en este codigo.</param>
/// <param name="CompetingCodes">Codigos distintos que llegaron a proponerse para el mismo contenedor.</param>
/// <param name="FirstFrame">Fotograma de la primera lectura coincidente.</param>
/// <param name="LastFrame">Fotograma de la ultima.</param>
/// <param name="FirstSeen">Momento de la primera lectura dentro de la secuencia.</param>
/// <param name="LastSeen">Momento de la ultima.</param>
/// <param name="SizeType">Codigo de tamano y tipo, si se leyo.</param>
/// <param name="IsConfirmed">
/// <c>true</c> cuando la evidencia acumulada supera el umbral configurado. Un
/// resultado sin confirmar es una hipotesis, no una identificacion.
/// </param>
public sealed record ContainerIdentification(
    int TrackId,
    BicCode Code,
    float Confidence,
    int Observations,
    int CompetingCodes,
    int FirstFrame,
    int LastFrame,
    TimeSpan FirstSeen,
    TimeSpan LastSeen,
    SizeTypeCode? SizeType,
    bool IsConfirmed)
{
    /// <summary>Fotogramas transcurridos entre la primera y la ultima lectura.</summary>
    public int FrameSpan => Math.Max(0, LastFrame - FirstFrame);

    /// <summary>Resumen de una linea, para consola o para el panel del video.</summary>
    public string ToDisplayString()
    {
        var state = IsConfirmed ? "confirmado" : "provisional";
        var size = SizeType is null ? string.Empty : $" [{SizeType.Value}]";
        return $"#{TrackId} {Code.Value}{size} {Confidence:P0} ({Observations} lecturas, {state})";
    }

    /// <inheritdoc />
    public override string ToString() => ToDisplayString();
}
