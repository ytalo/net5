namespace MaritimeVision.Core.Tracking;

/// <summary>Resultado de una asignacion lineal.</summary>
/// <param name="Matches">Pares (fila, columna) asignados y por debajo del umbral.</param>
/// <param name="UnmatchedRows">Filas que quedaron sin asignar.</param>
/// <param name="UnmatchedColumns">Columnas que quedaron sin asignar.</param>
public sealed record AssignmentResult(
    IReadOnlyList<(int Row, int Column)> Matches,
    IReadOnlyList<int> UnmatchedRows,
    IReadOnlyList<int> UnmatchedColumns);

/// <summary>
/// Resuelve el problema de asignacion lineal (encontrar el emparejamiento de coste
/// minimo entre filas y columnas) por el metodo de caminos aumentantes de
/// Jonker-Volgenant.
/// </summary>
/// <remarks>
/// Es el equivalente de <c>scipy.optimize.linear_sum_assignment</c> / <c>lap.lapjv</c>,
/// que es lo que usa ByteTrack para casar tracks con detecciones. Coste O(n^2 * m)
/// con matrices rectangulares admitidas.
/// </remarks>
public static class LinearAssignment
{
    private const double Infinity = double.MaxValue / 4d;

    /// <summary>
    /// Asigna filas a columnas minimizando el coste total y descarta despues los
    /// pares cuyo coste supere <paramref name="threshold"/>.
    /// </summary>
    /// <param name="cost">Matriz de costes, indexada como <c>[fila, columna]</c>.</param>
    /// <param name="threshold">Coste maximo aceptable para considerar valido un par.</param>
    public static AssignmentResult Solve(double[,] cost, double threshold)
    {
        ArgumentNullException.ThrowIfNull(cost);

        var rows = cost.GetLength(0);
        var columns = cost.GetLength(1);

        if (rows == 0 || columns == 0)
        {
            return new AssignmentResult(
                Array.Empty<(int, int)>(),
                Enumerable.Range(0, rows).ToArray(),
                Enumerable.Range(0, columns).ToArray());
        }

        var assignment = SolveOptimal(cost, rows, columns);

        var matches = new List<(int Row, int Column)>();
        var unmatchedRows = new List<int>();
        var matchedColumns = new bool[columns];

        for (var row = 0; row < rows; row++)
        {
            var column = assignment[row];
            if (column >= 0 && cost[row, column] <= threshold)
            {
                matches.Add((row, column));
                matchedColumns[column] = true;
            }
            else
            {
                unmatchedRows.Add(row);
            }
        }

        var unmatchedColumns = new List<int>();
        for (var column = 0; column < columns; column++)
        {
            if (!matchedColumns[column])
            {
                unmatchedColumns.Add(column);
            }
        }

        return new AssignmentResult(matches, unmatchedRows, unmatchedColumns);
    }

    /// <summary>
    /// Devuelve, para cada fila, la columna que le asigna la solucion optima,
    /// o -1 si la fila queda libre (solo posible con mas filas que columnas).
    /// </summary>
    public static int[] SolveOptimal(double[,] cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        return SolveOptimal(cost, cost.GetLength(0), cost.GetLength(1));
    }

    private static int[] SolveOptimal(double[,] cost, int rows, int columns)
    {
        // El algoritmo exige al menos tantas columnas como filas; si no es asi se
        // resuelve el problema traspuesto y se invierte el resultado.
        if (rows > columns)
        {
            var transposed = new double[columns, rows];
            for (var i = 0; i < rows; i++)
            {
                for (var j = 0; j < columns; j++)
                {
                    transposed[j, i] = cost[i, j];
                }
            }

            var columnToRow = SolveRectangular(transposed, columns, rows);

            var result = new int[rows];
            Array.Fill(result, -1);
            for (var j = 0; j < columns; j++)
            {
                if (columnToRow[j] >= 0)
                {
                    result[columnToRow[j]] = j;
                }
            }

            return result;
        }

        return SolveRectangular(cost, rows, columns);
    }

    /// <summary>
    /// Nucleo de Jonker-Volgenant con potenciales duales, para <c>rows &lt;= columns</c>.
    /// Los vectores usan indexacion 1..n para reservar el 0 como centinela del
    /// camino aumentante, siguiendo la formulacion clasica del algoritmo.
    /// </summary>
    private static int[] SolveRectangular(double[,] cost, int rows, int columns)
    {
        var rowPotential = new double[rows + 1];
        var columnPotential = new double[columns + 1];
        var columnToRow = new int[columns + 1];
        var previousColumn = new int[columns + 1];

        for (var row = 1; row <= rows; row++)
        {
            columnToRow[0] = row;
            var currentColumn = 0;

            var minimalCost = new double[columns + 1];
            var visited = new bool[columns + 1];
            Array.Fill(minimalCost, Infinity);

            do
            {
                visited[currentColumn] = true;
                var currentRow = columnToRow[currentColumn];
                var delta = Infinity;
                var nextColumn = 0;

                for (var column = 1; column <= columns; column++)
                {
                    if (visited[column])
                    {
                        continue;
                    }

                    var reduced = Sanitize(cost[currentRow - 1, column - 1])
                                  - rowPotential[currentRow]
                                  - columnPotential[column];

                    if (reduced < minimalCost[column])
                    {
                        minimalCost[column] = reduced;
                        previousColumn[column] = currentColumn;
                    }

                    if (minimalCost[column] < delta)
                    {
                        delta = minimalCost[column];
                        nextColumn = column;
                    }
                }

                for (var column = 0; column <= columns; column++)
                {
                    if (visited[column])
                    {
                        rowPotential[columnToRow[column]] += delta;
                        columnPotential[column] -= delta;
                    }
                    else
                    {
                        minimalCost[column] -= delta;
                    }
                }

                currentColumn = nextColumn;
            }
            while (columnToRow[currentColumn] != 0);

            // Se recorre el camino aumentante hacia atras reasignando columnas.
            do
            {
                var linkedColumn = previousColumn[currentColumn];
                columnToRow[currentColumn] = columnToRow[linkedColumn];
                currentColumn = linkedColumn;
            }
            while (currentColumn != 0);
        }

        var rowToColumn = new int[rows];
        Array.Fill(rowToColumn, -1);
        for (var column = 1; column <= columns; column++)
        {
            var row = columnToRow[column];
            if (row > 0)
            {
                rowToColumn[row - 1] = column - 1;
            }
        }

        return rowToColumn;
    }

    private static double Sanitize(double value)
        => double.IsNaN(value) ? Infinity : Math.Min(value, Infinity);
}
