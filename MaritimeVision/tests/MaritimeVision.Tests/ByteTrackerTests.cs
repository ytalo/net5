using MaritimeVision.Core.Models;
using MaritimeVision.Core.Tracking;
using Xunit;

namespace MaritimeVision.Tests;

public class ByteTrackerTests
{
    private const int ContainerClass = 0;
    private const int TruckClass = 1;

    [Fact]
    public void TracksAnObjectMovingAtConstantSpeedWithAStableId()
    {
        var tracker = new ByteTracker();
        var ids = new HashSet<int>();

        for (var frame = 0; frame < 40; frame++)
        {
            var tracks = tracker.Update([Container(20 + (frame * 8), 100)]);

            var track = Assert.Single(tracks);
            ids.Add(track.TrackId);
            Assert.Equal("container", track.Label);
            Assert.True(track.IsConfirmed);
        }

        Assert.Single(ids);
    }

    [Fact]
    public void KeepsSeparateIdentitiesForObjectsMovingInParallel()
    {
        var tracker = new ByteTracker();
        var idsPerFrame = new List<int[]>();

        for (var frame = 0; frame < 30; frame++)
        {
            var tracks = tracker.Update([
                Container(20 + (frame * 6), 60),
                Container(20 + (frame * 6), 300),
            ]);

            Assert.Equal(2, tracks.Count);
            idsPerFrame.Add(tracks.Select(track => track.TrackId).OrderBy(id => id).ToArray());
        }

        // El conjunto de identidades no debe cambiar en ningun momento.
        Assert.All(idsPerFrame, ids => Assert.Equal(idsPerFrame[0], ids));
        Assert.Equal(2, idsPerFrame[0].Distinct().Count());
    }

    [Fact]
    public void ANewTrackNeedsASecondObservationBeforeBeingPublished()
    {
        var tracker = new ByteTracker();

        // Un objeto ya presente desde el primer fotograma se publica de inmediato.
        Assert.Single(tracker.Update([Container(10, 10)]));

        // Uno que aparece despues no: primero hay que confirmarlo.
        var whenAppearing = tracker.Update([Container(18, 10), Container(400, 300)]);
        Assert.Single(whenAppearing);

        var afterConfirmation = tracker.Update([Container(26, 10), Container(408, 300)]);
        Assert.Equal(2, afterConfirmation.Count);
    }

    [Fact]
    public void SpuriousSingleFrameDetectionsNeverBecomeTracks()
    {
        var tracker = new ByteTracker();
        tracker.Update([Container(10, 10)]);

        // Un falso positivo que aparece un unico fotograma en un sitio distinto cada vez.
        tracker.Update([Container(18, 10), Container(500, 40)]);
        tracker.Update([Container(26, 10), Container(120, 380)]);
        var tracks = tracker.Update([Container(34, 10)]);

        Assert.Single(tracks);
    }

    [Fact]
    public void RecoversTheSameIdentityAfterAnOcclusion()
    {
        var tracker = new ByteTracker();
        const int step = 8;

        var idBefore = 0;
        for (var frame = 0; frame < 12; frame++)
        {
            var tracks = tracker.Update([Container(20 + (frame * step), 150)]);
            idBefore = Assert.Single(tracks).TrackId;
        }

        // El objeto pasa por detras de una grua: durante 8 fotogramas no hay detecciones.
        for (var frame = 12; frame < 20; frame++)
        {
            Assert.Empty(tracker.Update([]));
        }

        // Reaparece donde el movimiento predecia.
        var afterOcclusion = tracker.Update([Container(20 + (20 * step), 150)]);

        var recovered = Assert.Single(afterOcclusion);
        Assert.Equal(idBefore, recovered.TrackId);
    }

    [Fact]
    public void AssignsANewIdentityWhenTheObjectReturnsAfterTheBufferExpires()
    {
        var options = new ByteTrackOptions { TrackBuffer = 5, FrameRate = 30d };
        var tracker = new ByteTracker(options);

        var idBefore = 0;
        for (var frame = 0; frame < 6; frame++)
        {
            idBefore = Assert.Single(tracker.Update([Container(40, 40)])).TrackId;
        }

        for (var frame = 0; frame < 20; frame++)
        {
            tracker.Update([]);
        }

        tracker.Update([Container(40, 40)]);
        var tracks = tracker.Update([Container(40, 40)]);

        Assert.NotEqual(idBefore, Assert.Single(tracks).TrackId);
    }

    [Fact]
    public void LowConfidenceDetectionsKeepTheTrackAliveThroughPartialOcclusion()
    {
        // Este es el aporte central de ByteTrack: cuando un contenedor queda medio
        // tapado el detector le baja la confianza, y descartar esas detecciones
        // rompe el track justo cuando mas falta hace.
        var tracker = new ByteTracker(new ByteTrackOptions { TrackThreshold = 0.5f, LowThreshold = 0.1f });

        var idBefore = 0;
        for (var frame = 0; frame < 6; frame++)
        {
            idBefore = Assert.Single(tracker.Update([Container(20 + (frame * 7), 120)])).TrackId;
        }

        // Oclusion parcial: la confianza cae al 30%, por debajo del umbral principal.
        for (var frame = 6; frame < 16; frame++)
        {
            var tracks = tracker.Update([Container(20 + (frame * 7), 120, score: 0.3f)]);

            var track = Assert.Single(tracks);
            Assert.Equal(idBefore, track.TrackId);
            Assert.True(track.IsConfirmed);
        }

        // Al despejarse la oclusion la identidad sigue siendo la misma.
        var recovered = Assert.Single(tracker.Update([Container(20 + (16 * 7), 120)]));
        Assert.Equal(idBefore, recovered.TrackId);
    }

