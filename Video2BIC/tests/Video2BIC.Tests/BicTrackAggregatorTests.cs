using Video2BIC.Core.Bic;
using Video2BIC.Core.Recognition;
using Xunit;

namespace Video2BIC.Tests;

/// <summary>
/// La votacion por contenedor es lo que hace utilizable un OCR imperfecto: la
/// lectura correcta se repite fotograma tras fotograma y las equivocadas se
/// dispersan.
/// </summary>
public class BicTrackAggregatorTests
{
    private static BicReading Reading(
        int trackId,
        string code,
        float confidence = 0.9f,
        int frame = 0,
        int corrections = 0,
        string? sizeType = null)
    {
        Assert.True(BicCode.TryParse(code, out var parsed));

        var candidate = new BicCandidate(parsed, confidence, corrections, code);
        SizeTypeCode? size = null;
        if (sizeType is not null)
        {
            Assert.True(SizeTypeCode.TryParse(sizeType, out var parsedSize));
            size = parsedSize;
        }

        return new BicReading(trackId, frame, TimeSpan.FromSeconds(frame / 25d), candidate, size);
    }

    [Fact]
    public void ConfirmsAfterEnoughAgreeingReadings()
    {
        var aggregator = new BicTrackAggregator();

        Assert.Null(aggregator.Observe(Reading(1, "CSQU3054383", frame: 0)));
        Assert.Null(aggregator.Observe(Reading(1, "CSQU3054383", frame: 5)));

        var identification = aggregator.Observe(Reading(1, "CSQU3054383", frame: 10));

        Assert.NotNull(identification);
        Assert.True(identification.IsConfirmed);
        Assert.Equal("CSQU3054383", identification.Code.Value);
        Assert.Equal(3, identification.Observations);
        Assert.Equal(0, identification.FirstFrame);
        Assert.Equal(10, identification.LastFrame);
    }

    [Fact]
    public void ReportsTheConfirmationOnlyOnce()
    {
        var aggregator = new BicTrackAggregator();

        var events = Enumerable.Range(0, 8)
            .Select(frame => aggregator.Observe(Reading(1, "CSQU3054383", frame: frame)))
            .Count(identification => identification is not null);

        // Un sistema de gestion de terminal no puede recibir el mismo contenedor
        // ocho veces por haberlo leido ocho veces.
        Assert.Equal(1, events);
    }

    [Fact]
    public void ASingleGoodReadingIsNotEnough()
    {
        var aggregator = new BicTrackAggregator();
        Assert.Null(aggregator.Observe(Reading(1, "CSQU3054383", confidence: 1f)));

        var provisional = aggregator.Get(1);
        Assert.NotNull(provisional);
        Assert.False(provisional.IsConfirmed);
        Assert.False(aggregator.IsConfirmed(1));
    }

    [Fact]
    public void ThreeWeakReadingsAreNotEnoughEither()
    {
        // El recuento se cumple pero la evidencia acumulada (3 x 0.2 = 0.6) se queda
        // por debajo del umbral de 1.5.
        var aggregator = new BicTrackAggregator();

        for (var frame = 0; frame < 3; frame++)
        {
            Assert.Null(aggregator.Observe(Reading(1, "CSQU3054383", confidence: 0.2f, frame: frame)));
        }

        Assert.False(aggregator.IsConfirmed(1));
    }

    [Fact]
    public void TheMajorityReadingWinsOverTheOccasionalMistake()
    {
        var aggregator = new BicTrackAggregator();

        aggregator.Observe(Reading(1, "MSKU1234565", frame: 0));
        aggregator.Observe(Reading(1, "CSQU3054383", frame: 5));   // lectura suelta equivocada
        aggregator.Observe(Reading(1, "MSKU1234565", frame: 10));
        aggregator.Observe(Reading(1, "MSKU1234565", frame: 15));
        aggregator.Observe(Reading(1, "MSKU1234565", frame: 20));

        var identification = aggregator.Get(1);

        Assert.NotNull(identification);
        Assert.Equal("MSKU1234565", identification.Code.Value);
        Assert.True(identification.IsConfirmed);
        Assert.Equal(2, identification.CompetingCodes);
    }

