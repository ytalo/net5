using MaritimeVision.Core.Tracking;
using Xunit;

namespace MaritimeVision.Tests;

public class LinearAssignmentTests
{
    [Fact]
    public void SolvesTheClassicThreeByThreeInstance()
    {
        // Optimo conocido: (0,1) + (1,0) + (2,2) = 5.
        var cost = new double[,]
        {
            { 4, 1, 3 },
            { 2, 0, 5 },
            { 3, 2, 2 },
        };

        var assignment = LinearAssignment.SolveOptimal(cost);

        Assert.Equal(new[] { 1, 0, 2 }, assignment);
        Assert.Equal(5d, TotalCost(cost, assignment), 9);
    }

    [Fact]
    public void PrefersTheGloballyOptimalPairingOverTheGreedyOne()
    {
        // La eleccion voraz tomaria (0,0)=1 y quedaria atrapada en un total de 11.
        var cost = new double[,]
        {
            { 1, 2 },
            { 10, 3 },
        };

        var assignment = LinearAssignment.SolveOptimal(cost);

        Assert.Equal(new[] { 0, 1 }, assignment);
        Assert.Equal(4d, TotalCost(cost, assignment), 9);
    }

    [Fact]
    public void HandlesMoreColumnsThanRows()
    {
        var cost = new double[,]
        {
            { 9, 1, 8 },
            { 7, 6, 2 },
        };

        var assignment = LinearAssignment.SolveOptimal(cost);

        Assert.Equal(2, assignment.Length);
        Assert.All(assignment, column => Assert.InRange(column, 0, 2));
        Assert.Equal(3d, TotalCost(cost, assignment), 9);
    }

    [Fact]
    public void HandlesMoreRowsThanColumns()
    {
        var cost = new double[,]
        {
            { 5, 9 },
            { 1, 4 },
            { 8, 2 },
        };

        var assignment = LinearAssignment.SolveOptimal(cost);

        // Solo dos columnas: exactamente una fila queda sin asignar.
        Assert.Equal(1, assignment.Count(column => column < 0));
        Assert.Equal(3d, TotalCost(cost, assignment), 9);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 3)]
    [InlineData(6, 6)]
    public void MatchesBruteForceOnRandomInstances(int rows, int columns)
    {
        var random = new Random(20260819 + (rows * 31) + columns);

        for (var trial = 0; trial < 40; trial++)
        {
            var cost = new double[rows, columns];
            for (var i = 0; i < rows; i++)
            {
                for (var j = 0; j < columns; j++)
                {
                    cost[i, j] = Math.Round(random.NextDouble() * 20d, 4);
                }
            }

            var assignment = LinearAssignment.SolveOptimal(cost);
            var actual = TotalCost(cost, assignment);
            var expected = BruteForceMinimum(cost, rows, columns);

            Assert.Equal(expected, actual, 6);
            AssertIsValidAssignment(assignment, rows, columns);
        }
    }

    [Fact]
    public void ThresholdMovesExpensivePairsToTheUnmatchedLists()
    {
        // El unico emparejamiento barato es (0,0); el otro par cuesta 100.
        var cost = new double[,]
        {
            { 0.1, 100 },
            { 100, 100 },
        };

        var result = LinearAssignment.Solve(cost, threshold: 0.8d);

        Assert.Equal(new[] { (0, 0) }, result.Matches);
        Assert.Equal(new[] { 1 }, result.UnmatchedRows);
        Assert.Equal(new[] { 1 }, result.UnmatchedColumns);
    }

    [Fact]
    public void EmptyInputsProduceEmptyMatches()
    {
        var result = LinearAssignment.Solve(new double[0, 0], threshold: 0.5d);

        Assert.Empty(result.Matches);
        Assert.Empty(result.UnmatchedRows);
        Assert.Empty(result.UnmatchedColumns);
    }

    [Fact]
    public void NoRowsButSomeColumnsLeavesEveryColumnUnmatched()
    {
        var result = LinearAssignment.Solve(new double[0, 3], threshold: 0.5d);

        Assert.Empty(result.Matches);
        Assert.Equal(new[] { 0, 1, 2 }, result.UnmatchedColumns);
    }

    private static void AssertIsValidAssignment(int[] assignment, int rows, int columns)
    {
        Assert.Equal(rows, assignment.Length);

        var used = new HashSet<int>();
        foreach (var column in assignment)
        {
            if (column < 0)
            {
                continue;
            }

            Assert.InRange(column, 0, columns - 1);
            Assert.True(used.Add(column), "Una columna se asigno a dos filas distintas.");
        }

        Assert.Equal(Math.Min(rows, columns), used.Count);
    }

    private static double TotalCost(double[,] cost, int[] assignment)
    {
        var total = 0d;
        for (var row = 0; row < assignment.Length; row++)
        {
            if (assignment[row] >= 0)
            {
                total += cost[row, assignment[row]];
            }
        }

        return total;
    }

    /// <summary>
    /// Minimo exacto por enumeracion; solo para instancias pequenas.
    /// Cuando hay mas filas que columnas, cualquier fila puede quedar libre, no
    /// solo las ultimas: hay que explorar tambien esa rama.
    /// </summary>
    private static double BruteForceMinimum(double[,] cost, int rows, int columns)
    {
        var required = Math.Min(rows, columns);
        var best = double.MaxValue;
        var used = new bool[columns];

        void Recurse(int row, int assigned, double accumulated)
        {
            if (accumulated >= best)
            {
                return;
            }

            if (row == rows)
            {
                if (assigned == required)
                {
                    best = accumulated;
                }

                return;
            }

            // Quedan menos filas por recorrer de las que hacen falta para completar
            // el emparejamiento: esta rama no puede llevar a una solucion valida.
            if (assigned + (rows - row) < required)
            {
                return;
            }

            for (var column = 0; column < columns; column++)
            {
                if (used[column])
                {
                    continue;
                }

                used[column] = true;
                Recurse(row + 1, assigned + 1, accumulated + cost[row, column]);
                used[column] = false;
            }

            Recurse(row + 1, assigned, accumulated);
        }

        Recurse(0, 0, 0d);
        return best;
    }
}
