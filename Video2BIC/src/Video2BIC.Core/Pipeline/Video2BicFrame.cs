using MaritimeVision.Core.Models;
using OpenCvSharp;
using Video2BIC.Core.Recognition;

namespace Video2BIC.Core.Pipeline;

/// <summary>
/// Un fotograma ya procesado: la imagen anotada y todo lo que se supo de ella.
/// </summary>
/// <param name="Frame">
/// Fotograma BGR. <b>Se reutiliza entre iteraciones</b>: quien lo consuma debe
/// clonarlo si necesita conservarlo.
/// </param>
/// <param name="Analysis">Detecciones y contenedores seguidos en este fotograma.</param>
/// <param name="Readings">Lecturas de codigo intentadas en este fotograma.</param>
/// <param name="NewlyIdentified">
/// Contenedores cuyo codigo ha quedado confirmado justo en este fotograma. Es el
/// evento que interesa a un sistema de gestion de terminal: «acaba de entrar el
/// contenedor X».
/// </param>
/// <param name="Fps">FPS de proceso instantaneos, suavizados.</param>
public readonly record struct Video2BicFrame(
    Mat Frame,
    FrameAnalysis Analysis,
    IReadOnlyList<BicReading> Readings,
    IReadOnlyList<ContainerIdentification> NewlyIdentified,
    double Fps);
