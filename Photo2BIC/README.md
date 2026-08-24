# Photo2BIC

Aplicación .NET 10 / C# que lee el **código BIC (ISO 6346) de la fotografía de un
contenedor** combinando varios motores de reconocimiento de texto —locales y de
nube— y decidiendo por consenso entre ellos.

```
$ photo2bic read puerta.jpg --detalle

Entrada:     puerta.jpg
Motores:     tesseract-lineas, tesseract-disperso, azure, google (4 de 7)
Fotografia:  1600x1200 px, analizada en 3.4s

Lo que leyo cada motor:
  motor               variante    ms   conf  texto
  ------------------  ----------  ---  ----  --------------------
  tesseract-lineas    original    350  88 %  CSQU3054383 22G1
  tesseract-lineas    clahe       613  77 %  CSQU3054383 POY 22G1
  tesseract-lineas    otsu        132  88 %  2G1
  tesseract-disperso  original    174  51 %  CSQU3054383
  azure               original    890  97 %  CSQU 305438 3 22G1
  google              original    734  95 %  CSQU3054383 22G1

Codigo:      CSQU 305438 3
Propietario: CSQ   Categoria: U (contenedor de carga)   Serie: 305438   Control: 3
Tamano:      22G1 - 6058 mm (20 pies), 2591 mm de alto, uso general
Confianza:   99 % (6 lectura(s), 3 familia(s): azure, google, tesseract-lineas...)
Veredicto:   CONFIRMADO
```

El resultado no es «esto es lo que leyó el OCR», sino **un código, quién lo
respalda y cuánta confianza merece** —incluido el caso honesto de «hay dos
códigos empatados, míralo tú».

---

## La idea

Video2BIC, en este mismo repositorio, resuelve el problema en vídeo. Allí un
contenedor se ve **decenas de veces**: la lectura correcta se repite entre
fotogramas y las equivocadas se dispersan, así que basta con votar.

**En una fotografía sólo hay una oportunidad.** No hay fotogramas que votar, así
que la variedad hay que fabricarla en otra parte, y son dos:

| Fuente de variedad | Qué es | Cuánto vale |
|---|---|---|
| **Varios motores** | Tesseract, un CRNN propio, Azure, Google, Textract, OCR.space leyendo la misma imagen | Mucho: fallan por razones distintas |
| **Varias preparaciones** | La misma imagen con el contraste igualado, binarizada de dos formas, invertida, ampliada | Bastante menos: el mismo motor tiene los mismos puntos ciegos |

Distinguir esas dos cosas es el núcleo de la aplicación. Dos motores distintos que
coinciden son **dos opiniones independientes** y su acuerdo multiplica la
confianza. El mismo motor coincidiendo consigo mismo sobre otra variante vale
mucho menos: si se equivoca en una imagen tiende a equivocarse igual en la otra.

Que esto no es teórico se ve en cuanto se ejecuta. Sobre una misma fotografía:

```
tesseract-lineas    original    CSQU3054383      <- bien
tesseract-lineas    otsu        2G1              <- el umbral se comio el rotulo
tesseract-lineas    adaptativa  (nada)           <- ni encontro la linea
```

Ninguna preparación gana siempre, y no se sabe cuál va a funcionar hasta que se
prueba. Por eso se prueban todas.

### Y por debajo, el dígito de control

De los once caracteres de un código BIC, el último está determinado por los diez
anteriores. Eso detecta **el 93 % de los errores de un solo carácter** sin
ninguna otra evidencia, y es lo que permite proponer reparaciones agresivas
(`0`↔`O`, `5`↔`S`, `8`↔`B`…) dejando que la aritmética descarte las equivocadas.

El consenso decide **entre códigos que ya cuadran**, no entre cadenas de texto.
Son dos filtros en serie, no uno.

---

## Los siete motores

| Motor | Familia | Dónde | Qué aporta |
|---|---|---|---|
| `tesseract-lineas` | tesseract | local | MSER localiza las líneas y Tesseract lee cada una (`--psm 7`) |
| `tesseract-disperso` | tesseract | local | Tesseract busca el texto él solo en el panel entero (`--psm 11`) |
| `onnx` | onnx | local | Modelo CRNN/CTC propio; entrenado sobre texto de escena lee mucho mejor un rótulo degradado |
| `azure` | azure | nube | Azure AI Vision `imageanalysis:analyze`, feature `read` |
| `google` | google | nube | Google Cloud Vision `TEXT_DETECTION` |
| `textract` | aws | nube | Amazon Textract `DetectDocumentText`, firmado con SigV4 |
| `ocrspace` | ocrspace | nube | OCR.space; se puede probar sin cuenta con la clave `helloworld` |

Los dos de Tesseract **comparten familia a propósito**: son el mismo motor con dos
ajustes, y cuando coinciden eso no son dos opiniones independientes. El consenso
agrupa por familia para no inflar la confianza.

Ninguno es obligatorio. Un motor sin configurar responde «no disponible» con el
motivo y el análisis sigue sin él —que es la única forma de que tener varios sirva
de algo—.

