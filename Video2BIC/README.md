# Video2BIC

Aplicación .NET / C# que lee una secuencia de vídeo, **detecta contenedores
marítimos**, los sigue mientras cruzan la escena y **les lee el código BIC**
(ISO 6346) pintado en el panel.

El resultado no es «texto reconocido en el fotograma 37», sino una lista de
contenedores identificados, cada uno con su código validado, la confianza que lo
respalda y el intervalo de tiempo en que estuvo en escena.

```
$ video2bic scan --source puerto.mp4 --model models/containers.onnx --json informe.json

Entrada:  puerto.mp4
          1920x1080 a 25.00 FPS, 3750 fotogramas, 00:02:30
Detector: YOLO ONNX 'containers.onnx' entrada 640x640, 7 clases, proveedor Cpu
OCR:      Tesseract '5.3.4' psm 7, idioma eng

  >> contenedor #1: MSKU 123456 5 (94%, 5 lecturas)  22G1: 6058 mm (20 pies)...
  >> contenedor #4: CSQU 305438 3 (88%, 4 lecturas)  45R1: 12192 mm (40 pies)...

3750 fotogramas en 91.4s (41.0 FPS)
contenedores seguidos: 6, identificados: 5 (83%)
```

---

## Cómo funciona

| Etapa | Qué hace | Dónde está |
|---|---|---|
| **1. Vídeo** | Fichero, URL `rtsp://` / `http://`, o cámara conectada | `MaritimeVision.Core.Video` |
| **2. Detección** | YOLO sobre ONNX Runtime localiza los contenedores | `MaritimeVision.Core.Detectors` |
| **3. Seguimiento** | ByteTrack les da una identidad estable entre fotogramas | `MaritimeVision.Core.Tracking` |
| **4. Localización de texto** | MSER agrupa caracteres en líneas dentro del recorte | `TextRegions/` |
| **5. OCR** | Tesseract o un modelo CRNN/CTC en ONNX lee cada línea | `Ocr/` |
| **6. ISO 6346** | Reconstruye el código, lo repara y valida el dígito de control | `Bic/` |
| **7. Votación** | Acumula las lecturas de cada contenedor y decide su código | `Recognition/` |

Las tres primeras etapas se **reutilizan de [`MaritimeVision`](../MaritimeVision/)**,
que ya resolvía la detección y el seguimiento; Video2BIC referencia su biblioteca
`Core` en vez de duplicarla y aporta de la cuarta en adelante.

### Las dos ideas que hacen que esto funcione

**El dígito de control lo decide casi todo.** De los once caracteres de un código
BIC, el último está determinado por los diez anteriores. Eso convierte al OCR en
un problema mucho más fácil de lo que parece: **el 93 % de los errores de un solo
carácter se detectan** sin ninguna otra evidencia, así que se pueden probar
reparaciones agresivas (`0`↔`O`, `5`↔`S`, `8`↔`B`...) y dejar que la aritmética
descarte las equivocadas. Sin esa verificación, permitir correcciones convertiría
cualquier matrícula o rótulo publicitario en un código plausible.

**Un contenedor se ve muchas veces, no una.** Permanece decenas de fotogramas en
escena y se lee desde ángulos, distancias e iluminaciones distintas. La lectura
correcta se repite y las equivocadas se dispersan, así que **votar sobre el
conjunto es mucho más fiable que confiar en el mejor fotograma**. Es lo que
permite partir de un OCR con aciertos del 60 % por fotograma y llegar a una
identificación fiable por contenedor. El seguimiento no es un adorno: sin él no
habría sobre qué votar, y el mismo contenedor se contaría decenas de veces.

---

## Estructura

```
Video2BIC/
├── src/
│   ├── Video2BIC.Core/            Biblioteca reutilizable
│   │   ├── Bic/                   ISO 6346: dígito de control, análisis, tamaño y tipo
│   │   ├── Ocr/                   ITextRecognizer: Tesseract y CRNN/CTC en ONNX
│   │   ├── TextRegions/           Localización de líneas de texto con MSER
│   │   ├── Imaging/               Recorte, ampliación, polaridad, enderezado
│   │   ├── Recognition/           Lectura por contenedor y votación temporal
│   │   ├── Rendering/             Dibujo de cajas, códigos y panel
│   │   └── Pipeline/              Encadenado, presupuesto de OCR e informes
│   └── Video2BIC.Cli/             Ejecutable `video2bic`: scan / check / decode / info
└── tests/Video2BIC.Tests/         175 pruebas xUnit
```

