using System.Collections.Concurrent;

namespace MaritimeVision.Web;

/// <summary>Una secuencia lista para reproducirse anotada.</summary>
/// <param name="Id">Identificador opaco usado en las URLs.</param>
/// <param name="Source">Ruta local o URL que se pasara al lector de video.</param>
/// <param name="DisplayName">Nombre legible para la interfaz.</param>
/// <param name="CreatedAt">Momento de alta, usado para caducar la sesion.</param>
/// <param name="IsUpload">Si el fichero lo subio el usuario y hay que borrarlo al caducar.</param>
public sealed record VisionSession(
    string Id,
    string Source,
    string DisplayName,
    DateTimeOffset CreatedAt,
    bool IsUpload);

/// <summary>
/// Registro en memoria de las secuencias activas.
/// </summary>
/// <remarks>
/// Deliberadamente efimero: el visor no pretende ser un gestor de medios, solo
/// mantener viva una secuencia mientras alguien la esta viendo. Las sesiones
/// caducan y los ficheros subidos se borran con ellas.
/// </remarks>
public sealed class VisionSessionStore(ILogger<VisionSessionStore> logger)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, VisionSession> _sessions = new();

    /// <summary>Da de alta una secuencia y devuelve su sesion.</summary>
    public VisionSession Add(string source, string displayName, bool isUpload)
    {
        Prune();

        var session = new VisionSession(
            Guid.NewGuid().ToString("n"), source, displayName, DateTimeOffset.UtcNow, isUpload);

        _sessions[session.Id] = session;
        logger.LogInformation("Sesion {SessionId} creada para '{DisplayName}'.", session.Id, displayName);
        return session;
    }

    /// <summary>Recupera una sesion por identificador.</summary>
    public VisionSession? Find(string id)
        => _sessions.TryGetValue(id, out var session) ? session : null;

    /// <summary>Sesiones vigentes, de la mas reciente a la mas antigua.</summary>
    public IReadOnlyList<VisionSession> List()
        => _sessions.Values.OrderByDescending(session => session.CreatedAt).ToList();

    /// <summary>Elimina las sesiones caducadas y los ficheros que subieron.</summary>
    public void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow - Lifetime;

        foreach (var session in _sessions.Values.Where(session => session.CreatedAt < cutoff))
        {
            if (!_sessions.TryRemove(session.Id, out _))
            {
                continue;
            }

            if (!session.IsUpload)
            {
                continue;
            }

            try
            {
                File.Delete(session.Source);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "No se pudo borrar el fichero de la sesion {SessionId}.", session.Id);
            }
        }
    }
}
