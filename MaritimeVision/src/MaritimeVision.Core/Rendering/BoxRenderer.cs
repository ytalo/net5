using MaritimeVision.Core.Models;
using OpenCvSharp;

namespace MaritimeVision.Core.Rendering;

/// <summary>
/// Dibuja sobre el fotograma las cajas, las identidades y la estela de cada objeto.
/// </summary>
/// <remarks>
/// Mantiene el historico de centros por identidad, asi que hay que usar una
/// instancia por secuencia de video. No es segura para uso concurrente.
/// </remarks>
public sealed class BoxRenderer
{
    private const HersheyFonts Font = HersheyFonts.HersheyDuplex;

    private readonly RenderOptions _options;
    private readonly Dictionary<int, Queue<Point>> _trails = [];
    private readonly HashSet<int> _seenThisFrame = [];

    public BoxRenderer(RenderOptions? options = null) => _options = options ?? new RenderOptions();

    /// <summary>Descarta el historico de estelas.</summary>
    public void Reset() => _trails.Clear();

    /// <summary>
    /// Dibuja el analisis sobre <paramref name="frame"/>, modificandolo en el sitio.
    /// </summary>
    /// <param name="frame">Fotograma BGR sobre el que dibujar.</param>
    /// <param name="analysis">Resultado del analisis de ese fotograma.</param>
    /// <param name="fps">FPS de proceso a mostrar en el panel, si procede.</param>
    public void Draw(Mat frame, FrameAnalysis analysis, double fps = 0d)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(analysis);

        if (_options.ShowRawDetections)
        {
            DrawRawDetections(frame, analysis.Detections);
        }

        _seenThisFrame.Clear();

        foreach (var track in analysis.Tracks)
        {
            _seenThisFrame.Add(track.TrackId);
            var color = _options.ColorMode == ColorMode.ByClass
                ? Palette.ForIndex(track.ClassId)
                : Palette.ForIndex(track.TrackId);

            if (_options.ShowTrails)
            {
                DrawTrail(frame, track, color);
            }

            DrawTrack(frame, track, color);
        }

        PruneTrails();