El `Core` no depende de la CLI: se puede referenciar desde un servicio, una
función en la nube o el sistema de gestión de la terminal.

---

## Requisitos

- **.NET 10 SDK** o superior.
- **Tesseract** (`apt install tesseract-ocr`, `brew install tesseract`,
  `winget install UB-Mannheim.TesseractOCR`), o un modelo CRNN/CTC exportado a
  ONNX. Sin ninguno de los dos la aplicación avisa al arrancar y explica cómo
  conseguirlos.
- Un modelo YOLO de contenedores exportado a ONNX. Ver [`models/`](models/).

OpenCV y ONNX Runtime llegan como paquetes NuGet con sus binarios nativos para
Windows, Linux y macOS.

> Para compilar con un SDK anterior, el *target framework* se puede sobrescribir
> desde el entorno sin tocar ningún fichero:
> `TargetFramework=net8.0 dotnet build Video2BIC.sln`.

---

## Uso

```bash
dotnet build Video2BIC.sln
cd src/Video2BIC.Cli/bin/Debug/net10.0
```

**Analizar un vídeo y volcar el resultado a JSON:**

```bash
video2bic scan --source  puerto.mp4 \
               --model   models/containers.onnx \
               --labels  ../MaritimeVision/labels/maritime-container.names \
               --classes container \
               --json    salida/informe.json
```

**Guardar además el vídeo anotado**, con las zonas donde buscó el texto:

```bash
video2bic scan -s puerto.mp4 -m models/containers.onnx -o salida/anotado.mp4 --show-regions
```

Cada contenedor se dibuja en ámbar mientras se le busca el código y en verde
cuando queda confirmado; el panel superior lleva la cuenta y los últimos códigos
leídos.

**Cámara en vivo con un modelo de OCR propio en GPU:**

```bash
video2bic scan -s rtsp://camara.puerto.local/stream -m models/containers.onnx \
               --ocr-model models/rec.onnx --provider cuda
```

**Herramientas sueltas**, sin vídeo de por medio:

```bash
video2bic check CSQU3054383     # valida el código
video2bic check CSQU305438      # calcula el dígito de control que le falta
video2bic decode 22G1           # interpreta el código de tamaño y tipo
video2bic info --source puerto.mp4 --model models/containers.onnx
```

`video2bic --help` lista todas las opciones. Las que más se tocan:

| Opción | Por defecto | Qué hace |
|---|---|---|
| `--container-classes` | `container` | Cuáles de las clases del modelo son contenedores |
| `--conf` | 0.35 | Confianza mínima de detección |
| `--track-buffer` | 60 | Fotogramas que un contenedor sobrevive ocluido |
| `--read-every` | 5 | Fotogramas entre dos lecturas del mismo contenedor |
| `--max-reads` | 2 | Contenedores leídos por fotograma |
| `--min-readings` | 3 | Lecturas coincidentes para dar un código por confirmado |
| `--min-box-width` | 120 | Ancho mínimo de contenedor para molestarse en leerlo |
| `--max-corrections` | 2 | Caracteres que se permite corregir en una lectura |
| `--ocr` | auto | `tesseract` u `onnx` |
| `--psm` | 7 | Modo de Tesseract: 7 una línea, 11 el panel entero |
| `--show-regions` | off | Dibujar dónde se buscó el texto, para diagnosticar |
| `--permissive` | off | Aceptar códigos con el dígito de control roto (diagnóstico) |

Códigos de salida: `0` correcto, `1` error de ejecución, `2` error de uso,
`3` la pasada terminó sin identificar ningún contenedor.

---

## El presupuesto de OCR

El reconocimiento de texto es, con diferencia, la etapa cara: entre 40 y 300 ms
por contenedor, frente a los ~0.4 ms del seguimiento. Ejecutarlo en cada
fotograma y sobre cada contenedor haría el sistema inservible en vivo, y además
no aportaría nada: dos fotogramas consecutivos son casi la misma imagen.

El pipeline reparte un presupuesto por fotograma:

- solo se lee un contenedor **confirmado por el detector en ese fotograma** (una
  caja extrapolada por Kalman no tiene píxeles frescos debajo);
- solo si mide al menos `--min-box-width` × `--min-box-height` píxeles, porque por
  debajo de eso los caracteres miden dos o tres píxeles;
