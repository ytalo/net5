using MaritimeVision.Core.Abstractions;
using MaritimeVision.Core.Models;

namespace MaritimeVision.Core.Tracking;

/// <summary>
/// Implementacion de ByteTrack (Zhang et al., ECCV 2022) sobre .NET.
/// </summary>
/// <remarks>
/// <para>
/// La idea central de ByteTrack es no tirar las detecciones de confianza baja.
/// Un detector rebaja la confianza justo cuando un objeto se ocluye parcialmente,
/// que es precisamente cuando el tracker mas necesita evidencia. En vez de
/// filtrarlas por umbral, ByteTrack las asocia en una segunda pasada contra los
/// tracks que quedaron huerfanos en la primera.
/// </para>
/// <para>
/// El ciclo por fotograma es:
/// </para>
/// <list type="number">
///   <item>Separar detecciones en confianza alta y baja.</item>
///   <item>Predecir con Kalman la posicion de todos los tracks vivos.</item>
///   <item>Primera asociacion: tracks activos y perdidos frente a detecciones de confianza alta.</item>
///   <item>Segunda asociacion: tracks aun sin casar frente a detecciones de confianza baja.</item>
///   <item>Reconciliar los tracks sin confirmar y dar de alta los nuevos.</item>
///   <item>Retirar los tracks que llevan demasiado tiempo perdidos.</item>
/// </list>
/// <para>Esta clase no es segura para uso concurrente: un tracker por secuencia de video.</para>
/// </remarks>
public sealed class ByteTracker : IObjectTracker
{
    private readonly ByteTrackOptions _options;
    private readonly KalmanFilter _kalmanFilter;

    private readonly List<Track> _trackedTracks = [];
    private readonly List<Track> _lostTracks = [];

    private int _frameId;
    private int _nextTrackId;

    public ByteTracker(ByteTrackOptions? options = null)
    {
        _options = options ?? new ByteTrackOptions();
        _options.Validate();
        _kalmanFilter = new KalmanFilter();
    }

    /// <summary>Numero de fotogramas procesados desde el ultimo reinicio.</summary>
    public int FrameId => _frameId;

    /// <summary>Tracks confirmados y visibles en el fotograma actual.</summary>
    public IReadOnlyList<Track> ActiveTracks => _trackedTracks;

    /// <summary>Tracks vivos pero sin observacion en el fotograma actual.</summary>
    public IReadOnlyList<Track> LostTracks => _lostTracks;

