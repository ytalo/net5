using OpenCvSharp;
using Photo2BIC.Core.Imaging;
using Xunit;

namespace Photo2BIC.Tests;

/// <summary>
/// Las variantes de preprocesado, que son las que fabrican la variedad que en
/// video daban los fotogramas.
/// </summary>
public class PhotoVariantFactoryTests
{
    [Fact]
    public void AlwaysPutsTheUntouchedPhotographFirst()
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383");
        using var variants = new PhotoVariantFactory().Create(photo);

        Assert.Equal("original", variants[0].Name);
        Assert.Equal(photo.Channels(), variants[0].Image.Channels());
        Assert.Equal(photo.Size(), variants[0].Image.Size());
    }

    /// <summary>
    /// La variante original es una copia. Si fuera la imagen del llamante, liberar
    /// el juego de variantes le dejaria la fotografia liberada bajo los pies.
    /// </summary>
    [Fact]
    public void CopiesTheOriginalSoDisposingTheSetDoesNotTouchTheCallerImage()
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383");

        using (var variants = new PhotoVariantFactory().Create(photo))
        {
            Assert.NotSame(photo, variants[0].Image);
        }

        Assert.False(photo.IsDisposed);
        Assert.False(photo.Empty());
    }

    [Fact]
    public void GeneratesTheConfiguredVariantsAndNoOthers()
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383", width: 900);
        using var variants = new PhotoVariantFactory().Create(photo);

        Assert.Equal(
            ["original", "clahe", "otsu", "adaptativa", "invertida", "enfocada", "ampliada"],
            variants.Select(variant => variant.Name));
    }

    [Fact]
    public void GeneratesOnlyTheOriginalWhenEverythingElseIsSwitchedOff()
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383");
        using var variants = new PhotoVariantFactory(PhotoVariantOptions.OriginalOnly()).Create(photo);

        Assert.Equal("original", Assert.Single(variants).Name);
    }

    /// <summary>
    /// Ampliar una fotografia que ya viene grande no anade informacion y multiplica
    /// el coste de cada motor.
    /// </summary>
    [Fact]
    public void DoesNotUpscaleAPhotographThatIsAlreadyBigEnough()
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383", width: 1600, height: 1000);
        using var variants = new PhotoVariantFactory().Create(photo);

        Assert.DoesNotContain(variants, variant => variant.Name == "ampliada");
    }

    [Fact]
    public void DoublesTheSizeInTheUpscaledVariant()
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383", width: 600, height: 400);
        using var variants = new PhotoVariantFactory().Create(photo);

        var upscaled = variants.Single(variant => variant.Name == "ampliada");
        Assert.Equal(1200, upscaled.Image.Width);
    }

    /// <summary>
    /// Las binarizadas tienen que quedar con tinta oscura sobre fondo claro sea
    /// cual sea el color del panel, que es lo que espera cualquier OCR.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LeavesTheBinarisedVariantWithDarkInkOnALightBackground(bool lightTextOnDarkPanel)
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383", lightTextOnDarkPanel: lightTextOnDarkPanel);
        using var variants = new PhotoVariantFactory().Create(photo);

        var otsu = variants.Single(variant => variant.Name == "otsu");
        var bright = Cv2.CountNonZero(otsu.Image);

        Assert.True(bright * 2 > otsu.Image.Rows * otsu.Image.Cols);
    }

    [Fact]
    public void ReturnsAnEmptySetForAnEmptyImage()
    {
        using var photo = new Mat();
        using var variants = new PhotoVariantFactory().Create(photo);

        Assert.Empty(variants);
    }

    [Fact]
    public void KeepsTheFirstOnesWhenTheBudgetIsSmallerThanTheSet()
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383");
        using var variants = new PhotoVariantFactory().Create(photo);

        var taken = variants.Take(2);

        Assert.Equal(["original", "clahe"], taken.Select(variant => variant.Name));
    }

    [Fact]
    public void RejectsAnImpossibleConfiguration()
    {
        var options = new PhotoVariantOptions { ClaheClipLimit = 0d };

        Assert.Throws<InvalidOperationException>(() => new PhotoVariantFactory(options));
    }
}

