using Video2BIC.Core.Bic;

namespace Video2BIC.Core.Recognition;

/// <summary>Configuracion de <see cref="BicTrackAggregator"/>.</summary>
public sealed class BicAggregationOptions
{
    /// <summary>Lecturas coincidentes minimas para dar un codigo por confirmado.</summary>
    public int MinObservations { get; set; } = 3;

    /// <summary>
    /// Evidencia acumulada minima: la suma de las confianzas de las lecturas
    /// coincidentes. Exigirla ademas del recuento evita confirmar con tres lecturas
    /// malas lo que no se confirmaria con una buena.
    /// </summary>
    public float MinAccumulatedWeight { get; set; } = 1.5f;

    /// <summary>
    /// Ventaja minima del codigo lider sobre el segundo. Con 2.0, el lider debe
    /// acumular el doble de evidencia que su competidor mas cercano.
    /// </summary>
    public float MinLeadRatio { get; set; } = 2f;

    /// <summary>
    /// Lecturas cuyo codigo se propuso corrigiendo caracteres cuentan solo esta
    /// fraccion. Una lectura literal es mejor evidencia que una reparada.
    /// </summary>
    public float RepairedReadingWeight { get; set; } = 0.7f;

    /// <summary>Valida la configuracion.</summary>
    /// <exception cref="InvalidOperationException">Si algun valor es imposible.</exception>
    public void Validate()
    {
        if (MinObservations < 1)
        {
            throw new InvalidOperationException("MinObservations debe ser positivo.");
        }

        if (MinAccumulatedWeight <= 0f)
        {
            throw new InvalidOperationException("MinAccumulatedWeight debe ser positivo.");
        }

        if (MinLeadRatio < 1f)
        {
            throw new InvalidOperationException("MinLeadRatio no puede ser menor que 1.");
        }

        if (RepairedReadingWeight is <= 0f or > 1f)
        {
            throw new InvalidOperationException("RepairedReadingWeight debe estar en (0, 1].");
        }
    }
}

/// <summary>
/// Acumula las lecturas de cada contenedor a lo largo del video y decide cual es
/// su codigo.
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza que convierte un OCR mediocre en un resultado utilizable. Un
/// contenedor permanece en escena decenas de fotogramas y se lee desde angulos,
/// distancias e iluminaciones distintas; la lectura correcta se repite y las
/// equivocadas se dispersan. Votar sobre el conjunto es mucho mas fiable que
/// confiar en el mejor fotograma, y ademas da una medida honesta de cuanta
/// evidencia hay detras de cada identificacion.
/// </para>
/// <para>
/// El voto es ponderado: cada lectura aporta su confianza, rebajada si hizo falta
/// corregir caracteres para hacer cuadrar el digito de control.
/// </para>
/// </remarks>
public sealed class BicTrackAggregator
{
    private sealed class TrackVotes
    {
        public Dictionary<string, CodeVote> ByCode { get; } = new(StringComparer.Ordinal);

        public bool Confirmed { get; set; }

        public string? ConfirmedCode { get; set; }
    }

    private sealed class CodeVote
    {
        public required BicCode Code { get; init; }

        public float Weight { get; set; }

        public int Observations { get; set; }

        public float BestConfidence { get; set; }

        public int FirstFrame { get; set; }

        public int LastFrame { get; set; }

        public TimeSpan FirstSeen { get; set; }

        public TimeSpan LastSeen { get; set; }

        public Dictionary<string, (SizeTypeCode Code, int Count)> SizeTypes { get; } = new(StringComparer.Ordinal);
    }

    private readonly BicAggregationOptions _options;
    private readonly Dictionary<int, TrackVotes> _tracks = [];

    public BicTrackAggregator(BicAggregationOptions? options = null)
    {
        _options = options ?? new BicAggregationOptions();
        _options.Validate();
    }

    /// <summary>Contenedores distintos sobre los que se ha acumulado alguna lectura.</summary>
    public int TrackCount => _tracks.Count;

