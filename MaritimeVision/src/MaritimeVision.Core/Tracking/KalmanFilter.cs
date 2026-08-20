using MaritimeVision.Core.Models;

namespace MaritimeVision.Core.Tracking;

/// <summary>Media y covarianza del estado de un track.</summary>
/// <param name="Mean">Vector de estado de 8 componentes.</param>
/// <param name="Covariance">Matriz de covarianza 8x8.</param>
public sealed record KalmanState(Matrix Mean, Matrix Covariance);

/// <summary>
/// Filtro de Kalman de velocidad constante para seguimiento de cajas en imagen.
/// </summary>
/// <remarks>
/// <para>
/// El estado es <c>(cx, cy, a, h, vcx, vcy, va, vh)</c>: centro de la caja,
/// relacion de aspecto, altura y sus velocidades. La medida es <c>(cx, cy, a, h)</c>.
/// </para>
/// <para>
/// Es el mismo filtro que usan SORT, DeepSORT y ByteTrack: el ruido de proceso y
/// de medida se escala con la altura de la caja, de modo que un objeto lejano
/// (pequeno en imagen) recibe una incertidumbre proporcionalmente menor en
/// pixeles que uno cercano.
/// </para>
/// </remarks>
public sealed class KalmanFilter
{
    /// <summary>Dimension del vector de estado.</summary>
    public const int StateDimension = 8;

    /// <summary>Dimension del vector de medida.</summary>
    public const int MeasurementDimension = 4;

    /// <summary>
    /// Umbrales de chi-cuadrado al 95% para 1..9 grados de libertad, usados en el
    /// gating de Mahalanobis. El indice 3 (4 grados de libertad) es el habitual,
    /// porque la medida tiene 4 componentes.
    /// </summary>
    public static readonly double[] Chi2Inv95 =
    [
        3.8415, 5.9915, 7.8147, 9.4877, 11.070, 12.592, 14.067, 15.507, 16.919,
    ];

    private readonly Matrix _motionMatrix;
    private readonly Matrix _updateMatrix;
    private readonly double _stdWeightPosition;
    private readonly double _stdWeightVelocity;

    /// <param name="stdWeightPosition">Peso del ruido de posicion respecto a la altura de la caja.</param>
    /// <param name="stdWeightVelocity">Peso del ruido de velocidad respecto a la altura de la caja.</param>
    public KalmanFilter(double stdWeightPosition = 1d / 20d, double stdWeightVelocity = 1d / 160d)
    {
        _stdWeightPosition = stdWeightPosition;
        _stdWeightVelocity = stdWeightVelocity;

        // Modelo de velocidad constante con dt = 1 fotograma.
        _motionMatrix = Matrix.Identity(StateDimension);
        for (var i = 0; i < MeasurementDimension; i++)
        {
            _motionMatrix[i, MeasurementDimension + i] = 1d;
        }

        // La medida observa las 4 primeras componentes del estado.
        _updateMatrix = new Matrix(MeasurementDimension, StateDimension);
        for (var i = 0; i < MeasurementDimension; i++)
        {
            _updateMatrix[i, i] = 1d;
        }
    }

    /// <summary>
    /// Crea el estado inicial a partir de la primera observacion. La velocidad se
    /// inicializa a cero y con una covarianza alta, porque aun no hay evidencia
    /// sobre el movimiento del objeto.
    /// </summary>
    public KalmanState Initiate(in BoundingBox box)
    {
        var (cx, cy, aspect, height) = box.ToXyah();

        var mean = new Matrix(StateDimension, 1);
        mean[0, 0] = cx;
        mean[1, 0] = cy;
        mean[2, 0] = aspect;
        mean[3, 0] = height;

        double[] std =
        [
            2 * _stdWeightPosition * height,
            2 * _stdWeightPosition * height,
            1e-2,
            2 * _stdWeightPosition * height,
            10 * _stdWeightVelocity * height,
            10 * _stdWeightVelocity * height,
            1e-5,
            10 * _stdWeightVelocity * height,
        ];

        var covariance = new Matrix(StateDimension, StateDimension);
        for (var i = 0; i < StateDimension; i++)
        {
            covariance[i, i] = std[i] * std[i];
        }

        return new KalmanState(mean, covariance);
    }

