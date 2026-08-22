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

Cada contenedor se dibuja en ámbar mientras se le busca el código y en verde
cuando queda confirmado.

---

## Cómo funciona

| Etapa | Qué hace |
|---|---|
| **1. Vídeo** | Fichero, URL `rtsp://` / `http://`, o cámara conectada |
| **2. Detección** | YOLO sobre ONNX Runtime localiza los contenedores |
| **3. Seguimiento** | ByteTrack les da una identidad estable entre fotogramas |
| **4. Localización de texto** | MSER agrupa caracteres en líneas dentro del recorte |
| **5. OCR** | Tesseract o un modelo CRNN/CTC en ONNX lee cada línea |
| **6. ISO 6346** | Reconstruye el código, lo repara y valida el dígito de control |
| **7. Votación** | Acumula las lecturas de cada contenedor y decide su código |

Las tres primeras etapas se reutilizan de la biblioteca `MaritimeVision.Core`,
que ya resolvía la detección y el seguimiento; Video2BIC la referencia en vez de
duplicarla y aporta de la cuarta en adelante.

### Las dos ideas que hacen que esto funcione

**El dígito de control lo decide casi todo.** De los once caracteres de un código
BIC, el último está determinado por los diez anteriores. Eso convierte al OCR en
un problema mucho más fácil de lo que parece: **el 93 % de los errores de un solo
carácter se detectan** sin ninguna otra evidencia, así que se pueden probar
reparaciones agresivas (`0`↔`O`, `5`↔`S`, `8`↔`B`...) y dejar que la aritmética
descarte las equivocadas.

**Un contenedor se ve muchas veces, no una.** Permanece decenas de fotogramas en
escena y se lee desde ángulos y distancias distintas. La lectura correcta se
repite y las equivocadas se dispersan, así que **votar sobre el conjunto es mucho
más fiable que confiar en el mejor fotograma**. Es lo que permite partir de un
OCR con aciertos del 60 % por fotograma y llegar a una identificación fiable por
contenedor.

---

## Empezar

### Requisitos

- **.NET 10 SDK** o superior.
- **Tesseract** (`apt install tesseract-ocr`, `brew install tesseract`,
  `winget install UB-Mannheim.TesseractOCR`), o un modelo CRNN/CTC en ONNX. Sin
  ninguno de los dos la aplicación avisa al arrancar y explica cómo conseguirlos.
- **Un modelo YOLO de contenedores exportado a ONNX.** Es lo único que no viene
  en el repositorio: son pesos entrenados, no código, y **COCO no tiene la clase
  «contenedor marítimo»**, así que un preentrenado estándar no sirve como solución
  final. Ver [`Video2BIC/models/`](Video2BIC/models/) para entrenarlo, o para
  probar el recorrido completo con un modelo COCO mientras tanto.

OpenCV y ONNX Runtime llegan como paquetes NuGet con sus binarios nativos para
Windows, Linux y macOS.

### Compilar y ejecutar

Desde la raíz del repositorio:

```bash
dotnet build Video2BIC/Video2BIC.sln

dotnet run --project Video2BIC/src/Video2BIC.Cli -- scan \
    --source  puerto.mp4 \
    --model   Video2BIC/models/containers.onnx \
    --labels  MaritimeVision/labels/maritime-container.names \
    --classes container \
    --json    salida/informe.json
```

El ejecutable queda en
`Video2BIC/src/Video2BIC.Cli/bin/Debug/net10.0/video2bic`, así que a partir de
ahí se invoca directamente:

```bash
video2bic scan -s puerto.mp4 -m models/containers.onnx -o salida/anotado.mp4 --show-regions
```

Herramientas sueltas, sin vídeo de por medio:

```bash
video2bic check CSQU3054383     # valida el código
video2bic check CSQU305438      # calcula el dígito de control que le falta
video2bic decode 22G1           # interpreta el código de tamaño y tipo
```

`video2bic --help` lista todas las opciones. Las que más se tocan:

| Opción | Por defecto | Qué hace |
|---|---|---|
| `--container-classes` | `container` | Cuáles de las clases del modelo son contenedores |
| `--read-every` | 5 | Fotogramas entre dos lecturas del mismo contenedor |
| `--min-readings` | 3 | Lecturas coincidentes para dar un código por confirmado |
| `--min-box-width` | 120 | Ancho mínimo de contenedor para molestarse en leerlo |
| `--max-corrections` | 2 | Caracteres que se permite corregir en una lectura |
| `--ocr` | auto | `tesseract` u `onnx` |
| `--show-regions` | off | Dibujar dónde se buscó el texto, para diagnosticar |

Códigos de salida: `0` correcto, `1` error de ejecución, `2` error de uso,
`3` la pasada terminó sin identificar ningún contenedor.

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
├── tests/Video2BIC.Tests/         175 pruebas xUnit
├── models/                        Dónde van los .onnx (no versionados)
└── charsets/                      Alfabetos del reconocedor de texto
```

El `Core` no depende de la CLI: se puede referenciar desde un servicio, una
función en la nube o el sistema de gestión de la terminal.

**La documentación completa está en [`Video2BIC/README.md`](Video2BIC/README.md)**:
formato del informe JSON, presupuesto de OCR, detalles de implementación de cada
etapa y problemas conocidos.

---

## Pruebas

```bash
dotnet test Video2BIC/Video2BIC.sln
```

175 pruebas, sin necesidad de descargar pesos entrenados. Las que más valor
tienen son las que comprueban lo contrario de lo esperado: que un rótulo de
`MAX GROSS 30480 KG` **no** produzca ningún código, que un panel corrugado sin
letras **no** dispare el localizador de texto, y que dos códigos empatados en
evidencia **no** confirmen nada. Hay siete de extremo a extremo con Tesseract
real —la última sobre un vídeo completo— que se saltan solas si no está
instalado.

---

## Referencias

- ISO 6346:2022, *Freight containers — Coding, identification and marking*.
- BIC, *Container Identification Number* — registro de códigos de propietario.
- Matas et al., *Robust Wide Baseline Stereo from Maximally Stable Extremal
  Regions*, BMVC 2002 — el detector de regiones de texto.
- Zhang et al., *ByteTrack: Multi-Object Tracking by Associating Every Detection
  Box*, ECCV 2022 — el seguimiento.
