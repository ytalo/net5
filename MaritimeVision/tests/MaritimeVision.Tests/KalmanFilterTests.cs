using MaritimeVision.Core.Models;
using MaritimeVision.Core.Tracking;
using Xunit;

namespace MaritimeVision.Tests;

public class KalmanFilterTests
{
    [Fact]
    public void InitiateCentersTheStateOnTheObservation()
    {
        var filter = new KalmanFilter();
        var box = new BoundingBox(100, 200, 50, 80);

        var state = filter.Initiate(box);
        var recovered = KalmanFilter.ToBoundingBox(state);

        Assert.Equal(box.X, recovered.X, 3);
        Assert.Equal(box.Y, recovered.Y, 3);
        Assert.Equal(box.Width, recovered.Width, 3);
        Assert.Equal(box.Height, recovered.Height, 3);

        // Sin evidencia de movimiento, la velocidad inicial debe ser nula.
        Assert.Equal(0d, state.Mean[4, 0], 9);
        Assert.Equal(0d, state.Mean[5, 0], 9);
    }

    [Fact]
    public void PredictIncreasesUncertainty()
    {
        var filter = new KalmanFilter();
        var state = filter.Initiate(new BoundingBox(50, 50, 40, 60));

        var predicted = filter.Predict(state);

        Assert.True(predicted.Covariance[0, 0] > state.Covariance[0, 0]);
        Assert.True(predicted.Covariance[1, 1] > state.Covariance[1, 1]);
    }

    [Fact]
    public void UpdateReducesUncertainty()
    {
        var filter = new KalmanFilter();
        var state = filter.Predict(filter.Initiate(new BoundingBox(50, 50, 40, 60)));

        var updated = filter.Update(state, new BoundingBox(54, 50, 40, 60));

        Assert.True(updated.Covariance[0, 0] < state.Covariance[0, 0]);
    }

    [Fact]
    public void LearnsConstantVelocityAndExtrapolatesThroughAGap()
    {
        var filter = new KalmanFilter();
        const float stepX = 6f;
        const float startX = 100f;
        const int observedFrames = 40;

        var state = filter.Initiate(new BoundingBox(startX, 200, 60, 90));

        // Se observa el objeto avanzando a velocidad constante.
        for (var frame = 1; frame <= observedFrames; frame++)
        {
            state = filter.Predict(state);
            state = filter.Update(state, new BoundingBox(startX + (stepX * frame), 200, 60, 90));
        }

        // El filtro converge asintoticamente: el ruido de proceso le impide fijar
        // la velocidad exacta, asi que se comprueba un margen relativo, no la
        // igualdad. Un 5% basta para distinguir "aprendio el movimiento" de "no".
        Assert.InRange(state.Mean[4, 0], stepX * 0.95d, stepX * 1.05d);
        Assert.InRange(state.Mean[5, 0], -0.5d, 0.5d);

        // A partir de aqui deja de haber observaciones: el filtro debe seguir
        // extrapolando, que es lo que sostiene un track durante una oclusion.
        const int gapFrames = 5;
        for (var frame = 0; frame < gapFrames; frame++)
        {
            state = filter.Predict(state);
        }

        var expectedX = startX + (stepX * (observedFrames + gapFrames));
        var extrapolated = KalmanFilter.ToBoundingBox(state);

        // La caja extrapolada debe seguir solapando holgadamente con la posicion real.
        var truth = new BoundingBox(expectedX, 200, 60, 90);
        Assert.True(
            extrapolated.IntersectionOverUnion(truth) > 0.8f,
            $"La extrapolacion se desvio demasiado: {extrapolated} frente a {truth}.");
    }

    [Fact]
    public void GatingDistanceIsSmallerForThePlausibleObservation()
    {
        var filter = new KalmanFilter();
        var state = filter.Predict(filter.Initiate(new BoundingBox(100, 100, 50, 70)));

        var distances = filter.GatingDistance(state, [
            new BoundingBox(102, 101, 50, 70),   // desplazamiento verosimil
            new BoundingBox(600, 400, 50, 70),   // al otro lado de la imagen
        ]);

        Assert.True(distances[0] < distances[1]);
        Assert.True(distances[0] < KalmanFilter.Chi2Inv95[3]);
        Assert.True(distances[1] > KalmanFilter.Chi2Inv95[3]);
    }

    [Fact]
    public void CovarianceStaysSymmetricOverManyIterations()
    {
        var filter = new KalmanFilter();
        var state = filter.Initiate(new BoundingBox(10, 10, 30, 40));
        var random = new Random(7);

        for (var frame = 0; frame < 500; frame++)
        {
            state = filter.Predict(state);
            state = filter.Update(state, new BoundingBox(
                10 + (frame * 2f) + (float)random.NextDouble(),
                10 + (float)random.NextDouble(),
                30,
                40));
        }

        for (var i = 0; i < KalmanFilter.StateDimension; i++)
        {
            for (var j = 0; j < KalmanFilter.StateDimension; j++)
            {
                Assert.Equal(state.Covariance[i, j], state.Covariance[j, i], 12);
            }

            Assert.True(state.Covariance[i, i] > 0d, $"La varianza del componente {i} dejo de ser positiva.");
            Assert.False(double.IsNaN(state.Mean[i, 0]), $"El componente {i} del estado degenero a NaN.");
        }
    }
}