```
$ photo2bic engines

  motor               familia    donde  listo  detalle
  ------------------  ---------  -----  -----  -----------------------------------------
  tesseract-lineas    tesseract  local  si     tesseract 5.3.4
  tesseract-disperso  tesseract  local  si     tesseract 5.3.4
  onnx                onnx       local  NO     falta: falta el modelo. Indicalo con --onnx-model
  azure               azure      nube   NO     falta: faltan credenciales. Exporta AZURE_VISION_...
```

### Credenciales

Se leen **sólo de variables de entorno**, nunca de la línea de comandos: un
argumento queda en el historial del intérprete y en la lista de procesos.

| Variable | Para |
|---|---|
| `AZURE_VISION_ENDPOINT`, `AZURE_VISION_KEY` | Azure AI Vision |
| `GOOGLE_VISION_API_KEY` **o** `GOOGLE_VISION_ACCESS_TOKEN` | Google Cloud Vision |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN`, `AWS_REGION` | Amazon Textract |
| `OCRSPACE_API_KEY` | OCR.space (`helloworld` para probar) |
| `PHOTO2BIC_ONNX_MODEL`, `PHOTO2BIC_ONNX_CHARSET` | Modelo CRNN/CTC |
| `PHOTO2BIC_TESSERACT` | Ejecutable de Tesseract, si no está en el `PATH` |

> **Ojo:** los motores de nube envían la fotografía al proveedor y **se facturan
> por llamada**. Si el entorno ya tiene credenciales de AWS o de Azure por otro
> motivo, `photo2bic read` las va a usar. Para acotarlo, `--engines tesseract`.

---

## Empezar

### Requisitos

- **.NET 10 SDK**.
- Al menos un motor. El más rápido es Tesseract:
  `apt install tesseract-ocr` · `brew install tesseract` ·
  `winget install UB-Mannheim.TesseractOCR`.

OpenCV y ONNX Runtime llegan como paquetes NuGet con sus binarios nativos.

### Probarlo sin conseguir ninguna fotografía

Las fotos de contenedores no se versionan —pesan, y las de una terminal real
tienen dueño—, así que la aplicación genera su propia escena de prueba:

```bash
dotnet build Photo2BIC/Photo2BIC.sln

cd Photo2BIC/src/Photo2BIC.Cli/bin/Debug/net10.0

./photo2bic sample ejemplo.jpg
./photo2bic read ejemplo.jpg --detalle
```

La escena incluye el corrugado del panel, que es la principal fuente de falsos
positivos al buscar texto. Lo que **no** reproduce es lo que de verdad hace difícil
una fotografía real —suciedad, reflejos, pintura descascarillada, ángulo—: sirve
para comprobar que la aplicación funciona, no para medir lo bien que lee.

### Sobre fotografías de verdad

```bash
photo2bic read puerta.jpg --detalle
photo2bic read puerta.jpg --engines tesseract,azure --json informe.json
photo2bic batch fotos/ --recursivo --csv resultados.csv --min-familias 2
```

---

## Comandos

| Comando | Qué hace |
|---|---|
| `read <foto>` | Lee una fotografía |
| `batch <carpeta>` | Lee todas las de una carpeta, con informe agregado |
| `engines` | Qué motores hay y qué le falta a cada uno |
| `check <codigo>` | Valida un código, o le calcula el dígito de control |
| `sample <fichero>` | Genera una fotografía de prueba |

`photo2bic --help` lista todas las opciones. Las que más se tocan:

| Opción | Por defecto | Qué hace |
|---|---|---|
| `--engines` | todos | Motores a usar. Atajos: `local`, `nube`, `tesseract`, `todos` |
| `--detalle` | off | Enseña lo que leyó cada motor sobre cada variante |
| `--variantes` | 4 | Preparaciones distintas por motor local |
| `--min-confianza` | 0,6 | Confianza combinada para dar un código por confirmado |
| `--min-familias` | 1 | Familias que deben coincidir para confirmar |
| `--peso` | — | Peso por familia: `--peso azure=1,tesseract=0.6` |
| `--max-correcciones` | 2 | Caracteres reparables por lectura |
| `--sin-digito-control` | off | Acepta códigos con el control incorrecto. **Sólo para diagnosticar** |

`--min-familias 2` es lo razonable en producción: el coste de dar por bueno un
código equivocado es un contenedor mal asignado.

Códigos de salida: `0` correcto, `1` error de ejecución, `2` error de uso,
`3` ninguna fotografía dio código.

---

## Cómo se combina la evidencia

La confianza de un código es una **«o» ruidosa**: la probabilidad de que *al menos
una* de las evidencias que lo respaldan sea correcta.

```
dentro de una familia:   score ← score + (1 − score) · confianza · amortiguación
                         amortiguación ×= 0,35 en cada lectura adicional

entre familias:          score ← score + (1 − score) · scoreFamilia · peso
                         sin amortiguar: son independientes