    /// <summary>
    /// Incorpora una lectura.
    /// </summary>
    /// <returns>
    /// La identificacion si esta lectura es la que hace que el contenedor pase a
    /// estar confirmado; <c>null</c> en cualquier otro caso, incluido cuando ya lo
    /// estaba. Sirve para emitir el evento una sola vez.
    /// </returns>
    public ContainerIdentification? Observe(in BicReading reading)
    {
        if (!_tracks.TryGetValue(reading.TrackId, out var track))
        {
            track = new TrackVotes();
            _tracks[reading.TrackId] = track;
        }

        var key = reading.Code.Value;
        if (!track.ByCode.TryGetValue(key, out var vote))
        {
            vote = new CodeVote
            {
                Code = reading.Code,
                FirstFrame = reading.FrameIndex,
                FirstSeen = reading.Timestamp,
            };
            track.ByCode[key] = vote;
        }

        var weight = reading.Candidate.Confidence
                     * (reading.Candidate.IsLiteral ? 1f : _options.RepairedReadingWeight);

        vote.Weight += weight;
        vote.Observations++;
        vote.BestConfidence = Math.Max(vote.BestConfidence, reading.Candidate.Confidence);
        vote.LastFrame = reading.FrameIndex;
        vote.LastSeen = reading.Timestamp;

        if (reading.SizeType is { } sizeType)
        {
            var count = vote.SizeTypes.TryGetValue(sizeType.Value, out var existing) ? existing.Count : 0;
            vote.SizeTypes[sizeType.Value] = (sizeType, count + 1);
        }

        if (track.Confirmed)
        {
            return null;
        }

        var identification = Resolve(reading.TrackId, track);
        if (identification is not { IsConfirmed: true })
        {
            return null;
        }

        track.Confirmed = true;
        track.ConfirmedCode = identification.Code.Value;
        return identification;
    }

    /// <summary>Identificacion actual de un contenedor, confirmada o no.</summary>
    public ContainerIdentification? Get(int trackId)
        => _tracks.TryGetValue(trackId, out var track) ? Resolve(trackId, track) : null;

    /// <summary><c>true</c> si el contenedor ya tiene un codigo confirmado.</summary>
    public bool IsConfirmed(int trackId)
        => _tracks.TryGetValue(trackId, out var track) && track.Confirmed;

    /// <summary>
    /// Todas las identificaciones, primero las confirmadas y dentro de cada grupo
    /// por confianza.
    /// </summary>
    public IReadOnlyList<ContainerIdentification> Results()
        => _tracks.Keys
            .Select(trackId => Resolve(trackId, _tracks[trackId]))
            .Where(identification => identification is not null)
            .Select(identification => identification!)
            .OrderByDescending(identification => identification.IsConfirmed)
            .ThenByDescending(identification => identification.Confidence)
            .ThenBy(identification => identification.TrackId)
            .ToList();

    /// <summary>Olvida todo lo acumulado.</summary>
    public void Reset() => _tracks.Clear();

    private ContainerIdentification? Resolve(int trackId, TrackVotes track)
    {
        if (track.ByCode.Count == 0)
        {
            return null;
        }

        // Un codigo ya confirmado no cambia aunque despues llegue otro con mas
        // votos: la identidad publicada debe ser estable, y una lectura tardia que
        // gane la votacion es casi siempre un contenedor distinto que el seguimiento
        // ha mezclado.
        var leader = track.ConfirmedCode is not null && track.ByCode.TryGetValue(track.ConfirmedCode, out var pinned)
            ? pinned
            : track.ByCode.Values.OrderByDescending(vote => vote.Weight).First();

        var runnerUp = track.ByCode.Values
            .Where(vote => !ReferenceEquals(vote, leader))
            .Select(vote => vote.Weight)
            .DefaultIfEmpty(0f)
            .Max();

        var confirmed = track.Confirmed
                        || (leader.Observations >= _options.MinObservations
                            && leader.Weight >= _options.MinAccumulatedWeight
                            && leader.Weight >= runnerUp * _options.MinLeadRatio);

        var sizeType = leader.SizeTypes.Count > 0
            ? leader.SizeTypes.Values.OrderByDescending(entry => entry.Count).First().Code
            : null;

        return new ContainerIdentification(
            trackId,
            leader.Code,
            Score(leader, runnerUp),
            leader.Observations,
            track.ByCode.Count,
            leader.FirstFrame,
            leader.LastFrame,
            leader.FirstSeen,
            leader.LastSeen,
            sizeType,
            confirmed);
    }

    /// <summary>
    /// Confianza publicada: la mejor lectura del codigo lider, moderada por la
    /// proporcion de evidencia que acapara frente a sus competidores.
    /// </summary>
    private static float Score(CodeVote leader, float runnerUpWeight)
    {
        var total = leader.Weight + runnerUpWeight;
        var share = total > 0f ? leader.Weight / total : 0f;
        return Math.Clamp(leader.BestConfidence * share, 0f, 1f);
    }
}
