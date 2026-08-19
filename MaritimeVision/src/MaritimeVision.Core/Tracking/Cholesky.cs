namespace MaritimeVision.Core.Tracking;

/// <summary>
/// Descomposicion de Cholesky <c>A = L * L^T</c> para matrices simetricas
/// definidas positivas, con resolucion de sistemas asociada.
/// </summary>
/// <remarks>
/// Reemplaza a <c>scipy.linalg.cho_factor</c> / <c>cho_solve</c>, que es lo que
/// usa la implementacion de referencia de ByteTrack para calcular la ganancia de
/// Kalman y la distancia de Mahalanobis.
/// </remarks>
public sealed class Cholesky
{
    private readonly Matrix _lower;

    private Cholesky(Matrix lower) => _lower = lower;

    /// <summary>Factor triangular inferior <c>L</c>.</summary>
    public Matrix Lower => _lower;

    /// <summary>
    /// Descompone <paramref name="matrix"/>. Si la matriz pierde la definicion
    /// positiva por acumulacion de error numerico, se le suma un multiplo
    /// creciente de la identidad hasta recuperarla.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si la matriz no es cuadrada o sigue sin ser factorizable tras la regularizacion.
    /// </exception>
    public static Cholesky Decompose(Matrix matrix)
    {
        if (matrix.Rows != matrix.Columns)
        {
            throw new InvalidOperationException("La descomposicion de Cholesky exige una matriz cuadrada.");
        }

        var candidate = matrix.Symmetrize();
        var jitter = 0d;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (TryDecompose(candidate, jitter, out var lower))
            {
                return new Cholesky(lower);
            }

            jitter = jitter == 0d ? 1e-9d : jitter * 100d;
        }

        throw new InvalidOperationException(
            "La matriz no es definida positiva ni siquiera tras regularizarla.");
    }

    private static bool TryDecompose(Matrix matrix, double jitter, out Matrix lower)
    {
        var n = matrix.Rows;
        lower = new Matrix(n, n);

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var sum = matrix[i, j];
                if (i == j)
                {
                    sum += jitter;
                }

                for (var k = 0; k < j; k++)
                {
                    sum -= lower[i, k] * lower[j, k];
                }

                if (i == j)
                {
                    if (sum <= 0d || double.IsNaN(sum))
                    {
                        return false;
                    }

                    lower[i, j] = Math.Sqrt(sum);
                }
                else
                {
                    lower[i, j] = sum / lower[j, j];
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Resuelve <c>A * X = B</c> por sustitucion progresiva y regresiva.
    /// </summary>
    public Matrix Solve(Matrix rightHandSide)
    {
        if (rightHandSide.Rows != _lower.Rows)
        {
            throw new ArgumentException(
                $"El termino independiente debe tener {_lower.Rows} filas, no {rightHandSide.Rows}.",
                nameof(rightHandSide));
        }

        var n = _lower.Rows;
        var columns = rightHandSide.Columns;
        var result = rightHandSide.Clone();

        // L * Y = B
        for (var c = 0; c < columns; c++)
        {
            for (var i = 0; i < n; i++)
            {
                var sum = result[i, c];
                for (var k = 0; k < i; k++)
                {
                    sum -= _lower[i, k] * result[k, c];
                }

                result[i, c] = sum / _lower[i, i];
            }
        }

        // L^T * X = Y
        for (var c = 0; c < columns; c++)
        {
            for (var i = n - 1; i >= 0; i--)
            {
                var sum = result[i, c];
                for (var k = i + 1; k < n; k++)
                {
                    sum -= _lower[k, i] * result[k, c];
                }

                result[i, c] = sum / _lower[i, i];
            }
        }

        return result;
    }

    /// <summary>
    /// Resuelve unicamente <c>L * Y = B</c> (sustitucion progresiva).
    /// La suma de cuadrados de <c>Y</c> es la distancia de Mahalanobis al cuadrado,
    /// que es como ByteTrack calcula el gating sin invertir la covarianza.
    /// </summary>
    public Matrix SolveLowerTriangular(Matrix rightHandSide)
    {
        if (rightHandSide.Rows != _lower.Rows)
        {
            throw new ArgumentException(
                $"El termino independiente debe tener {_lower.Rows} filas, no {rightHandSide.Rows}.",
                nameof(rightHandSide));
        }

        var n = _lower.Rows;
        var result = rightHandSide.Clone();

        for (var c = 0; c < result.Columns; c++)
        {
            for (var i = 0; i < n; i++)
            {
                var sum = result[i, c];
                for (var k = 0; k < i; k++)
                {
                    sum -= _lower[i, k] * result[k, c];
                }

                result[i, c] = sum / _lower[i, i];
            }
        }

        return result;
    }
}