```

La misma fórmula dos veces, con y sin amortiguación. Tiene las propiedades que
hacen falta: dos evidencias mediocres suman más que una buena, añadir evidencia
nunca baja la confianza, y **nunca se llega a la certeza** (hay un techo explícito
en 0,999; por muchos motores que coincidan, debajo sigue habiendo un OCR sobre una
fotografía, y un 100 % invitaría a saltarse la revisión justo en los casos en que
todos se equivocan a la vez).

Los pesos por familia están **todos a 1 por defecto, a propósito**. No hay una
jerarquía universal de motores: cuál acierta más depende de las cámaras, de la luz
y del estado de la flota. Se ajustan midiendo sobre las propias fotografías, y
`batch` da justo esa medida:

```
Aportacion de cada motor al codigo elegido:
  motor               fotografias  porcentaje
  ------------------  -----------  ----------
  azure               47           94 %
  tesseract-disperso  31           62 %
  ocrspace            12           24 %
```

---

## Estructura

```
Photo2BIC/
├── src/
│   ├── Photo2BIC.Core/            Biblioteca reutilizable
│   │   ├── Engines/               IPhotoOcrEngine y los siete motores
│   │   │   ├── Local/             Tesseract y CRNN/CTC en ONNX
│   │   │   └── Cloud/             Azure, Google, Textract (con SigV4) y OCR.space
│   │   ├── Imaging/               Variantes de preprocesado, carga y escena de prueba
│   │   ├── Fusion/                Consenso entre motores
│   │   ├── Configuration/         Credenciales desde el entorno
│   │   └── Pipeline/              Encadenado e informes JSON
│   └── Photo2BIC.Cli/             Ejecutable `photo2bic`
├── tests/Photo2BIC.Tests/         117 pruebas xUnit
└── models/                        Dónde va el .onnx (no versionado)
```

**Qué se reutiliza y qué es nuevo.** La norma ISO 6346 (dígito de control,
análisis, reparación de glifos, tamaño y tipo), los reconocedores locales y la
localización de líneas con MSER ya estaban resueltos en `Video2BIC.Core`;
Photo2BIC lo referencia en vez de duplicarlo, igual que Video2BIC hace con
`MaritimeVision.Core`. Lo que aporta es lo que no existía: la abstracción de motor
sobre fotografía completa, los cuatro servicios de nube, las variantes de
preprocesado y el consenso entre motores.

El `Core` no depende de la CLI: se puede referenciar desde un servicio, una
función en la nube o el sistema de gestión de la terminal.

---

## Pruebas

```bash
dotnet test Photo2BIC/Photo2BIC.sln
```

117 pruebas, sin necesidad de credenciales ni de red.

Los motores de nube son, en su mayor parte, código que interpreta JSON ajeno, así
que se prueban contra **cuerpos de respuesta con la forma real de cada servicio**
inyectando un `HttpMessageHandler`. La firma SigV4 se contrasta con los **vectores
oficiales de AWS** (`get-vanilla` y la clave de firma documentada): es el único
sitio donde un error no se manifiesta como un resultado peor sino como un 403 seco.

Siete pruebas hacen el recorrido completo con Tesseract de verdad, y **se saltan
solas si no está instalado**.

Las que más valor tienen son las que comprueban lo contrario de lo esperado: que
dos motores independientes pesen más que uno repitiéndose, que un rótulo de
`MAX GROSS 30480 KG` **no** produzca ningún código, que un motor caído **no** tumbe
el análisis, y que dos códigos empatados **no** se confirmen.

---

## Limitaciones conocidas

- **Errores correlacionados dentro de una familia.** Si Tesseract lee un `5` donde
  hay un `3`, lo lee igual en todas las variantes, y el dígito de control lo
  detecta pero no lo arregla: las tablas de confusión de glifos cubren
  letra↔dígito (`0`↔`O`, `5`↔`S`) pero no dígito↔dígito. Ese caso se resuelve con
  otra familia, no con más preparaciones. Es exactamente lo que se ve al ejecutar
  `sample` con un código y leerlo sólo con Tesseract.
- **El código de tamaño y tipo no tiene dígito de control**, así que no se puede
  validar como el BIC. Se acepta el primero que encaje en la tabla de la norma;
  por eso se marca aparte y no forma parte del veredicto.
- **Una fotografía, un contenedor.** No hay detección de contenedores: se asume
  que la foto es de uno. Si salen dos, aparecerán dos códigos y el resultado se
  marcará como ambiguo, que es lo correcto pero no es lo mismo que leer los dos.
  Para escenas con varios contenedores está Video2BIC, que sí detecta y sigue.
- **No hay corrección de perspectiva.** Un panel fotografiado muy de lado se lee
  mal. Sólo se corrige la inclinación de cada línea, no la homografía del panel.

---

## Referencias

- ISO 6346:2022, *Freight containers — Coding, identification and marking*.
- BIC, *Container Identification Number* — registro de códigos de propietario.
- Matas et al., *Robust Wide Baseline Stereo from Maximally Stable Extremal
  Regions*, BMVC 2002 — el detector de regiones de texto.
- Pearl, *Probabilistic Reasoning in Intelligent Systems*, 1988 — la «o» ruidosa
  con la que se combinan las evidencias.
- AWS, *Signature Version 4 signing process* — y su juego de vectores de prueba.
