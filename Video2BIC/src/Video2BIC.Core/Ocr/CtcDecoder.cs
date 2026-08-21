namespace Video2BIC.Core.Ocr;

/// <summary>Texto obtenido al decodificar la salida de un modelo CTC.</summary>
/// <param name="Text">Cadena decodificada.</param>
/// <param name="Confidence">Media de la probabilidad de los caracteres emitidos, en [0, 1].</param>
public readonly record struct CtcDecoding(string Text, float Confidence);

/// <summary>
/// Decodificador voraz de una salida CTC.
/// </summary>
/// <remarks>
/// <para>
/// Un modelo CTC no emite una cadena, sino una distribucion de probabilidad por
/// cada franja vertical de la imagen. Decodificarla consiste en quedarse con la
/// clase mas probable de cada franja, colapsar las repeticiones consecutivas y
/// tirar la clase en blanco, que es la que el modelo usa para separar dos
/// caracteres iguales seguidos.
/// </para>
/// <para>
/// La busqueda voraz basta aqui. Un <i>beam search</i> ayuda cuando hay un modelo
/// de lenguaje detras; en un codigo BIC el «modelo de lenguaje» es el digito de
/// control, que se aplica despues y es mucho mas restrictivo.
/// </para>
/// </remarks>
public static class CtcDecoder
{
    /// <summary>
    /// Decodifica una matriz de logits o probabilidades en disposicion
    /// <c>[franjas, clases]</c>.
    /// </summary>
    /// <param name="scores">
    /// Valores en fila mayor: la franja <c>t</c> ocupa
    /// <c>scores[t * classCount .. (t + 1) * classCount]</c>.
    /// </param>
    /// <param name="classCount">Numero de clases por franja.</param>
    /// <param name="charset">Alfabeto con el que traducir cada clase.</param>
    /// <param name="applySoftmax">
    /// Normalizar cada franja con softmax. Si es <c>null</c> se decide por el
    /// contenido: se aplica cuando los valores no parecen probabilidades.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Si las dimensiones no encajan o el alfabeto no tiene el numero de clases
    /// que emite el modelo.
    /// </exception>
    public static CtcDecoding Decode(
        ReadOnlySpan<float> scores,
        int classCount,
        CtcCharset charset,
        bool? applySoftmax = null)
    {
        ArgumentNullException.ThrowIfNull(charset);

        if (classCount <= 1)
        {
            throw new ArgumentException("Un modelo CTC emite al menos dos clases.", nameof(classCount));
        }

        if (scores.Length % classCount != 0)
        {
            throw new ArgumentException(
                $"La salida ({scores.Length} valores) no es multiplo de {classCount} clases.", nameof(scores));
        }

        if (classCount != charset.ClassCount)
        {
            throw new ArgumentException(
                $"El modelo emite {classCount} clases y el alfabeto define {charset.ClassCount} " +
                $"({charset.Characters.Length} caracteres mas el blanco).",
                nameof(charset));
        }

        var steps = scores.Length / classCount;
        var text = new List<char>(steps);
        var probabilitySum = 0d;
        var emitted = 0;
        var previousClass = -1;

        for (var step = 0; step < steps; step++)
        {
            var window = scores.Slice(step * classCount, classCount);
            var bestClass = ArgMax(window);

            var isProbability = applySoftmax is { } forced
                ? !forced
                : LooksLikeProbabilities(window);

            var probability = isProbability
                ? Math.Clamp(window[bestClass], 0f, 1f)
                : Softmax(window, bestClass);

            // La regla del CTC: se emite un caracter cuando la clase cambia y no es
            // el blanco. Repetirla significa que el mismo caracter se extiende sobre
            // varias franjas, no que aparezca dos veces.
            if (bestClass != previousClass && bestClass != charset.BlankIndex)
            {
                var character = charset[bestClass];
                if (character != '\0')
                {
                    text.Add(character);
                    probabilitySum += probability;
                    emitted++;
                }
            }

            previousClass = bestClass;
        }

        var confidence = emitted > 0 ? (float)(probabilitySum / emitted) : 0f;
        return new CtcDecoding(new string(text.ToArray()), confidence);
    }

    /// <summary>
    /// Decodifica una salida en disposicion <c>[clases, franjas]</c>, la que emiten
    /// algunos modelos exportados desde PyTorch sin transponer.
    /// </summary>
    public static CtcDecoding DecodeTransposed(
        ReadOnlySpan<float> scores,
        int classCount,
        CtcCharset charset,
        bool? applySoftmax = null)
    {
        if (classCount <= 0 || scores.Length % classCount != 0)
        {
            throw new ArgumentException(
                $"La salida ({scores.Length} valores) no es multiplo de {classCount} clases.", nameof(scores));
        }

        var steps = scores.Length / classCount;
        var transposed = new float[scores.Length];

        for (var classIndex = 0; classIndex < classCount; classIndex++)
        {
            for (var step = 0; step < steps; step++)
            {
                transposed[(step * classCount) + classIndex] = scores[(classIndex * steps) + step];
            }
        }

        return Decode(transposed, classCount, charset, applySoftmax);
    }

    private static int ArgMax(ReadOnlySpan<float> values)
    {
        var best = 0;
        for (var index = 1; index < values.Length; index++)
        {
            if (values[index] > values[best])
            {
                best = index;
            }
        }

        return best;
    }

    /// <summary>
    /// Heuristica para saber si una franja ya viene normalizada: todos los valores
    /// en [0, 1] y sumando aproximadamente uno.
    /// </summary>
    private static bool LooksLikeProbabilities(ReadOnlySpan<float> values)
    {
        var sum = 0d;
        foreach (var value in values)
        {
            if (value is < 0f or > 1f || float.IsNaN(value))
            {
                return false;
            }

            sum += value;
        }

        return Math.Abs(sum - 1d) < 0.05d;
    }

    private static float Softmax(ReadOnlySpan<float> values, int index)
    {
        // Se resta el maximo antes de exponenciar para no desbordar con logits grandes.
        var max = values[ArgMax(values)];
        var total = 0d;

        foreach (var value in values)
        {
            total += Math.Exp(value - max);
        }

        return total > 0d ? (float)(Math.Exp(values[index] - max) / total) : 0f;
    }
}