    [Fact]
    public void WithoutTheLowConfidenceStageTheSameSequenceLosesTheTrack()
    {
        // Control del test anterior: igualar los dos umbrales desactiva la segunda
        // asociacion y reproduce el comportamiento de un tracker tipo SORT.
        var tracker = new ByteTracker(new ByteTrackOptions { TrackThreshold = 0.5f, LowThreshold = 0.5f });

        for (var frame = 0; frame < 6; frame++)
        {
            Assert.Single(tracker.Update([Container(20 + (frame * 7), 120)]));
        }

        IReadOnlyList<TrackedObject> tracks = [];
        for (var frame = 6; frame < 16; frame++)
        {
            tracks = tracker.Update([Container(20 + (frame * 7), 120, score: 0.3f)]);
        }

        Assert.Empty(tracks);
    }

    [Fact]
    public void ClassAwareMatchingPreventsIdentityTransferBetweenClasses()
    {
        var tracker = new ByteTracker(new ByteTrackOptions { ClassAwareMatching = true });

        var containerId = 0;
        for (var frame = 0; frame < 5; frame++)
        {
            containerId = Assert.Single(tracker.Update([Container(100, 100)])).TrackId;
        }

        // Un camion aparece exactamente encima del contenedor. Sin conciencia de
        // clase, el solape perfecto haria que heredase la identidad.
        tracker.Update([Detect(100, 100, TruckClass, "container-truck")]);
        var tracks = tracker.Update([Detect(100, 100, TruckClass, "container-truck")]);

        var truck = Assert.Single(tracks, track => track.Label == "container-truck");
        Assert.NotEqual(containerId, truck.TrackId);
    }

    [Fact]
    public void ClassAgnosticMatchingLetsTheIdentityFollowTheOverlappingBox()
    {
        var tracker = new ByteTracker(new ByteTrackOptions { ClassAwareMatching = false });

        var containerId = 0;
        for (var frame = 0; frame < 5; frame++)
        {
            containerId = Assert.Single(tracker.Update([Container(100, 100)])).TrackId;
        }

        var tracks = tracker.Update([Detect(100, 100, TruckClass, "container-truck")]);

        var track = Assert.Single(tracks);
        Assert.Equal(containerId, track.TrackId);
        Assert.Equal("container-truck", track.Label);
    }

    [Fact]
    public void DetectionsBelowTheNewTrackThresholdNeverStartATrack()
    {
        var tracker = new ByteTracker(new ByteTrackOptions
        {
            TrackThreshold = 0.5f,
            NewTrackThreshold = 0.8f,
        });

        for (var frame = 0; frame < 10; frame++)
        {
            // Por encima del umbral de asociacion pero por debajo del de alta.
            Assert.Empty(tracker.Update([Container(50, 50, score: 0.6f)]));
        }
    }

    [Fact]
    public void PredictedTracksAreOnlyPublishedWhenExplicitlyEnabled()
    {
        var options = new ByteTrackOptions { MaxPredictedFrames = 5 };
        var tracker = new ByteTracker(options);

        for (var frame = 0; frame < 8; frame++)
        {
            tracker.Update([Container(20 + (frame * 5), 90)]);
        }

        var duringGap = tracker.Update([]);

        var predicted = Assert.Single(duringGap);
        Assert.Equal(TrackState.Lost, predicted.State);
        Assert.False(predicted.IsConfirmed);
    }

    [Fact]
    public void ResetClearsEveryTrackAndRestartsTheIdCounter()
    {
        var tracker = new ByteTracker();
        for (var frame = 0; frame < 5; frame++)
        {
            tracker.Update([Container(30, 30)]);
        }

        Assert.NotEmpty(tracker.ActiveTracks);

        tracker.Reset();

        Assert.Empty(tracker.ActiveTracks);
        Assert.Empty(tracker.LostTracks);
        Assert.Equal(0, tracker.FrameId);

        var restarted = Assert.Single(tracker.Update([Container(30, 30)]));
        Assert.Equal(1, restarted.TrackId);
    }

    [Fact]
    public void TrackIdsAreUniqueAcrossAWholeCrowdedSequence()
    {
        var tracker = new ByteTracker();
        var random = new Random(1234);
        var seen = new Dictionary<int, int>();

        for (var frame = 0; frame < 60; frame++)
        {
            var detections = new List<Detection>();
            for (var lane = 0; lane < 5; lane++)
            {
                // Cinco objetos en carriles separados, con ruido de deteccion.
                var jitter = (float)((random.NextDouble() - 0.5d) * 2d);
                detections.Add(Container(30 + (frame * 5) + jitter, 40 + (lane * 90)));
            }

            foreach (var track in tracker.Update(detections))
            {
                seen[track.TrackId] = frame;
            }
        }

        // Cinco objetos reales deben producir exactamente cinco identidades.
        Assert.Equal(5, seen.Count);
    }

    private static Detection Container(float x, float y, float score = 0.9f)
        => Detect(x, y, ContainerClass, "container", score);

    private static Detection Detect(float x, float y, int classId, string label, float score = 0.9f)
        => new(new BoundingBox(x, y, 70, 55), score, classId, label);
}