    /// <inheritdoc />
    public void Reset()
    {
        _trackedTracks.Clear();
        _lostTracks.Clear();
        _frameId = 0;
        _nextTrackId = 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<TrackedObject> Update(IReadOnlyList<Detection> detections)
    {
        ArgumentNullException.ThrowIfNull(detections);

        _frameId++;

        var activated = new List<Track>();
        var refound = new List<Track>();
        var newlyLost = new List<Track>();
        var removed = new List<Track>();

        // Paso 1: partir las detecciones por confianza.
        var highScore = new List<Detection>();
        var lowScore = new List<Detection>();
        foreach (var detection in detections)
        {
            if (detection.Score >= _options.TrackThreshold)
            {
                highScore.Add(detection);
            }
            else if (detection.Score >= _options.LowThreshold)
            {
                lowScore.Add(detection);
            }
        }

        // Los tracks recien nacidos aun no confirmados se tratan aparte: compiten
        // solo por las detecciones que sobran, y desaparecen si no las consiguen.
        var confirmed = new List<Track>();
        var unconfirmed = new List<Track>();
        foreach (var track in _trackedTracks)
        {
            (track.IsActivated ? confirmed : unconfirmed).Add(track);
        }

        // Paso 2: predecir. Los tracks perdidos entran en la primera asociacion,
        // que es lo que permite recuperar una identidad tras una oclusion.
        var pool = new List<Track>(confirmed.Count + _lostTracks.Count);
        pool.AddRange(confirmed);
        pool.AddRange(_lostTracks);
        foreach (var track in pool)
        {
            track.Predict();
        }

        // Paso 3: primera asociacion contra las detecciones de confianza alta.
        var firstCost = TrackDistance.IouDistance(pool, highScore, _options.ClassAwareMatching);
        if (_options.FuseScore)
        {
            TrackDistance.FuseScore(firstCost, highScore);
        }

        var firstPass = LinearAssignment.Solve(firstCost, _options.MatchThreshold);
        foreach (var (trackIndex, detectionIndex) in firstPass.Matches)
        {
            var track = pool[trackIndex];
            var detection = highScore[detectionIndex];

            if (track.State == TrackState.Tracked)
            {
                track.Update(detection, _frameId);
                activated.Add(track);
            }
            else
            {
                track.ReActivate(detection, _frameId);
                refound.Add(track);
            }
        }

        // Paso 4: segunda asociacion. Solo participan los tracks que venian siendo
        // seguidos: reactivar uno perdido con una deteccion dudosa daria demasiados
        // cambios de identidad.
        var remainingTracks = firstPass.UnmatchedRows
            .Select(index => pool[index])
            .Where(track => track.State == TrackState.Tracked)
            .ToList();

        var secondCost = TrackDistance.IouDistance(remainingTracks, lowScore, _options.ClassAwareMatching);
        var secondPass = LinearAssignment.Solve(secondCost, _options.SecondMatchThreshold);
        foreach (var (trackIndex, detectionIndex) in secondPass.Matches)
        {
            var track = remainingTracks[trackIndex];
            var detection = lowScore[detectionIndex];

            if (track.State == TrackState.Tracked)
            {
                track.Update(detection, _frameId);
                activated.Add(track);
            }
            else
            {
                track.ReActivate(detection, _frameId);
                refound.Add(track);
            }
        }

        foreach (var trackIndex in secondPass.UnmatchedRows)
        {
            var track = remainingTracks[trackIndex];
            if (track.State != TrackState.Lost)
            {
                track.MarkLost();
                newlyLost.Add(track);
            }
        }

        // Paso 5: los tracks sin confirmar solo pueden casar con lo que haya sobrado.
        var leftoverDetections = firstPass.UnmatchedColumns.Select(index => highScore[index]).ToList();
        var unconfirmedCost = TrackDistance.IouDistance(unconfirmed, leftoverDetections, _options.ClassAwareMatching);
        if (_options.FuseScore)
        {
            TrackDistance.FuseScore(unconfirmedCost, leftoverDetections);
        }

        var unconfirmedPass = LinearAssignment.Solve(unconfirmedCost, _options.UnconfirmedMatchThreshold);
        foreach (var (trackIndex, detectionIndex) in unconfirmedPass.Matches)
        {
            var track = unconfirmed[trackIndex];
            track.Update(leftoverDetections[detectionIndex], _frameId);
            activated.Add(track);
        }

        foreach (var trackIndex in unconfirmedPass.UnmatchedRows)
        {
            var track = unconfirmed[trackIndex];
            track.MarkRemoved();
            removed.Add(track);
        }

        // Paso 6: dar de alta tracks nuevos con lo que siga sin asociar.
        foreach (var detectionIndex in unconfirmedPass.UnmatchedColumns)
        {
            var detection = leftoverDetections[detectionIndex];
            if (detection.Score < _options.EffectiveNewTrackThreshold)
            {
                continue;
            }

            var track = Track.FromDetection(_kalmanFilter, detection);
            track.Activate(++_nextTrackId, _frameId, isFirstFrame: _frameId == 1);
            activated.Add(track);
        }

        // Paso 7: retirar los tracks que agotaron el buffer.
        foreach (var track in _lostTracks)
        {
            if (_frameId - track.FrameId > _options.MaxTimeLost)
            {
                track.MarkRemoved();
                removed.Add(track);
            }
        }

        RebuildTrackLists(activated, refound, newlyLost, removed);

        return BuildOutput();
    }

    private void RebuildTrackLists(
        List<Track> activated,
        List<Track> refound,
        List<Track> newlyLost,
        List<Track> removed)
    {
        var removedSet = new HashSet<Track>(removed);

        var nextTracked = new List<Track>();
        var seen = new HashSet<Track>();

        foreach (var track in _trackedTracks)
        {
            if (track.State == TrackState.Tracked && seen.Add(track))
            {
                nextTracked.Add(track);
            }
        }

        foreach (var track in activated.Concat(refound))
        {
            if (seen.Add(track))
            {
                nextTracked.Add(track);
            }
        }

        var trackedSet = new HashSet<Track>(nextTracked);

        var nextLost = _lostTracks
            .Concat(newlyLost)
            .Distinct()
            .Where(track => !trackedSet.Contains(track) && !removedSet.Contains(track))
            .ToList();

        RemoveDuplicates(nextTracked, nextLost, _options.ClassAwareMatching);

        _trackedTracks.Clear();
        _trackedTracks.AddRange(nextTracked);
        _lostTracks.Clear();
        _lostTracks.AddRange(nextLost);
    }

    /// <summary>
    /// Elimina tracks que han convergido sobre el mismo objeto. Cuando dos cajas
    /// de la misma clase se solapan casi por completo se conserva la que acumula
    /// mas historia, que es la que probablemente lleva la identidad correcta.
    /// </summary>
    private static void RemoveDuplicates(List<Track> tracked, List<Track> lost, bool classAware)
    {
        const double duplicateDistance = 0.15d;

        var dropTracked = new HashSet<int>();
        var dropLost = new HashSet<int>();

        for (var i = 0; i < tracked.Count; i++)
        {
            for (var j = 0; j < lost.Count; j++)
            {
                // Dos cajas de clases distintas en el mismo sitio no son un
                // duplicado: un contenedor sobre un chasis se solapa casi por
                // completo con el camion que lo transporta, y son dos objetos.
                if (classAware && tracked[i].ClassId != lost[j].ClassId)
                {
                    continue;
                }

                if (1d - tracked[i].Box.IntersectionOverUnion(lost[j].Box) > duplicateDistance)
                {
                    continue;
                }

                var trackedAge = tracked[i].FrameId - tracked[i].StartFrame;
                var lostAge = lost[j].FrameId - lost[j].StartFrame;

                if (trackedAge > lostAge)
                {
                    dropLost.Add(j);
                }
                else
                {
                    dropTracked.Add(i);
                }
            }
        }

        for (var i = tracked.Count - 1; i >= 0; i--)
        {
            if (dropTracked.Contains(i))
            {
                tracked.RemoveAt(i);
            }
        }

        for (var j = lost.Count - 1; j >= 0; j--)
        {
            if (dropLost.Contains(j))
            {
                lost.RemoveAt(j);
            }
        }
    }

    private List<TrackedObject> BuildOutput()
    {
        var output = new List<TrackedObject>(_trackedTracks.Count);

        foreach (var track in _trackedTracks)
        {
            if (track.IsActivated)
            {
                output.Add(track.ToTrackedObject(_frameId));
            }
        }

        // Opcionalmente se publica tambien la posicion extrapolada de los tracks
        // ocluidos, para que la caja no parpadee durante oclusiones cortas.
        if (_options.MaxPredictedFrames > 0)
        {
            foreach (var track in _lostTracks)
            {
                if (track.IsActivated && _frameId - track.FrameId <= _options.MaxPredictedFrames)
                {
                    output.Add(track.ToTrackedObject(_frameId));
                }
            }
        }

        return output;
    }
}