    /// <summary>Propaga el estado un fotograma hacia delante.</summary>
    public KalmanState Predict(KalmanState state)
    {
        var height = state.Mean[3, 0];

        double[] std =
        [
            _stdWeightPosition * height,
            _stdWeightPosition * height,
            1e-2,
            _stdWeightPosition * height,
            _stdWeightVelocity * height,
            _stdWeightVelocity * height,
            1e-5,
            _stdWeightVelocity * height,
        ];

        var processNoise = new Matrix(StateDimension, StateDimension);
        for (var i = 0; i < StateDimension; i++)
        {
            processNoise[i, i] = std[i] * std[i];
        }

        var mean = _motionMatrix.Multiply(state.Mean);
        var covariance = _motionMatrix
            .Multiply(state.Covariance)
            .MultiplyByTranspose(_motionMatrix)
            .Add(processNoise);

        return new KalmanState(mean, covariance.Symmetrize());
    }

    /// <summary>Proyecta el estado al espacio de medida, sumando el ruido de observacion.</summary>
    public KalmanState Project(KalmanState state)
    {
        var height = state.Mean[3, 0];

        double[] std =
        [
            _stdWeightPosition * height,
            _stdWeightPosition * height,
            1e-1,
            _stdWeightPosition * height,
        ];

        var innovationCovariance = new Matrix(MeasurementDimension, MeasurementDimension);
        for (var i = 0; i < MeasurementDimension; i++)
        {
            innovationCovariance[i, i] = std[i] * std[i];
        }

        var mean = _updateMatrix.Multiply(state.Mean);
        var covariance = _updateMatrix
            .Multiply(state.Covariance)
            .MultiplyByTranspose(_updateMatrix)
            .Add(innovationCovariance);

        return new KalmanState(mean, covariance.Symmetrize());
    }

    /// <summary>Corrige el estado con una nueva observacion.</summary>
    public KalmanState Update(KalmanState state, in BoundingBox measurement)
    {
        var projected = Project(state);
        var cholesky = Cholesky.Decompose(projected.Covariance);

        // K = P * H^T * S^-1, resuelto como S * K^T = (P * H^T)^T para evitar invertir S.
        var crossCovariance = state.Covariance.MultiplyByTranspose(_updateMatrix); // 8x4
        var gain = cholesky.Solve(crossCovariance.Transpose()).Transpose();        // 8x4

        var (cx, cy, aspect, height) = measurement.ToXyah();
        var innovation = new Matrix(MeasurementDimension, 1);
        innovation[0, 0] = cx - projected.Mean[0, 0];
        innovation[1, 0] = cy - projected.Mean[1, 0];
        innovation[2, 0] = aspect - projected.Mean[2, 0];
        innovation[3, 0] = height - projected.Mean[3, 0];

        var mean = state.Mean.Add(gain.Multiply(innovation));
        var covariance = state.Covariance.Subtract(
            gain.Multiply(projected.Covariance).MultiplyByTranspose(gain));

        return new KalmanState(mean, covariance.Symmetrize());
    }

    /// <summary>
    /// Distancia de Mahalanobis al cuadrado entre el estado proyectado y cada caja
    /// candidata. Sirve para descartar asociaciones geometricamente imposibles
    /// comparando con <see cref="Chi2Inv95"/>.
    /// </summary>
    public double[] GatingDistance(KalmanState state, IReadOnlyList<BoundingBox> measurements)
    {
        var projected = Project(state);
        var cholesky = Cholesky.Decompose(projected.Covariance);

        var differences = new Matrix(MeasurementDimension, Math.Max(1, measurements.Count));
        for (var i = 0; i < measurements.Count; i++)
        {
            var (cx, cy, aspect, height) = measurements[i].ToXyah();
            differences[0, i] = cx - projected.Mean[0, 0];
            differences[1, i] = cy - projected.Mean[1, 0];
            differences[2, i] = aspect - projected.Mean[2, 0];
            differences[3, i] = height - projected.Mean[3, 0];
        }

        var solved = cholesky.SolveLowerTriangular(differences);

        var distances = new double[measurements.Count];
        for (var i = 0; i < measurements.Count; i++)
        {
            var sum = 0d;
            for (var d = 0; d < MeasurementDimension; d++)
            {
                sum += solved[d, i] * solved[d, i];
            }

            distances[i] = sum;
        }

        return distances;
    }

    /// <summary>Convierte el estado en la caja delimitadora que representa.</summary>
    public static BoundingBox ToBoundingBox(KalmanState state) => BoundingBox.FromXyah(
        (float)state.Mean[0, 0],
        (float)state.Mean[1, 0],
        (float)state.Mean[2, 0],
        (float)state.Mean[3, 0]);
}