- como mucho `--max-reads` contenedores por fotograma, atendiendo primero al que
  menos veces se ha intentado y, a igualdad, al más grande;
- se deja de leer un contenedor **en cuanto su código queda confirmado**
  (`--keep-reading` lo desactiva).

---

## El informe

`--json` produce un fichero pensado para el sistema de gestión de la terminal, no
para leerlo a ojo. El formato se escribe a mano con `Utf8JsonWriter` para que sea
estable: no cambia porque se renombre una propiedad de C#.

```json
{
  "source": "puerto.mp4",
  "framesProcessed": 3750,
  "containersTracked": 6,
  "containers": [
    {
      "trackId": 1,
      "bicCode": "MSKU1234565",
      "ownerCode": "MSK",
      "categoryIdentifier": "U",
      "serialNumber": "123456",
      "checkDigit": 5,
      "checkDigitValid": true,
      "confirmed": true,
      "confidence": 0.9412,
      "observations": 5,
      "competingCodes": 2,
      "firstFrame": 210,
      "lastFrame": 264,
      "firstSeen": "00:00:08.4000000",
      "lastSeen": "00:00:10.5600000",
      "sizeType": {
        "code": "22G1",
        "length": "6058 mm (20 pies)",
        "height": "2591 mm (8 pies 6 pulgadas) de alto, 2438 mm de ancho",
        "type": "uso general, aberturas en un extremo y rejillas de ventilacion pasiva"
      }
    }
  ]
}
```

`competingCodes` es la señal a vigilar: si un contenedor acumuló varios códigos
distintos, la lectura fue difícil aunque el ganador esté confirmado.

`--csv` produce las mismas identificaciones en una línea por contenedor.

---

## Detalles de implementación

### El dígito de control (`Bic/BicCheckDigit.cs`)

Cada carácter tiene un valor: los dígitos el suyo, y las letras `A=10` en
adelante **saltando los múltiplos de 11**. Se ponderan por `2^posición`, se suman
y se toma el resto entre 11 (un resto de 10 se publica como 0).

El salto de los múltiplos de 11 no es arbitrario: garantiza que ninguna letra sea
equivalente a un cero. Y como los pesos son potencias de 2, que son invertibles
módulo 11, una sustitución solo pasa desapercibida si el carácter nuevo vale lo
mismo que el viejo módulo 11 — 23 de las 350 sustituciones posibles sobre un
código concreto, un 6,6 %.

### La reparación de lecturas (`Bic/BicCodeParser.cs`)

1. Se normaliza el texto y se concatenan los trozos consecutivos, porque en la
   puerta de un contenedor el código aparece repartido en varias líneas
   (`MSKU` / `123456` / `5`).
2. Se recorre el resultado con una ventana de once caracteres.
3. Si la ventana no es un código válido tal cual, se prueban sustituciones de
   glifos parecidos hasta agotar el presupuesto de `--max-corrections`, sabiendo
   que las posiciones 1-3 son letras, la 4 es `U`/`J`/`Z` y las demás dígitos.
4. Cada corrección penaliza la confianza; el dígito de control decide cuáles
   sobreviven.

### La localización de texto (`TextRegions/MserTextRegionDetector.cs`)

MSER busca manchas cuyo contorno apenas cambia al mover el umbral de
binarización, que es exactamente lo que es un carácter pintado sobre chapa. Se
busca dos veces, sobre la imagen y sobre su negativo, porque el código va en
blanco sobre pintura oscura tan a menudo como al revés. Los candidatos se filtran
por geometría de carácter y se agrupan en líneas enlazando cada uno con su vecino
compatible más cercano **a la derecha**.

> **Por qué MSER y no un filtro morfológico.** La primera versión usaba
> `tophat`/`blackhat` más un cierre horizontal, la receta clásica. Funcionaba a
> una escala y fallaba a otra: un núcleo morfológico está atado al tamaño de
> carácter para el que se eligió, y el mismo contenedor ocupa 80 píxeles al fondo
> del muelle y 900 al pasar por delante de la cámara. MSER no depende del tamaño.

El corrugado del panel es la principal fuente de falsos positivos. Lo descartan
tres filtros independientes: la proporción de un carácter (una cresta de 3×300
píxeles no la tiene), la densidad de la región (un trozo de cresta es un
rectángulo macizo y llena más del 90 % de su caja, una letra ronda la mitad) y la
exigencia de alinearse con otros dos caracteres para formar una línea.

