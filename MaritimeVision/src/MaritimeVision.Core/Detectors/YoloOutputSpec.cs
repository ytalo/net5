namespace MaritimeVision.Core.Detectors;

/// <summary>Familias de salida que produce una cabeza de deteccion YOLO exportada a ONNX.</summary>
public enum YoloOutputFormat
{
    /// <summary>Se deduce a partir de la forma del tensor y del numero de clases.</summary>
    Auto = 0,

    /// <summary>YOLOv8 / v9 / v11: <c>[1, 4 + clases, anclas]</c>, sin objectness.</summary>
    Yolov8 = 1,

    /// <summary>YOLOv5 / v7: <c>[1, anclas, 5 + clases]</c>, con objectness.</summary>
    Yolov5 = 2,

    /// <summary>Exportado con NMS incorporada: <c>[1, detecciones, 6]</c> = <c>x1 y1 x2 y2 score clase</c>.</summary>
    EndToEnd = 3,
}

/// <summary>
/// Interpretacion concreta del tensor de salida de un modelo YOLO.
/// </summary>
/// <param name="Rows">Numero de anclas (o de detecciones, si la NMS va incorporada).</param>
/// <param name="Features">Valores por ancla.</param>
/// <param name="ChannelsFirst">
/// <c>true</c> si el tensor es <c>[1, features, rows]</c> (disposicion de YOLOv8);
/// <c>false</c> si es <c>[1, rows, features]</c> (disposicion de YOLOv5).
/// </param>
/// <param name="HasObjectness">Si existe la puntuacion de objectness previa a las de clase.</param>
/// <param name="IsEndToEnd">Si el modelo ya aplica NMS y emite cajas finales.</param>
/// <param name="ClassCount">Numero de clases que declara el tensor.</param>
public sealed record YoloOutputSpec(
    int Rows,
    int Features,
    bool ChannelsFirst,
    bool HasObjectness,
    bool IsEndToEnd,
    int ClassCount)
{
    /// <summary>
    /// Deduce la interpretacion del tensor a partir de su forma y del numero de
    /// clases esperado.
    /// </summary>
    /// <param name="dimensions">Forma del tensor, con o sin dimension de lote.</param>
    /// <param name="expectedClassCount">
    /// Clases del conjunto de etiquetas, o 0 si se desconoce. Se usa solo para
    /// desempatar formas ambiguas.
    /// </param>
    /// <param name="format">Formato forzado por configuracion, o <see cref="YoloOutputFormat.Auto"/>.</param>
    /// <exception cref="NotSupportedException">Si la forma del tensor no es interpretable.</exception>
    public static YoloOutputSpec Infer(
        IReadOnlyList<int> dimensions,
        int expectedClassCount,
        YoloOutputFormat format = YoloOutputFormat.Auto)
    {
        ArgumentNullException.ThrowIfNull(dimensions);

        // Se ignora la dimension de lote cuando esta presente y vale 1.
        var shape = dimensions.Count == 3 && dimensions[0] == 1
            ? new[] { dimensions[1], dimensions[2] }
            : dimensions.Count == 2
                ? new[] { dimensions[0], dimensions[1] }
                : null;

        if (shape is null || shape[0] <= 0 || shape[1] <= 0)
        {
            throw new NotSupportedException(
                $"Forma de salida no soportada: [{string.Join(", ", dimensions)}]. " +
                "Se esperaba un tensor de rango 2 o 3 con dimensiones conocidas.");
        }

        var (first, second) = (shape[0], shape[1]);

        switch (format)
        {
            case YoloOutputFormat.Yolov8:
                return FromOrientation(
                    first, second, IsChannelsFirst(first, second, expectedClassCount), hasObjectness: false);

            case YoloOutputFormat.Yolov5:
                return FromOrientation(
                    first, second, IsChannelsFirst(first, second, expectedClassCount), hasObjectness: true);

            case YoloOutputFormat.EndToEnd:
                return new YoloOutputSpec(first, second, ChannelsFirst: false, HasObjectness: false,
                    IsEndToEnd: true, ClassCount: Math.Max(expectedClassCount, 1));
        }

        var channelsFirst = IsChannelsFirst(first, second, expectedClassCount);
        var features = channelsFirst ? first : second;
        var rows = channelsFirst ? second : first;

        // Seis valores por fila en disposicion por filas solo puede ser una salida
        // con NMS incorporada, salvo que el modelo tenga una o dos clases.
        if (!channelsFirst && features == 6 && expectedClassCount > 2)
        {
            return new YoloOutputSpec(rows, features, ChannelsFirst: false, HasObjectness: false,
                IsEndToEnd: true, ClassCount: expectedClassCount);
        }

        if (expectedClassCount > 0 && features - 5 == expectedClassCount)
        {
            return new YoloOutputSpec(rows, features, channelsFirst, HasObjectness: true,
                IsEndToEnd: false, ClassCount: expectedClassCount);
        }

        if (expectedClassCount > 0 && features - 4 == expectedClassCount)
        {
            return new YoloOutputSpec(rows, features, channelsFirst, HasObjectness: false,
                IsEndToEnd: false, ClassCount: expectedClassCount);
        }

        // Ninguna de las dos orientaciones encaja con el numero de clases esperado:
        // el fichero de etiquetas no corresponde al modelo. Aun asi se intenta
        // interpretar la geometria, porque el numero de clases real se puede leer
        // del propio tensor; quien llama decidira si avisa del desajuste.
        if (features <= 4)
        {
            channelsFirst = !channelsFirst;
            features = channelsFirst ? first : second;
            rows = channelsFirst ? second : first;
        }

        if (features <= 4)
        {
            throw new NotSupportedException(
                $"No se puede interpretar una salida de forma [{string.Join(", ", dimensions)}]: " +
                "ninguna de sus dimensiones tiene los 5 valores por ancla que necesita como minimo " +
                "una cabeza de deteccion YOLO.");
        }

        // Sin etiquetas fiables se asume la convencion moderna (sin objectness).
        return FromOrientation(first, second, channelsFirst, hasObjectness: false);
    }

    /// <summary>
    /// Decide cual de las dos dimensiones contiene los valores por ancla.
    /// </summary>
    /// <remarks>
    /// El criterio principal es la coherencia con el numero de clases: la
    /// dimension de features tiene que valer <c>4 + clases</c> o <c>5 + clases</c>.
    /// Solo cuando eso no desempata se recurre a que las anclas son siempre
    /// muchas mas que los features (8400 frente a 84, por ejemplo).
    /// </remarks>
    private static bool IsChannelsFirst(int first, int second, int expectedClassCount)
    {
        var firstFits = FitsClassCount(first, expectedClassCount);
        var secondFits = FitsClassCount(second, expectedClassCount);

        if (firstFits != secondFits)
        {
            return firstFits;
        }

        return first < second;
    }

    private static bool FitsClassCount(int features, int expectedClassCount)
        => expectedClassCount > 0
           && (features - 4 == expectedClassCount || features - 5 == expectedClassCount);

    private static YoloOutputSpec FromOrientation(int first, int second, bool channelsFirst, bool hasObjectness)
    {
        var features = channelsFirst ? first : second;
        var rows = channelsFirst ? second : first;
        var classCount = features - (hasObjectness ? 5 : 4);

        if (classCount <= 0)
        {
            throw new NotSupportedException(
                $"El tensor de salida tiene {features} valores por ancla, insuficientes para el formato indicado.");
        }

        return new YoloOutputSpec(rows, features, channelsFirst, hasObjectness, IsEndToEnd: false, classCount);
    }

    /// <summary>Indice plano del valor <paramref name="feature"/> de la fila <paramref name="row"/>.</summary>
    public int Offset(int row, int feature)
        => ChannelsFirst ? (feature * Rows) + row : (row * Features) + feature;
}