/// <summary>Carga y codificacion de fotografias.</summary>
public class PhotoLoaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "photo2bic-tests", Guid.NewGuid().ToString("n"));

    public PhotoLoaderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void LoadsAPhotographFromDisk()
    {
        var path = SyntheticPhoto.Write(Path.Combine(_directory, "puerta.jpg"), "CSQU3054383");

        using var photo = PhotoLoader.Load(path);

        Assert.False(photo.Empty());
        Assert.Equal(3, photo.Channels());
    }

    /// <summary>
    /// Un movil entrega 4000 pixeles de ancho. No mejoran el OCR y multiplican por
    /// seis el coste de cada motor.
    /// </summary>
    [Fact]
    public void ReducesAPhotographWiderThanTheLimitKeepingItsProportions()
    {
        using var big = new Mat(1500, 3000, MatType.CV_8UC3, new Scalar(120, 120, 120));

        using var reduced = PhotoLoader.Downscale(big, 1600);

        Assert.Equal(1600, reduced.Width);
        Assert.Equal(800, reduced.Height);
    }

    [Fact]
    public void LeavesAPhotographThatAlreadyFitsUntouched()
    {
        using var small = new Mat(400, 800, MatType.CV_8UC3, new Scalar(120, 120, 120));

        var result = PhotoLoader.Downscale(small, 1600);

        Assert.Same(small, result);
    }

    [Fact]
    public void SaysWhichFileIsMissingInsteadOfFailingObscurely()
    {
        var ex = Assert.Throws<FileNotFoundException>(
            () => PhotoLoader.Load(Path.Combine(_directory, "no-existe.jpg")));

        Assert.Contains("no-existe.jpg", ex.Message);
    }

    [Fact]
    public void ExplainsThatAFileIsNotAnImage()
    {
        var path = Path.Combine(_directory, "texto.jpg");
        File.WriteAllText(path, "esto no es una imagen");

        var ex = Assert.Throws<InvalidOperationException>(() => PhotoLoader.Load(path));

        Assert.Contains("no se pudo leer", ex.Message);
    }

    [Fact]
    public void EncodesAnImageAsJpegWithTheUsualHeader()
    {
        using var photo = SyntheticPhoto.Door("CSQU3054383");

        var jpeg = PhotoLoader.EncodeJpeg(photo);

        Assert.True(jpeg.Length > 1000);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
    }

    /// <summary>
    /// Varios servicios interpretan mal un JPEG de un solo canal, asi que las
    /// variantes en escala de grises se envian en tres.
    /// </summary>
    [Fact]
    public void EncodesAGrayscaleVariantAsAThreeChannelJpeg()
    {
        using var gray = new Mat(200, 300, MatType.CV_8UC1, new Scalar(128));

        var jpeg = PhotoLoader.EncodeJpeg(gray);
        using var decoded = Cv2.ImDecode(jpeg, ImreadModes.Unchanged);

        Assert.Equal(3, decoded.Channels());
    }

    [Fact]
    public void ListsOnlyTheImageFilesOfAFolderInOrder()
    {
        SyntheticPhoto.Write(Path.Combine(_directory, "b.jpg"), "CSQU3054383");
        SyntheticPhoto.Write(Path.Combine(_directory, "a.png"), "MSKU1234565");
        File.WriteAllText(Path.Combine(_directory, "notas.txt"), "no soy una foto");

        var photos = PhotoLoader.Enumerate(_directory);

        Assert.Equal(["a.png", "b.jpg"], photos.Select(Path.GetFileName));
    }

    [Fact]
    public void EntersTheSubfoldersOnlyWhenAsked()
    {
        SyntheticPhoto.Write(Path.Combine(_directory, "raiz.jpg"), "CSQU3054383");
        SyntheticPhoto.Write(Path.Combine(_directory, "dentro", "hija.jpg"), "MSKU1234565");

        Assert.Single(PhotoLoader.Enumerate(_directory));
        Assert.Equal(2, PhotoLoader.Enumerate(_directory, recursive: true).Count);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