    [Fact]
    public void TwoCodesTiedInEvidenceDoNotConfirmAnything()
    {
        // Sin ventaja clara sobre el competidor no hay identificacion: es preferible
        // no dar ningun codigo a dar uno equivocado.
        var aggregator = new BicTrackAggregator();

        for (var frame = 0; frame < 4; frame++)
        {
            aggregator.Observe(Reading(1, "MSKU1234565", frame: frame * 5));
            aggregator.Observe(Reading(1, "CSQU3054383", frame: (frame * 5) + 1));
        }

        Assert.False(aggregator.IsConfirmed(1));

        var identification = aggregator.Get(1);
        Assert.NotNull(identification);
        Assert.False(identification.IsConfirmed);

        // La confianza publicada refleja el empate: la mitad de la evidencia.
        Assert.InRange(identification.Confidence, 0.4f, 0.5f);
    }

    [Fact]
    public void ARepairedReadingCarriesLessWeightThanALiteralOne()
    {
        var literal = new BicTrackAggregator();
        var repaired = new BicTrackAggregator();

        for (var frame = 0; frame < 3; frame++)
        {
            literal.Observe(Reading(1, "CSQU3054383", confidence: 0.6f, frame: frame));
            repaired.Observe(Reading(1, "CSQU3054383", confidence: 0.6f, frame: frame, corrections: 2));
        }

        // 3 x 0.6 = 1.8 supera el umbral; 3 x 0.6 x 0.7 = 1.26 no.
        Assert.True(literal.IsConfirmed(1));
        Assert.False(repaired.IsConfirmed(1));
    }

    [Fact]
    public void AConfirmedCodeDoesNotChangeLater()
    {
        // Si el seguimiento mezcla dos contenedores, la identidad ya publicada tiene
        // que aguantar: cambiarla a posteriori seria peor que no cambiarla.
        var aggregator = new BicTrackAggregator();

        for (var frame = 0; frame < 3; frame++)
        {
            aggregator.Observe(Reading(1, "MSKU1234565", frame: frame));
        }

        Assert.True(aggregator.IsConfirmed(1));

        for (var frame = 10; frame < 20; frame++)
        {
            aggregator.Observe(Reading(1, "CSQU3054383", confidence: 1f, frame: frame));
        }

        Assert.Equal("MSKU1234565", aggregator.Get(1)!.Code.Value);
    }

    [Fact]
    public void KeepsOneVotePerContainer()
    {
        var aggregator = new BicTrackAggregator();

        for (var frame = 0; frame < 3; frame++)
        {
            aggregator.Observe(Reading(1, "MSKU1234565", frame: frame));
            aggregator.Observe(Reading(2, "CSQU3054383", frame: frame));
        }

        Assert.Equal(2, aggregator.TrackCount);
        Assert.Equal("MSKU1234565", aggregator.Get(1)!.Code.Value);
        Assert.Equal("CSQU3054383", aggregator.Get(2)!.Code.Value);
        Assert.Equal(2, aggregator.Results().Count);
    }

    [Fact]
    public void KeepsTheMostRepeatedSizeTypeCode()
    {
        var aggregator = new BicTrackAggregator();

        aggregator.Observe(Reading(1, "MSKU1234565", frame: 0, sizeType: "22G1"));
        aggregator.Observe(Reading(1, "MSKU1234565", frame: 5, sizeType: "45R1"));
        aggregator.Observe(Reading(1, "MSKU1234565", frame: 10, sizeType: "22G1"));

        Assert.Equal("22G1", aggregator.Get(1)!.SizeType!.Value);
    }

    [Fact]
    public void ListsConfirmedContainersFirst()
    {
        var aggregator = new BicTrackAggregator();

        aggregator.Observe(Reading(1, "MSKU1234565", frame: 0));
        for (var frame = 0; frame < 3; frame++)
        {
            aggregator.Observe(Reading(2, "CSQU3054383", frame: frame));
        }

        var results = aggregator.Results();

        Assert.Equal(2, results[0].TrackId);
        Assert.True(results[0].IsConfirmed);
        Assert.False(results[1].IsConfirmed);
    }

    [Fact]
    public void ResetForgetsEverything()
    {
        var aggregator = new BicTrackAggregator();
        aggregator.Observe(Reading(1, "MSKU1234565"));

        aggregator.Reset();

        Assert.Equal(0, aggregator.TrackCount);
        Assert.Null(aggregator.Get(1));
        Assert.Empty(aggregator.Results());
    }

    [Fact]
    public void RejectsAnImpossibleConfiguration()
        => Assert.Throws<InvalidOperationException>(
            () => new BicTrackAggregator(new BicAggregationOptions { MinObservations = 0 }));
}
