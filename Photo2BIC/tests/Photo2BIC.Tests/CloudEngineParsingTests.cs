using Photo2BIC.Core.Engines.Cloud;
using Xunit;

namespace Photo2BIC.Tests;

/// <summary>
/// Interpretacion de las respuestas de Azure AI Vision.
/// </summary>
public class AzureVisionParsingTests
{
    private const string Response =
        """
        {
          "modelVersion": "2023-10-01",
          "metadata": { "width": 1600, "height": 1200 },
          "readResult": {
            "blocks": [
              {
                "lines": [
                  {
                    "text": "MSKU 123456 5",
                    "boundingPolygon": [
                      {"x": 120, "y": 200}, {"x": 700, "y": 205},
                      {"x": 700, "y": 280}, {"x": 120, "y": 275}
                    ],
                    "words": [
                      {
                        "text": "MSKU",
                        "boundingPolygon": [
                          {"x": 120, "y": 200}, {"x": 320, "y": 202},
                          {"x": 320, "y": 278}, {"x": 120, "y": 275}
                        ],
                        "confidence": 0.978
                      },
                      {
                        "text": "1234565",
                        "boundingPolygon": [
                          {"x": 340, "y": 202}, {"x": 700, "y": 205},
                          {"x": 700, "y": 280}, {"x": 340, "y": 278}
                        ],
                        "confidence": 0.941
                      }
                    ]
                  },
                  {
                    "text": "22G1",
                    "boundingPolygon": [
                      {"x": 130, "y": 400}, {"x": 300, "y": 400},
                      {"x": 300, "y": 460}, {"x": 130, "y": 460}
                    ],
                    "words": [
                      {
                        "text": "22G1",
                        "boundingPolygon": [
                          {"x": 130, "y": 400}, {"x": 300, "y": 400},
                          {"x": 300, "y": 460}, {"x": 130, "y": 460}
                        ],
                        "confidence": 0.902
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

    [Fact]
    public void ReadsTheWordsWithTheirConfidenceAndBox()
    {
        var fragments = AzureVisionEngine.Parse(Response);

        Assert.Equal(3, fragments.Count);
        Assert.Equal("MSKU", fragments[0].Text);
        Assert.Equal(0.978f, fragments[0].Confidence, 3);
        Assert.Equal(120, fragments[0].Left);
        Assert.Equal(200, fragments[0].Top);
        Assert.Equal(200, fragments[0].Width);
    }

    [Fact]
    public void KeepsTheReadingOrderSoTheCodeCanBeRebuilt()
    {
        var fragments = AzureVisionEngine.Parse(Response);

        Assert.Equal(["MSKU", "1234565", "22G1"], fragments.Select(fragment => fragment.Text));
    }

    /// <summary>
    /// Una linea sin palabras se toma al nivel de linea. Pasa con algunas versiones
    /// del modelo, y perder la linea entera por eso seria absurdo.
    /// </summary>
    [Fact]
    public void FallsBackToTheLineWhenThereAreNoWords()
    {
        var fragments = AzureVisionEngine.Parse(
            """
            {"readResult":{"blocks":[{"lines":[
              {"text":"CSQU3054383","boundingPolygon":[{"x":10,"y":20},{"x":210,"y":20},
               {"x":210,"y":70},{"x":10,"y":70}]}
            ]}]}}
            """);

        var fragment = Assert.Single(fragments);
        Assert.Equal("CSQU3054383", fragment.Text);
        Assert.Equal(200, fragment.Width);
    }

    [Fact]
    public void ReturnsNothingWhenTheImageHasNoText()
    {
        Assert.Empty(AzureVisionEngine.Parse("""{"readResult":{"blocks":[]}}"""));
    }

    [Fact]
    public void SurvivesAResponseWithoutAReadResult()
    {
        Assert.Empty(AzureVisionEngine.Parse("""{"modelVersion":"2023-10-01"}"""));
    }
}

/// <summary>
/// Interpretacion de las respuestas de Google Cloud Vision.
/// </summary>
public class GoogleVisionParsingTests
{
    [Fact]
    public void PrefersTheFullTextAnnotationBecauseItCarriesConfidence()
    {
        var fragments = GoogleVisionEngine.Parse(
            """
            {"responses":[{
              "textAnnotations":[
                {"description":"MSKU 1234565","boundingPoly":{"vertices":[{"x":1,"y":1}]}},
                {"description":"MSKU","boundingPoly":{"vertices":[{"x":1,"y":1}]}}
              ],
              "fullTextAnnotation":{"pages":[{"blocks":[{"paragraphs":[{"words":[
                {"symbols":[{"text":"M"},{"text":"S"},{"text":"K"},{"text":"U"}],
                 "confidence":0.96,
                 "boundingBox":{"vertices":[{"x":100,"y":50},{"x":260,"y":50},
                                            {"x":260,"y":120},{"x":100,"y":120}]}},
                {"symbols":[{"text":"1"},{"text":"2"},{"text":"3"},{"text":"4"},
                            {"text":"5"},{"text":"6"},{"text":"5"}],
                 "confidence":0.88,
                 "boundingBox":{"vertices":[{"x":280,"y":50},{"x":520,"y":50},
                                            {"x":520,"y":120},{"x":280,"y":120}]}}
              ]}]}]}]}
            }]}
            """,
            0.8f);

        Assert.Equal(2, fragments.Count);
        Assert.Equal("MSKU", fragments[0].Text);
        Assert.Equal(0.96f, fragments[0].Confidence, 3);
        Assert.Equal("1234565", fragments[1].Text);
        Assert.Equal(160, fragments[0].Width);
    }

    /// <summary>
    /// Sin <c>fullTextAnnotation</c> se cae a <c>textAnnotations</c>, saltandose el
    /// primer elemento, que es la transcripcion completa y no una palabra.
    /// </summary>
    [Fact]
    public void SkipsTheWholeImageTranscriptionInTheFallback()
    {
        var fragments = GoogleVisionEngine.Parse(
            """
            {"responses":[{"textAnnotations":[
              {"description":"MSKU 1234565\n22G1","boundingPoly":{"vertices":[{"x":0,"y":0}]}},
              {"description":"MSKU","boundingPoly":{"vertices":[{"x":100,"y":50},{"x":260,"y":120}]}},
              {"description":"1234565","boundingPoly":{"vertices":[{"x":280,"y":50},{"x":520,"y":120}]}},
              {"description":"22G1","boundingPoly":{"vertices":[{"x":100,"y":200},{"x":230,"y":260}]}}
            ]}]}
            """,
            0.8f);

        Assert.Equal(["MSKU", "1234565", "22G1"], fragments.Select(fragment => fragment.Text));
        Assert.All(fragments, fragment => Assert.Equal(0.8f, fragment.Confidence, 3));
    }

    /// <summary>
    /// El codificador de protobuf omite los campos que valen cero, asi que un
    /// vertice en el origen llega sin <c>x</c> ni <c>y</c>.
    /// </summary>
    [Fact]
    public void TreatsAMissingVertexCoordinateAsZero()
    {
        var fragments = GoogleVisionEngine.Parse(
            """
            {"responses":[{"textAnnotations":[
              {"description":"todo"},
              {"description":"MSKU","boundingPoly":{"vertices":[{"y":10},{"x":200,"y":80}]}}
            ]}]}
            """,
            0.8f);

        var fragment = Assert.Single(fragments);
        Assert.Equal(0, fragment.Left);
        Assert.Equal(10, fragment.Top);
        Assert.Equal(200, fragment.Width);
    }

    /// <summary>Cloud Vision devuelve 200 con el error dentro del cuerpo.</summary>
    [Fact]
    public void RaisesTheErrorThatTravelsInsideASuccessfulResponse()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GoogleVisionEngine.Parse(
            """{"responses":[{"error":{"code":7,"message":"This API method requires billing"}}]}""",
            0.8f));

        Assert.Contains("requires billing", ex.Message);
    }

    [Fact]
    public void ReturnsNothingWhenTheImageHasNoText()
    {
        Assert.Empty(GoogleVisionEngine.Parse("""{"responses":[{}]}""", 0.8f));
    }
}

/// <summary>
/// Interpretacion de las respuestas de Amazon Textract.
/// </summary>
public class TextractParsingTests
{
    private const string Response =
        """
        {
          "DocumentMetadata": { "Pages": 1 },
          "Blocks": [
            { "BlockType": "PAGE", "Id": "p1" },
            {
              "BlockType": "LINE", "Text": "MSKU 1234565", "Confidence": 99.1,
              "Geometry": { "BoundingBox": { "Width": 0.5, "Height": 0.1, "Left": 0.1, "Top": 0.2 } }
            },
            {
              "BlockType": "WORD", "Text": "MSKU", "Confidence": 99.4,
              "Geometry": { "BoundingBox": { "Width": 0.2, "Height": 0.1, "Left": 0.1, "Top": 0.2 } }
            },
            {
              "BlockType": "WORD", "Text": "1234565", "Confidence": 97.2,
              "Geometry": { "BoundingBox": { "Width": 0.25, "Height": 0.1, "Left": 0.35, "Top": 0.2 } }
            },
            { "BlockType": "WORD", "Text": "ruido", "Confidence": 8.0 }
          ]
        }
        """;

    /// <summary>
    /// Textract devuelve el mismo texto como pagina, linea y palabra. Contar las
    /// tres seria triplicar la evidencia de una sola lectura.
    /// </summary>
    [Fact]
    public void TakesOnlyTheWordLevelToAvoidCountingTheSameReadingThreeTimes()
    {
        var fragments = AwsTextractEngine.Parse(Response, 1000, 800, 0.2f);

        Assert.Equal(["MSKU", "1234565"], fragments.Select(fragment => fragment.Text));
    }

    [Fact]
    public void TurnsTheNormalisedBoxIntoPixels()
    {
        var fragments = AwsTextractEngine.Parse(Response, 1000, 800, 0.2f);

        Assert.Equal(100, fragments[0].Left);
        Assert.Equal(160, fragments[0].Top);
        Assert.Equal(200, fragments[0].Width);
        Assert.Equal(80, fragments[0].Height);
    }

    [Fact]
    public void TurnsThePercentageConfidenceIntoAFraction()
    {
        var fragments = AwsTextractEngine.Parse(Response, 1000, 800, 0.2f);

        Assert.Equal(0.994f, fragments[0].Confidence, 3);
    }

    [Fact]
    public void DropsTheWordsBelowTheConfidenceFloor()
    {
        var fragments = AwsTextractEngine.Parse(Response, 1000, 800, 0.2f);

        Assert.DoesNotContain(fragments, fragment => fragment.Text == "ruido");
    }

    [Fact]
    public void ReturnsNothingWhenThereAreNoBlocks()
    {
        Assert.Empty(AwsTextractEngine.Parse("""{"Blocks":[]}""", 100, 100, 0.2f));
    }
}

/// <summary>
/// Interpretacion de las respuestas de OCR.space.
/// </summary>
public class OcrSpaceParsingTests
{
    [Fact]
    public void ReadsTheWordsFromTheOverlayWithTheirPosition()
    {
        var fragments = OcrSpaceEngine.Parse(
            """
            {
              "ParsedResults": [{
                "TextOverlay": { "Lines": [
                  { "LineText": "MSKU 1234565", "Words": [
                    { "WordText": "MSKU", "Left": 120, "Top": 200, "Height": 70, "Width": 190 },
                    { "WordText": "1234565", "Left": 330, "Top": 202, "Height": 70, "Width": 350 }
                  ]},
                  { "LineText": "22G1", "Words": [
                    { "WordText": "22G1", "Left": 130, "Top": 400, "Height": 60, "Width": 170 }
                  ]}
                ], "HasOverlay": true },
                "ParsedText": "MSKU 1234565\n22G1\n",
                "FileParseExitCode": 1
              }],
              "OCRExitCode": 1,
              "IsErroredOnProcessing": false
            }
            """,
            0.7f);

        Assert.Equal(["MSKU", "1234565", "22G1"], fragments.Select(fragment => fragment.Text));
        Assert.Equal(120, fragments[0].Left);
        Assert.Equal(190, fragments[0].Width);
        Assert.All(fragments, fragment => Assert.Equal(0.7f, fragment.Confidence, 3));
    }

    /// <summary>
    /// Sin superposicion queda el texto plano, troceado por lineas para conservar
    /// el orden de lectura.
    /// </summary>
    [Fact]
    public void FallsBackToThePlainTextSplitByLines()
    {
        var fragments = OcrSpaceEngine.Parse(
            """
            {"ParsedResults":[{"TextOverlay":{"Lines":[],"HasOverlay":false},
              "ParsedText":"MSKU 1234565\r\n22G1\r\n"}],
             "IsErroredOnProcessing":false}
            """,
            0.7f);

        Assert.Equal(["MSKU 1234565", "22G1"], fragments.Select(fragment => fragment.Text));
    }

    [Fact]
    public void RaisesTheErrorThatTravelsInsideASuccessfulResponse()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OcrSpaceEngine.Parse(
            """
            {"IsErroredOnProcessing":true,
             "ErrorMessage":["E216: You may only perform this action upto maximum 10 number of times"]}
            """,
            0.7f));

        Assert.Contains("E216", ex.Message);
    }

    /// <summary>
    /// El servicio manda el error unas veces como cadena y otras como lista de
    /// cadenas, sin que nada en la respuesta anuncie cual toca.
    /// </summary>
    [Fact]
    public void AcceptsTheErrorAsAPlainStringToo()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OcrSpaceEngine.Parse(
            """{"IsErroredOnProcessing":true,"ErrorMessage":"Invalid API key"}""",
            0.7f));

        Assert.Contains("Invalid API key", ex.Message);
    }

    [Fact]
    public void ReturnsNothingWhenTheImageHasNoText()
    {
        Assert.Empty(OcrSpaceEngine.Parse(
            """{"ParsedResults":[{"ParsedText":"","TextOverlay":{"Lines":[]}}],"IsErroredOnProcessing":false}""",
            0.7f));
    }
}
