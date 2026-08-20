using MaritimeVision.Core.Tracking;
using Xunit;

namespace MaritimeVision.Tests;

public class LinearAlgebraTests
{
    [Fact]
    public void MultiplyProducesTheTextbookResult()
    {
        var a = FromArray(new double[,] { { 1, 2, 3 }, { 4, 5, 6 } });
        var b = FromArray(new double[,] { { 7, 8 }, { 9, 10 }, { 11, 12 } });

        var product = a.Multiply(b);

        Assert.Equal(2, product.Rows);
        Assert.Equal(2, product.Columns);
        Assert.Equal(58d, product[0, 0], 9);
        Assert.Equal(64d, product[0, 1], 9);
        Assert.Equal(139d, product[1, 0], 9);
        Assert.Equal(154d, product[1, 1], 9);
    }

    [Fact]
    public void MultiplyByTransposeMatchesExplicitTranspose()
    {
        var a = FromArray(new double[,] { { 1, 2, 3 }, { 4, 5, 6 } });
        var b = FromArray(new double[,] { { 2, 0, 1 }, { 3, 1, 4 }, { 5, 2, 2 } });

        var direct = a.MultiplyByTranspose(b);
        var viaTranspose = a.Multiply(b.Transpose());

        for (var i = 0; i < direct.Rows; i++)
        {
            for (var j = 0; j < direct.Columns; j++)
            {
                Assert.Equal(viaTranspose[i, j], direct[i, j], 9);
            }
        }
    }

    [Fact]
    public void CholeskyReconstructsTheOriginalMatrix()
    {
        var matrix = FromArray(new double[,]
        {
            { 4, 12, -16 },
            { 12, 37, -43 },
            { -16, -43, 98 },
        });

        var lower = Cholesky.Decompose(matrix).Lower;
        var reconstructed = lower.MultiplyByTranspose(lower);

        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                Assert.Equal(matrix[i, j], reconstructed[i, j], 8);
            }
        }
    }

    [Fact]
    public void CholeskyProducesTheKnownLowerFactor()
    {
        var matrix = FromArray(new double[,]
        {
            { 4, 12, -16 },
            { 12, 37, -43 },
            { -16, -43, 98 },
        });

        var lower = Cholesky.Decompose(matrix).Lower;

        Assert.Equal(2d, lower[0, 0], 8);
        Assert.Equal(6d, lower[1, 0], 8);
        Assert.Equal(1d, lower[1, 1], 8);
        Assert.Equal(-8d, lower[2, 0], 8);
        Assert.Equal(5d, lower[2, 1], 8);
        Assert.Equal(3d, lower[2, 2], 8);
        Assert.Equal(0d, lower[0, 1], 8);
    }

    [Fact]
    public void SolveRecoversTheRightHandSide()
    {
        var matrix = FromArray(new double[,]
        {
            { 6, 2, 1 },
            { 2, 5, 2 },
            { 1, 2, 4 },
        });

        var rightHandSide = FromArray(new double[,] { { 1, 4 }, { 2, 5 }, { 3, 6 } });
        var solution = Cholesky.Decompose(matrix).Solve(rightHandSide);
        var reconstructed = matrix.Multiply(solution);

        for (var i = 0; i < rightHandSide.Rows; i++)
        {
            for (var j = 0; j < rightHandSide.Columns; j++)
            {
                Assert.Equal(rightHandSide[i, j], reconstructed[i, j], 8);
            }
        }
    }

    [Fact]
    public void SolveHandlesLargerRandomSystems()
    {
        const int size = 8;
        var random = new Random(4242);

        // A = M * M^T + n*I es simetrica y definida positiva por construccion.
        var m = new Matrix(size, size);
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < size; j++)
            {
                m[i, j] = random.NextDouble() * 2d - 1d;
            }
        }

        var matrix = m.MultiplyByTranspose(m);
        for (var i = 0; i < size; i++)
        {
            matrix[i, i] += size;
        }

        var rightHandSide = new Matrix(size, 3);
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                rightHandSide[i, j] = random.NextDouble() * 10d;
            }
        }

        var reconstructed = matrix.Multiply(Cholesky.Decompose(matrix).Solve(rightHandSide));

        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                Assert.Equal(rightHandSide[i, j], reconstructed[i, j], 7);
            }
        }
    }

    [Fact]
    public void CholeskyRegularizesASingularMatrixInsteadOfThrowing()
    {
        // Matriz semidefinida (rango 1): sin regularizacion la factorizacion falla.
        var matrix = FromArray(new double[,] { { 1, 1 }, { 1, 1 } });

        var lower = Cholesky.Decompose(matrix).Lower;

        Assert.True(lower[0, 0] > 0d);
        Assert.True(lower[1, 1] > 0d);
    }

    private static Matrix FromArray(double[,] values)
    {
        var matrix = new Matrix(values.GetLength(0), values.GetLength(1));
        for (var i = 0; i < values.GetLength(0); i++)
        {
            for (var j = 0; j < values.GetLength(1); j++)
            {
                matrix[i, j] = values[i, j];
            }
        }

        return matrix;
    }
}