### El preprocesado (`Imaging/TextImagePreprocessor.cs`)

Cada línea se lleva a escala de grises, se iguala el contraste por bloques
(CLAHE), se endereza y se deja siempre como **texto oscuro sobre fondo claro**,
que es lo que espera cualquier OCR entrenado sobre documentos.

Decidir la polaridad tiene truco: comparar el centro con el borde, que es lo
inmediato, no sirve porque el recorte va ajustado a la línea y el borde también
es texto. El criterio que sí funciona es que **la tinta siempre es la clase
minoritaria**: se separan las dos con Otsu y se mira cuál tiene menos píxeles.

El alto es un mínimo, no un objetivo: las líneas pequeñas se amplían y las
grandes se dejan como están. Reducir una línea que ya viene grande solo tira
información.

---

## Pruebas

```bash
dotnet test
```

175 pruebas, sin necesidad de descargar pesos entrenados:

- **ISO 6346**: dígito de control contra códigos conocidos (incluido el
  `CSQU3054383` de la propia norma), la tabla de valores completa, el caso del
  resto 10, y la prueba que recorre las 350 sustituciones de un solo carácter
  para medir cuántas sobreviven.
- **Análisis de lecturas**: código limpio dentro de texto de alrededor, código
  partido en tres líneas, reparación de `0`↔`O` y `5`↔`S`, y **la prueba contraria**:
  que un rótulo de `MAX GROSS 30480 KG` o una tira de dígitos no produzcan ningún
  candidato.
- **Códigos de tamaño y tipo**: incluida la confusión clásica de leer `45R1` como
  «45 pies» cuando son 40.
- **Decodificador CTC**: colapso de repeticiones, el blanco separando dos
  caracteres iguales, salidas transpuestas, softmax sobre logits y el error claro
  cuando el alfabeto no corresponde al modelo.
- **Localización de texto**: encuentra las dos líneas de un contenedor sintético
  a dos escalas distintas y **no encuentra nada en un panel corrugado sin
  rótulos**.
- **Votación**: confirmación por mayoría, la lectura suelta equivocada que no
  gana, dos códigos empatados que no confirman nada, el aviso emitido una sola
  vez y la identidad ya publicada que no cambia a posteriori.
- **Pipeline completo**: genera vídeo sintético con OpenCV, lo procesa con un
  detector y un OCR simulados, y comprueba que **un OCR que falla cinco de cada
  seis veces sigue identificando bien el contenedor**.
- **Tesseract de verdad**: siete pruebas que pintan un contenedor con su código y
  recorren la cadena entera, la última sobre un vídeo completo. Se saltan solas
  si Tesseract no está instalado.

---

## Problemas conocidos

**`DllNotFoundException: OpenCvSharpExtern` en Linux.** Los binarios nativos que
publica OpenCvSharp para `linux-x64` están compilados contra Ubuntu 22.04. En
Ubuntu 24.04 o Debian 12 faltan librerías (`libavcodec.so.58`, `libtiff.so.5`,
`libtesseract.so.4`...). Comprueba cuáles con:

```bash
ldd bin/Debug/net10.0/runtimes/linux-x64/native/libOpenCvSharpExtern.so | grep "not found"
```

La salida más limpia es ejecutar en un contenedor basado en Ubuntu 22.04.

**Tesseract cuesta un proceso por línea.** Entre 40 y 300 ms cada uno, la mayor
parte solo en arrancarlo. Para vídeo en tiempo real usa `--ocr-model` con un
CRNN/CTC en ONNX, que se ejecuta en el mismo proceso.

**El código no siempre está en el panel que se ve.** Un contenedor fotografiado
por el lado equivocado, o con la puerta contra otro contenedor, no tiene código
legible. Es la razón de que el informe distinga «seguidos» de «identificados» en
vez de dar por hecho que todo lo detectado se lee.

---

## Referencias

- ISO 6346:2022, *Freight containers — Coding, identification and marking*.
- BIC, *Container Identification Number* — registro de códigos de propietario.
- Matas et al., *Robust Wide Baseline Stereo from Maximally Stable Extremal
  Regions*, BMVC 2002 — el detector de regiones.
- Zhang et al., *ByteTrack: Multi-Object Tracking by Associating Every Detection
  Box*, ECCV 2022 — el seguimiento, implementado en `MaritimeVision`.
