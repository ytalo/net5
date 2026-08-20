namespace MaritimeVision.Core.Models;

/// <summary>
/// Caja delimitadora en coordenadas de pixel del fotograma original, expresada
/// como esquina superior izquierda mas tamano (formato <c>tlwh</c>).
/// </summary>
/// <remarks>
/// Es un <c>readonly record struct</c> para que copiarla sea barato: el pipeline
/// crea decenas de miles por minuto de video.
/// </remarks>
public readonly record struct BoundingBox(float X, float Y, float Width, float Height)
{
    /// <summary>Borde izquierdo.</summary>
    public float Left => X;

    /// <summary>Borde superior.</summary>
    public float Top => Y;

    /// <summary>Borde derecho (exclusivo).</summary>
    public float Right => X + Width;

    /// <summary>Borde inferior (exclusivo).</summary>
    public float Bottom => Y + Height;

    /// <summary>Centro horizontal.</summary>
    public float CenterX => X + (Width * 0.5f);

    /// <summary>Centro vertical.</summary>
    public float CenterY => Y + (Height * 0.5f);

    /// <summary>Area en pixeles cuadrados. Nunca negativa.</summary>
    public float Area => Math.Max(0f, Width) * Math.Max(0f, Height);

    /// <summary>Relacion ancho/alto. Devuelve 0 si la altura es degenerada.</summary>
    public float AspectRatio => Height > float.Epsilon ? Width / Height : 0f;

    /// <summary>Construye la caja a partir de dos esquinas (formato <c>xyxy</c>).</summary>
    public static BoundingBox FromCorners(float left, float top, float right, float bottom)
        => new(left, top, right - left, bottom - top);

    /// <summary>
    /// Construye la caja a partir de centro, ancho y alto (formato <c>cxcywh</c>,
    /// el que emiten las cabezas de deteccion de YOLO).
    /// </summary>
    public static BoundingBox FromCenter(float centerX, float centerY, float width, float height)
        => new(centerX - (width * 0.5f), centerY - (height * 0.5f), width, height);

    /// <summary>
    /// Construye la caja desde el estado del filtro de Kalman
    /// (<c>centro x</c>, <c>centro y</c>, <c>aspecto</c>, <c>alto</c>).
    /// </summary>
    public static BoundingBox FromXyah(float centerX, float centerY, float aspect, float height)
    {
        var width = aspect * height;
        return new BoundingBox(centerX - (width * 0.5f), centerY - (height * 0.5f), width, height);
    }

    /// <summary>Proyecta la caja al espacio de estado del filtro de Kalman.</summary>
    public (float CenterX, float CenterY, float Aspect, float Height) ToXyah()
        => (CenterX, CenterY, AspectRatio, Height);

    /// <summary>Interseccion con otra caja. Devuelve una caja vacia si no se solapan.</summary>
    public BoundingBox Intersect(in BoundingBox other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? default : FromCorners(left, top, right, bottom);
    }

    /// <summary>
    /// Intersection over Union con otra caja, en el rango [0, 1].
    /// Es la metrica de similitud que usa la asociacion de ByteTrack.
    /// </summary>
    public float IntersectionOverUnion(in BoundingBox other)
    {
        var intersection = Intersect(other).Area;
        if (intersection <= 0f)
        {
            return 0f;
        }

        var union = Area + other.Area - intersection;
        return union > float.Epsilon ? intersection / union : 0f;
    }

    /// <summary>
    /// Recorta la caja al rectangulo <c>[0, width) x [0, height)</c> del fotograma.
    /// Evita que un track extrapolado por Kalman se dibuje fuera de la imagen.
    /// </summary>
    public BoundingBox ClampTo(int frameWidth, int frameHeight)
    {
        var left = Math.Clamp(Left, 0f, frameWidth);
        var top = Math.Clamp(Top, 0f, frameHeight);
        var right = Math.Clamp(Right, 0f, frameWidth);
        var bottom = Math.Clamp(Bottom, 0f, frameHeight);
        return FromCorners(left, top, Math.Max(left, right), Math.Max(top, bottom));
    }

    public override string ToString()
        => $"[x={X:F1} y={Y:F1} w={Width:F1} h={Height:F1}]";
}
