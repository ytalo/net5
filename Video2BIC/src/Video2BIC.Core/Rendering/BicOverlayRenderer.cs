using MaritimeVision.Core.Models;
using MaritimeVision.Core.Rendering;
using OpenCvSharp;
using Video2BIC.Core.Recognition;

namespace Video2BIC.Core.Rendering;

/// <summary>
/// Dibuja sobre el fotograma lo que la aplicacion sabe de cada contenedor: donde
/// esta, si se le esta leyendo el codigo y cual ha resultado ser.
/// </summary>
/// <remarks>
/// El color codifica el estado, que es lo que interesa de un vistazo: ambar
/// mientras se busca el codigo, verde cuando esta confirmado, gris cuando la caja
/// viene solo de la prediccion del filtro de Kalman. No es segura para uso
/// concurrente.
/// </remarks>
public sealed class BicOverlayRenderer
{
    private const HersheyFonts Font = HersheyFonts.HersheyDuplex;

    private static readonly Scalar Searching = new(60, 170, 245);
    private static readonly Scalar Confirmed = new(80, 200, 80);
    private static readonly Scalar Predicted = new(150, 150, 150);
    private static readonly Scalar Region = new(220, 200, 80);

    private readonly BicOverlayOptions _options;

    public BicOverlayRenderer(BicOverlayOptions? options = null)
        => _options = options ?? new BicOverlayOptions();

    /// <summary>
    /// Dibuja el estado del fotograma, modificandolo en el sitio.
    /// </summary>
    /// <param name="frame">Fotograma BGR.</param>
    /// <param name="tracks">Contenedores seguidos en este fotograma.</param>
    /// <param name="identifications">
    /// Identificacion actual de cada contenedor, indexada por identidad.
    /// </param>
    /// <param name="regions">
    /// Zonas de texto exploradas en este fotograma, en coordenadas del fotograma.
    /// </param>
    /// <param name="frameIndex">Indice del fotograma.</param>
    /// <param name="fps">FPS de proceso, suavizados.</param>
    public void Draw(
        Mat frame,
        IReadOnlyList<TrackedObject> tracks,
        IReadOnlyDictionary<int, ContainerIdentification> identifications,
        IReadOnlyList<Rect> regions,
        int frameIndex,
        double fps)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(identifications);
        ArgumentNullException.ThrowIfNull(regions);

        if (_options.ShowTextRegions)
        {
            foreach (var region in regions)
            {
                Cv2.Rectangle(frame, region, Region, 1);
            }
        }

        foreach (var track in tracks)
        {
            identifications.TryGetValue(track.TrackId, out var identification);
            DrawTrack(frame, track, identification);
        }

        if (_options.ShowHud)
        {
            DrawHud(frame, tracks.Count, identifications, frameIndex, fps);
        }
    }

    private void DrawTrack(Mat frame, in TrackedObject track, ContainerIdentification? identification)
    {
        var box = track.Box.ClampTo(frame.Width, frame.Height);
        var rect = new Rect(
            (int)Math.Round(box.X),
            (int)Math.Round(box.Y),
            (int)Math.Round(box.Width),
            (int)Math.Round(box.Height));

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var color = !track.IsConfirmed
            ? Predicted
            : identification is { IsConfirmed: true }
                ? Confirmed
                : Searching;

        Cv2.Rectangle(frame, rect, color, _options.BoxThickness);

        if (!_options.ShowLabels)
        {
            return;
        }

        var lines = new List<string> { $"#{track.TrackId} {track.Label}" };

        if (identification is null)
        {
            lines.Add("leyendo codigo...");
        }
        else
        {
            var size = identification.SizeType is null ? string.Empty : $"  {identification.SizeType.Value}";
            var state = identification.IsConfirmed ? "OK" : "?";
            lines.Add($"{identification.Code.Value}{size}  {state} {identification.Confidence:P0}");
        }

        DrawLabel(frame, rect, lines, color);
    }

    private void DrawLabel(Mat frame, Rect box, IReadOnlyList<string> lines, Scalar color)
    {
        const int paddingX = 5;
        const int paddingY = 4;

        var width = 0;
        var lineHeight = 0;
        var baseline = 0;

        foreach (var line in lines)
        {
            var size = Cv2.GetTextSize(line, Font, _options.FontScale, 1, out var lineBaseline);
            width = Math.Max(width, size.Width);
            lineHeight = Math.Max(lineHeight, size.Height);
            baseline = Math.Max(baseline, lineBaseline);
        }

        var step = lineHeight + baseline + 2;
        var chipHeight = (step * lines.Count) + (paddingY * 2);
        var chipWidth = width + (paddingX * 2);

        // Si no cabe encima de la caja (contenedor pegado al borde superior) la
        // etiqueta se dibuja dentro.
        var top = box.Y - chipHeight >= 0 ? box.Y - chipHeight : box.Y;
        var left = Math.Min(box.X, Math.Max(0, frame.Width - chipWidth));

        var chip = new Rect(left, top, chipWidth, chipHeight) & new Rect(0, 0, frame.Width, frame.Height);
        if (chip.Width <= 0 || chip.Height <= 0)
        {
            return;
        }

        Cv2.Rectangle(frame, chip, color, -1);

        var textColor = Palette.TextOn(color);
        for (var index = 0; index < lines.Count; index++)
        {
            Cv2.PutText(
                frame,
                lines[index],
                new Point(chip.X + paddingX, chip.Y + paddingY + lineHeight + (step * index)),
                Font,
                _options.FontScale,
                textColor,
                1,
                LineTypes.AntiAlias);
        }
    }

    private void DrawHud(
        Mat frame,
        int trackCount,
        IReadOnlyDictionary<int, ContainerIdentification> identifications,
        int frameIndex,
        double fps)
    {
        var confirmed = identifications.Values
            .Where(identification => identification.IsConfirmed)
            .OrderByDescending(identification => identification.LastFrame)
            .Take(_options.HudCodeCount)
            .Select(identification => identification.Code.Value)
            .ToList();

        var lines = new List<string>
        {
            $"Video2BIC  frame {frameIndex}  {fps:F1} FPS  contenedores {trackCount}  " +
            $"identificados {identifications.Values.Count(identification => identification.IsConfirmed)}",
        };

        if (confirmed.Count > 0)
        {
            lines.Add(string.Join("  ", confirmed));
        }

        var width = 0;
        var lineHeight = 0;
        var baseline = 0;

        foreach (var line in lines)
        {
            var size = Cv2.GetTextSize(line, Font, _options.FontScale, 1, out var lineBaseline);
            width = Math.Max(width, size.Width);
            lineHeight = Math.Max(lineHeight, size.Height);
            baseline = Math.Max(baseline, lineBaseline);
        }

        var step = lineHeight + baseline + 4;
        var panel = new Rect(0, 0, Math.Min(frame.Width, width + 16), (step * lines.Count) + 8);
        if (panel.Width <= 0 || panel.Height <= 0 || panel.Height > frame.Height)
        {
            return;
        }

        using (var region = frame[panel])
        using (var dark = new Mat(region.Size(), region.Type(), Scalar.All(0)))
        using (var blended = new Mat())
        {
            Cv2.AddWeighted(region, 0.35d, dark, 0.65d, 0d, blended);
            blended.CopyTo(region);
        }

        for (var index = 0; index < lines.Count; index++)
        {
            Cv2.PutText(
                frame,
                lines[index],
                new Point(8, 6 + lineHeight + (step * index)),
                Font,
                _options.FontScale,
                new Scalar(255, 255, 255),
                1,
                LineTypes.AntiAlias);
        }
    }
}
