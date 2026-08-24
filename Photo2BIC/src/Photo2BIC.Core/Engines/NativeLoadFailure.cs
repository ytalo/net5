namespace Photo2BIC.Core.Engines;

/// <summary>
/// Traduce un fallo de carga de biblioteca nativa a algo accionable.
/// </summary>
/// <remarks>
/// <para>
/// OpenCV y ONNX Runtime llegan como binarios nativos compilados contra las
/// bibliotecas del sistema de una distribucion concreta. En otra puede faltar
/// alguna dependencia -<c>libtesseract.so.4</c>, <c>libtiff.so.5</c>- y entonces
/// no falla la compilacion ni el arranque, sino la primera llamada que necesita
/// esa parte de la biblioteca.
/// </para>
/// <para>
/// El mensaje del runtime en ese caso es <c>EntryPointNotFoundException</c> o
/// <c>DllNotFoundException</c> con un volcado de rutas, que no le dice nada a
/// quien solo queria leer un codigo. Se reescribe para nombrar la causa y la
/// salida: los motores de nube no dependen de nada de esto y siguen funcionando.
/// </para>
/// </remarks>
public static class NativeLoadFailure
{
    /// <summary>
    /// <c>true</c> si la excepcion es un fallo de carga de biblioteca nativa.
    /// </summary>
    public static bool IsNativeLoadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is DllNotFoundException or EntryPointNotFoundException
               || (exception is TypeInitializationException && exception.InnerException is not null
                                                            && IsNativeLoadFailure(exception.InnerException));
    }

    /// <summary>Explicacion legible del fallo.</summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var root = exception;
        while (root is TypeInitializationException { InnerException: { } inner })
        {
            root = inner;
        }

        return "falta una biblioteca nativa o alguna de sus dependencias del sistema " +
               $"({root.Message.Split('\n')[0].Trim()}). " +
               "Los binarios de OpenCV que trae el paquete NuGet se compilan contra una " +
               "distribucion concreta; en otra puede hacer falta instalar sus dependencias. " +
               "Los motores de nube no dependen de esto y siguen disponibles.";
    }
}
