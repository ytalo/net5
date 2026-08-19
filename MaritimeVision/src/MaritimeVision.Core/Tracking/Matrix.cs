namespace MaritimeVision.Core.Tracking;

/// <summary>
/// Matriz densa de doubles, en almacenamiento por filas.
/// </summary>
/// <remarks>
/// ByteTrack solo necesita matrices de 8x8 como maximo, asi que no compensa
/// arrastrar una dependencia de algebra lineal: esta clase cubre exactamente las
/// operaciones que usan el filtro de Kalman y el gating de Mahalanobis.
/// </remarks>
public sealed class Matrix
{
    private readonly double[] _values;

    public Matrix(int rows, int columns)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));

        Rows = rows;
        Columns = columns;
        _values = new double[rows * columns];
    }

    public int Rows { get; }

    public int Columns { get; }

    public double this[int row, int column]
    {
        get => _values[(row * Columns) + column];
        set => _values[(row * Columns) + column] = value;
    }

    /// <summary>Matriz identidad de tamano <paramref name="size"/>.</summary>
    public static Matrix Identity(int size)
    {
        var result = new Matrix(size, size);
        for (var i = 0; i < size; i++)
        {
            result[i, i] = 1d;
        }

        return result;
    }

    /// <summary>Matriz diagonal a partir de los valores dados.</summary>
    public static Matrix Diagonal(ReadOnlySpan<double> diagonal)
    {
        var result = new Matrix(diagonal.Length, diagonal.Length);
        for (var i = 0; i < diagonal.Length; i++)
        {
            result[i, i] = diagonal[i];
        }

        return result;
    }

    /// <summary>Vector columna a partir de los valores dados.</summary>
    public static Matrix ColumnVector(ReadOnlySpan<double> values)
    {
        var result = new Matrix(values.Length, 1);
        for (var i = 0; i < values.Length; i++)
        {
            result[i, 0] = values[i];
        }

        return result;
    }

    public Matrix Clone()
    {
        var result = new Matrix(Rows, Columns);
        Array.Copy(_values, result._values, _values.Length);
        return result;
    }

    /// <summary>Producto matricial <c>this * other</c>.</summary>
    public Matrix Multiply(Matrix other)
    {
        if (Columns != other.Rows)
        {
            throw new ArgumentException(
                $"No se puede multiplicar {Rows}x{Columns} por {other.Rows}x{other.Columns}.", nameof(other));
        }

        var result = new Matrix(Rows, other.Columns);
        for (var i = 0; i < Rows; i++)
        {
            for (var k = 0; k < Columns; k++)
            {
                var a = this[i, k];
                if (a == 0d)
                {
                    continue;
                }

                for (var j = 0; j < other.Columns; j++)
                {
                    result[i, j] += a * other[k, j];
                }
            }
        }

        return result;
    }

    /// <summary>Producto <c>this * other^T</c>, sin materializar la traspuesta.</summary>
    public Matrix MultiplyByTranspose(Matrix other)
    {
        if (Columns != other.Columns)
        {
            throw new ArgumentException(
                $"No se puede multiplicar {Rows}x{Columns} por la traspuesta de {other.Rows}x{other.Columns}.",
                nameof(other));
        }

        var result = new Matrix(Rows, other.Rows);
        for (var i = 0; i < Rows; i++)
        {
            for (var j = 0; j < other.Rows; j++)
            {
                var sum = 0d;
                for (var k = 0; k < Columns; k++)
                {
                    sum += this[i, k] * other[j, k];
                }

                result[i, j] = sum;
            }
        }

        return result;
    }

    public Matrix Transpose()
    {
        var result = new Matrix(Columns, Rows);
        for (var i = 0; i < Rows; i++)
        {
            for (var j = 0; j < Columns; j++)
            {
                result[j, i] = this[i, j];
            }
        }

        return result;
    }

    public Matrix Add(Matrix other)
    {
        EnsureSameShape(other);
        var result = new Matrix(Rows, Columns);
        for (var i = 0; i < _values.Length; i++)
        {
            result._values[i] = _values[i] + other._values[i];
        }

        return result;
    }

    public Matrix Subtract(Matrix other)
    {
        EnsureSameShape(other);
        var result = new Matrix(Rows, Columns);
        for (var i = 0; i < _values.Length; i++)
        {
            result._values[i] = _values[i] - other._values[i];
        }

        return result;
    }

    /// <summary>Fuerza la simetria promediando con la traspuesta.</summary>
    /// <remarks>
    /// Las covarianzas deben ser simetricas, pero la aritmetica en punto flotante
    /// introduce asimetrias de orden 1e-16 que, acumuladas fotograma a fotograma,
    /// pueden hacer fallar la descomposicion de Cholesky.
    /// </remarks>
    public Matrix Symmetrize()
    {
        if (Rows != Columns)
        {
            throw new InvalidOperationException("Solo se puede simetrizar una matriz cuadrada.");
        }

        var result = new Matrix(Rows, Columns);
        for (var i = 0; i < Rows; i++)
        {
            for (var j = 0; j < Columns; j++)
            {
                result[i, j] = (this[i, j] + this[j, i]) * 0.5d;
            }
        }

        return result;
    }

    private void EnsureSameShape(Matrix other)
    {
        if (Rows != other.Rows || Columns != other.Columns)
        {
            throw new ArgumentException(
                $"Dimensiones incompatibles: {Rows}x{Columns} frente a {other.Rows}x{other.Columns}.", nameof(other));
        }
    }
}
