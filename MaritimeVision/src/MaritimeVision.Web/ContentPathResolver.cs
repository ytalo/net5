namespace MaritimeVision.Web;

/// <summary>
/// Resuelve las rutas relativas de la configuracion.
/// </summary>
/// <remarks>
/// El modelo y las etiquetas viven junto a la solucion, pero la aplicacion se
/// arranca indistintamente desde la raiz del repositorio, desde la carpeta del
/// proyecto o desde el directorio de publicacion. En lugar de obligar a escribir
/// rutas absolutas en <c>appsettings.json</c>, se prueban el directorio actual, la
/// raiz de contenido y sus carpetas superiores.
/// </remarks>
public sealed class ContentPathResolver(IHostEnvironment environment)
{
    private const int MaxAncestors = 6;

    /// <summary>
    /// Devuelve la ruta absoluta del fichero, o <c>null</c> si no se encuentra en
    /// ninguna de las ubicaciones candidatas.
    /// </summary>
    public string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Path.IsPathRooted(path))
        {
            return File.Exists(path) ? path : null;
        }

        foreach (var root in CandidateRoots())
        {
            var candidate = Path.GetFullPath(Path.Combine(root, path));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Ubicaciones en las que se busca, en orden. Se expone para poder incluirlas
    /// en el mensaje de error cuando no se encuentra un fichero.
    /// </summary>
    public IEnumerable<string> CandidateRoots()
    {
        yield return Directory.GetCurrentDirectory();

        var directory = new DirectoryInfo(environment.ContentRootPath);
        for (var level = 0; level < MaxAncestors && directory is not null; level++)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }
}
