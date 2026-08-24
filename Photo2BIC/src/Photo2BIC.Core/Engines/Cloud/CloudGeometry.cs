using System.Text.Json;

namespace Photo2BIC.Core.Engines.Cloud;

/// <summary>
/// Convierte las geometrias que devuelven los servicios en la caja rectangular
/// que usa <c>TextFragment</c>.
/// </summary>
/// <remarks>
/// Cada servicio la expresa a su manera -poligono de cuatro vertices, rectangulo
/// normalizado, coordenadas sueltas- pero al analizador de codigos solo le hace
/// falta el orden de lectura, asi que basta con la caja envolvente.
/// </remarks>
internal static class CloudGeometry
{
    /// <summary>
    /// Caja envolvente de un poligono expresado como lista de objetos con
    /// propiedades <c>x</c> e <c>y</c> (Azure) o <c>X</c> e <c>Y</c> (Google).
    /// </summary>
    public static (int Left, int Top, int Width, int Height) FromPolygon(JsonElement polygon)
    {
        if (polygon.ValueKind != JsonValueKind.Array)
        {
            return (0, 0, 0, 0);
        }

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        var points = 0;

        foreach (var vertex in polygon.EnumerateArray())
        {
            // Un vertice de Google puede venir sin 'x' o sin 'y' cuando valen cero:
            // el codificador de protobuf omite los campos por defecto. Tratar la
            // ausencia como cero es lo correcto, no como vertice invalido.
            var x = Number(vertex, "x") ?? Number(vertex, "X") ?? 0d;
            var y = Number(vertex, "y") ?? Number(vertex, "Y") ?? 0d;

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            points++;
        }

        if (points == 0)
        {
            return (0, 0, 0, 0);
        }

        return (
            (int)Math.Round(minX),
            (int)Math.Round(minY),
            (int)Math.Round(maxX - minX),
            (int)Math.Round(maxY - minY));
    }

    /// <summary>
    /// Caja de un rectangulo normalizado en [0, 1] (Textract), llevada a pixeles.
    /// </summary>
    public static (int Left, int Top, int Width, int Height) FromNormalizedBox(
        JsonElement box, int imageWidth, int imageHeight)
    {
        var left = Number(box, "Left") ?? 0d;
        var top = Number(box, "Top") ?? 0d;
        var width = Number(box, "Width") ?? 0d;
        var height = Number(box, "Height") ?? 0d;

        return (
            (int)Math.Round(left * imageWidth),
            (int)Math.Round(top * imageHeight),
            (int)Math.Round(width * imageWidth),
            (int)Math.Round(height * imageHeight));
    }

    /// <summary>Valor numerico de una propiedad, o <c>null</c> si no esta o no es numero.</summary>
    public static double? Number(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetDouble(out var number)
            ? number
            : null;

    /// <summary>Texto de una propiedad, o <c>null</c> si no esta o no es cadena.</summary>
    public static string? String(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Propiedad de tipo objeto o array, o <c>default</c> si no esta.</summary>
    public static JsonElement Child(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value
            : default;

    /// <summary>Elementos de un array; vacio si la propiedad no es un array.</summary>
    public static IEnumerable<JsonElement> Array(JsonElement element)
        => element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];
}