        if (_options.ShowHud)
        {
            DrawHud(frame, analysis, fps);
        }
    }

    private void DrawRawDetections(Mat frame, IReadOnlyList<Detection> detections)
    {
        var color = new Scalar(140, 140, 140);
        foreach (var detection in detections)
        {
            var box = detection.Box.ClampTo(frame.Width, frame.Height);
            Cv2.Rectangle(frame, ToRect(box), color, 1);
        }
    }

    private void DrawTrack(Mat frame, in TrackedObject track, Scalar color)
    {
        var box = track.Box.ClampTo(frame.Width, frame.Height);
        var rect = ToRect(box);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        // Un track sin deteccion en este fotograma se dibuja punteado: su caja es
        // una extrapolacion del filtro de Kalman, no una observacion.
        if (track.IsConfirmed)
        {
            Cv2.Rectangle(frame, rect, color, _options.BoxThickness);
        }
        else
        {
            DrawDashedRectangle(frame, rect, color, _options.BoxThickness);
        }

        if (!_options.ShowLabels)
        {
            return;
        }

        var text = _options.ShowConfidence
            ? $"#{track.TrackId} {track.Label} {track.Score:P0}"
            : $"#{track.TrackId} {track.Label}";

        DrawLabel(frame, rect, text, color);
    }

    private void DrawLabel(Mat frame, Rect box, string text, Scalar color)
    {
        const int paddingX = 4;
        const int paddingY = 3;

        var size = Cv2.GetTextSize(text, Font, _options.FontScale, 1, out var baseline);
        var chipHeight = size.Height + baseline + (paddingY * 2);
        var chipWidth = size.Width + (paddingX * 2);

        // Si no cabe encima de la caja (objeto pegado al borde superior) se dibuja dentro.
        var top = box.Y - chipHeight >= 0 ? box.Y - chipHeight : box.Y;
        var left = Math.Min(box.X, Math.Max(0, frame.Width - chipWidth));

        var chip = new Rect(left, top, chipWidth, chipHeight);
        Cv2.Rectangle(frame, chip, color, -1);
        Cv2.PutText(
            frame,
            text,
            new Point(chip.X + paddingX, chip.Y + size.Height + paddingY),
            Font,
            _options.FontScale,
            Palette.TextOn(color),
            1,
            LineTypes.AntiAlias);
    }

    private void DrawTrail(Mat frame, in TrackedObject track, Scalar color)
    {
        if (!_trails.TryGetValue(track.TrackId, out var trail))
        {
            trail = new Queue<Point>();
            _trails[track.TrackId] = trail;
        }

        trail.Enqueue(new Point((int)track.Box.CenterX, (int)track.Box.CenterY));
        while (trail.Count > _options.TrailLength)
        {
            trail.Dequeue();
        }

        var points = trail.ToArray();
        for (var i = 1; i < points.Length; i++)
        {
            // La estela se adelgaza hacia el pasado para sugerir la direccion del movimiento.
            var thickness = Math.Max(1, (int)Math.Round(_options.BoxThickness * (double)i / points.Length));
            Cv2.Line(frame, points[i - 1], points[i], color, thickness, LineTypes.AntiAlias);
        }
    }

    private void PruneTrails()
    {
        if (_trails.Count == 0)
        {
            return;
        }

        var stale = _trails.Keys.Where(id => !_seenThisFrame.Contains(id)).ToList();
        foreach (var id in stale)
        {
            _trails.Remove(id);
        }
    }

    private void DrawHud(Mat frame, FrameAnalysis analysis, double fps)
    {
        var counts = analysis.Tracks
            .GroupBy(track => track.Label)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key}: {group.Count()}")
            .Take(4);

        var summary = string.Join("  ", counts);
        var text = $"frame {analysis.FrameIndex}  {fps:F1} FPS  tracks {analysis.Tracks.Count}";
        if (summary.Length > 0)
        {
            text += $"  |  {summary}";
        }

        var size = Cv2.GetTextSize(text, Font, _options.FontScale, 1, out var baseline);
        var panel = new Rect(0, 0, Math.Min(frame.Width, size.Width + 16), size.Height + baseline + 12);

        using (var overlay = frame[panel].Clone())
        {
            using var dark = new Mat(overlay.Size(), overlay.Type(), new Scalar(0, 0, 0));
            using var blended = new Mat();
            Cv2.AddWeighted(overlay, 0.35d, dark, 0.65d, 0d, blended);
            blended.CopyTo(frame[panel]);
        }

        Cv2.PutText(
            frame,
            text,
            new Point(8, size.Height + 6),
            Font,
            _options.FontScale,
            new Scalar(255, 255, 255),
            1,
            LineTypes.AntiAlias);
    }

    private static void DrawDashedRectangle(Mat frame, Rect rect, Scalar color, int thickness)
    {
        const int dash = 8;
        const int gap = 6;

        DrawDashedLine(frame, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top), color, thickness);
        DrawDashedLine(frame, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom), color, thickness);
        DrawDashedLine(frame, new Point(rect.Right, rect.Bottom), new Point(rect.Left, rect.Bottom), color, thickness);
        DrawDashedLine(frame, new Point(rect.Left, rect.Bottom), new Point(rect.Left, rect.Top), color, thickness);

        static void DrawDashedLine(Mat frame, Point from, Point to, Scalar color, int thickness)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 1d)
            {
                return;
            }

            var stepX = dx / length;
            var stepY = dy / length;

            for (var position = 0d; position < length; position += dash + gap)
            {
                var end = Math.Min(position + dash, length);
                var start = new Point(from.X + (stepX * position), from.Y + (stepY * position));
                var finish = new Point(from.X + (stepX * end), from.Y + (stepY * end));
                Cv2.Line(frame, start, finish, color, thickness, LineTypes.AntiAlias);
            }
        }
    }

    private static Rect ToRect(in Models.BoundingBox box) => new(
        (int)Math.Round(box.X),
        (int)Math.Round(box.Y),
        (int)Math.Round(box.Width),
        (int)Math.Round(box.Height));
}
