using OpenCvSharp;
using Video2BIC.Core.Imaging;
using Video2BIC.Core.Ocr;
using Video2BIC.Core.Text;
using Video2BIC.Core.TextRegions;

namespace Photo2BIC.Core.Engines.Local;

/// <summary>
/// Recorrido comun de los motores locales: localizar las lineas de texto,
/// prepararlas y pasarselas al reconocedor.
/// </summary>
/// <remarks>
/// <para>
/// Un reconocedor local espera un recorte de una sola linea, ya enderezado, y no
/// sabe buscarlo por su cuenta. Los servicios remotos si lo hacen, y por eso no
/// pasan por aqui: esta clase es exactamente la parte del trabajo que un servicio
/// de nube resuelve dentro de su propia llamada.
/// </para>
/// <para>
/// Las coordenadas de cada trozo se devuelven referidas a la fotografia, no al
/// recorte, para que el informe pueda decir donde estaba el codigo y para que el
/// orden de lectura de los trozos sea el correcto al recomponer un codigo
/// repartido en varias lineas.
/// </para>
/// </remarks>
internal static class LocalTextReader
{
    /// <summary>
    /// Lee toda la fotografia.
    /// </summary>
    /// <param name="photo">Fotografia o variante preprocesada.</param>
    /// <param name="recognizer">Reconocedor de linea.</param>
    /// <param name="regions">
    /// Localizador de lineas. Solo se usa si el reconocedor no analiza la
    /// disposicion por su cuenta.
    /// </param>
    /// <param name="preprocess">Preparacion de cada recorte de linea.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    public static IReadOnlyList<TextFragment> Read(
        Mat photo,
        ITextRecognizer recognizer,
        ITextRegionDetector regions,
        TextPreprocessOptions preprocess,
        CancellationToken cancellationToken)
    {
        // Con analisis de disposicion propio -Tesseract en modo texto disperso- se
        // le entrega la imagen entera y se deja que busque el solo: una sola llamada
        // en vez de una por linea.
        if (recognizer.PerformsLayoutAnalysis)
        {
            return recognizer.Recognize(photo);
        }

        var lines = regions.Detect(photo);
        if (lines.Count == 0)
        {
            return Array.Empty<TextFragment>();
        }

        var fragments = new List<TextFragment>();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var box = line.Box & new Rect(0, 0, photo.Width, photo.Height);
            if (box.Width < 8 || box.Height < 8)
            {
                continue;
            }

            using var crop = new Mat(photo, box);
            using var prepared = TextImagePreprocessor.Prepare(crop, preprocess);

            if (prepared.Empty())
            {
                continue;
            }

            // El preprocesado puede haber ampliado el recorte, asi que las
            // coordenadas que devuelve el reconocedor van en la escala del recorte
            // preparado y hay que deshacerla antes de sumarle el origen de la linea.
            var scaleX = (double)box.Width / prepared.Width;
            var scaleY = (double)box.Height / prepared.Height;

            foreach (var fragment in recognizer.Recognize(prepared))
            {
                fragments.Add(fragment with
                {
                    Left = box.X + (int)Math.Round(fragment.Left * scaleX),
                    Top = box.Y + (int)Math.Round(fragment.Top * scaleY),
                    Width = (int)Math.Round(fragment.Width * scaleX),
                    Height = (int)Math.Round(fragment.Height * scaleY),
                });
            }
        }

        return fragments;
    }
}
