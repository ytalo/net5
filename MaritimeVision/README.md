# MaritimeVision

Aplicación .NET / C# que lee una secuencia de vídeo, detecta objetos con **YOLO**
(vía ONNX Runtime), les asigna una identidad estable con **ByteTrack** y reproduce
la secuencia con los *bounding boxes* dibujados.

Está pensada para contenedores marítimos en terminal portuaria, pero el detector
es agnóstico: sirve cualquier modelo YOLO exportado a ONNX y su lista de clases.

---

## Qué hace

| | |
|---|---|
| **Recibe** la secuencia | fichero de vídeo, URL `rtsp://` / `http://`, o cámara conectada |
| **Detecta** | YOLOv5 / v7 / v8 / v9 / v11 en formato ONNX, incluidos los exportados con NMS incorporada |
| **Sigue** | ByteTrack completo: filtro de Kalman, asociación en dos etapas y buffer de reidentificación |
| **Reproduce** | ventana de escritorio, visor web en el navegador, o fichero de vídeo anotado |

Cada objeto se dibuja con su caja, su identidad (`#12`), su clase, su confianza y
la estela de su trayectoria.

---

## Estructura

```
MaritimeVision/
├── src/
│   ├── MaritimeVision.Core/          Biblioteca: detección, seguimiento, vídeo y render
│   │   ├── Abstractions/             IObjectDetector, IObjectTracker, IVideoSource, IVideoSink
│   │   ├── Models/                   BoundingBox, Detection, TrackedObject, FrameAnalysis
│   │   ├── Detectors/                YoloDetector (ONNX), letterbox, NMS, parser de salidas
│   │   ├── Tracking/                 ByteTracker, Kalman, Cholesky, asignación lineal
│   │   ├── Video/                    Lectura (fichero/RTSP/cámara) y escritura
│   │   ├── Rendering/                Dibujo de cajas, etiquetas, estelas y panel
│   │   └── Pipeline/                 Encadenado y estadísticas
│   ├── MaritimeVision.Cli/           Ejecutable `mvision`: play / export / info
│   └── MaritimeVision.Web/           Visor ASP.NET Core: sube el vídeo y lo ve anotado
└── tests/MaritimeVision.Tests/       109 pruebas xUnit
```

El `Core` no depende de la CLI ni de la web: se puede referenciar desde cualquier
aplicación .NET (un servicio, una app WPF, una función en la nube).

---

## Requisitos

- **.NET 10 SDK** o superior.
- Nada más: OpenCV y ONNX Runtime llegan como paquetes NuGet con sus binarios
  nativos para Windows, Linux y macOS.

> **Sobre el *target framework*.** El repositorio nació en `net5.0`, pero .NET 5
> dejó de tener soporte en mayo de 2022 y ni `OpenCvSharp4` ni
> `Microsoft.ML.OnnxRuntime` publican ya *assets* para esa versión. La solución
> apunta a `net10.0` (LTS, con soporte hasta noviembre de 2028), definido en un
> único sitio: `Directory.Build.props`.
>
> Los dos paquetes nativos se consumen por su *asset* de `netstandard2.0`, que
> `net10.0` carga sin cambios, así que subir de versión de .NET no obliga a tocar
> código ni a esperar a que esos paquetes publiquen un *target* nuevo.

---

## El modelo: lo primero que hay que resolver

**COCO no tiene una clase «contenedor marítimo».** Un modelo YOLO preentrenado
estándar nunca va a detectar contenedores como tales: lo más cercano que conoce
son `truck`, `train` y `boat`. Hay dos caminos:

### A) Entrenar un modelo propio (lo correcto para producción)

```bash
pip install ultralytics

# 1. Entrenar con un dataset de contenedores (Roboflow Universe tiene varios
#    públicos de "shipping container" / "container terminal").
yolo detect train data=contenedores.yaml model=yolo11s.pt epochs=100 imgsz=640

# 2. Exportar a ONNX.
yolo export model=runs/detect/train/weights/best.pt format=onnx opset=12 imgsz=640
```

Copia el `.onnx` a `models/` y ajusta `labels/maritime-container.names` para que
coincida **exactamente** con el orden de clases del entrenamiento. Si no coincide,
la aplicación avisa por la salida de error.

### B) Empezar con un modelo COCO (para probar el sistema hoy)

```bash
pip install ultralytics
yolo export model=yolo11n.pt format=onnx opset=12 imgsz=640
```

```bash
mvision play -s puerto.mp4 -m yolo11n.onnx -l labels/coco.names -c truck,boat,train
```

Detectará camiones y buques, no contenedores. Sirve para validar el pipeline
completo (lectura, seguimiento, render, reproducción) antes de tener un modelo
propio, no como solución final.

---

## Uso: línea de comandos

```bash
dotnet build MaritimeVision.sln
cd src/MaritimeVision.Cli/bin/Debug/net10.0
```

**Reproducir con las cajas dibujadas** (abre una ventana; espacio pausa, `q` cierra):

```bash
mvision play --source puerto.mp4 \
             --model  models/containers.onnx \
             --labels labels/maritime-container.names \
             --classes container
```

**Exportar el vídeo anotado** (no necesita entorno gráfico, ideal para servidores):

```bash
mvision export -s puerto.mp4 -m models/containers.onnx -o salida/puerto-anotado.mp4
```

**Analizar una cámara IP en vivo:**

```bash
mvision play -s rtsp://camara.puerto.local/stream -m models/containers.onnx --track-buffer 60
```

**Inspeccionar una secuencia o un modelo:**

```bash
mvision info --source puerto.mp4 --model models/containers.onnx
```

`mvision --help` lista todas las opciones. Las principales:

| Opción | Por defecto | Qué hace |
|---|---|---|
| `--conf` | 0.25 | Confianza mínima de detección |
| `--iou` | 0.45 | Umbral de solape de la NMS |
| `--classes` | todas | Clases de interés, por nombre o índice |
| `--track-thresh` | 0.5 | Frontera entre confianza alta y baja de ByteTrack |
| `--low-thresh` | 0.1 | Confianza mínima aprovechable en la segunda asociación |
| `--track-buffer` | 30 | Fotogramas que un track sobrevive ocluido |
| `--predict-frames` | 0 | Dibujar la caja extrapolada durante una oclusión |
| `--class-agnostic` | off | Permitir que un track cambie de clase |
| `--provider` | cpu | `cpu`, `cuda` o `directml` |
| `--show-detections` | off | Dibujar también las detecciones crudas, para diagnóstico |

---

## Uso: visor web

Útil cuando no hay escritorio (servidor, contenedor, WSL) o para ver el resultado
desde otra máquina.

```bash
cd src/MaritimeVision.Web
export MaritimeVision__ModelPath="../../models/containers.onnx"
export MaritimeVision__LabelsPath="../../labels/maritime-container.names"
dotnet run
```

Abre `http://localhost:5000`, sube un vídeo y se reproduce anotado en el
navegador mediante MJPEG, a la velocidad real de la secuencia.

Configuración en `appsettings.json`, sección `MaritimeVision` (o variables de
entorno con el prefijo `MaritimeVision__`):

| Clave | Por defecto | Qué hace |
|---|---|---|
| `ModelPath` | — | Ruta del `.onnx`. Relativa, se busca hacia arriba desde el proyecto |
| `LabelsPath` | — | Fichero de etiquetas. Vacío deduce las clases del modelo |
| `TargetClasses` | vacío | Clases de interés separadas por comas |
| `JpegQuality` | 80 | Calidad del flujo MJPEG |
| `MaxUploadMegabytes` | 512 | Tamaño máximo de subida |
| `AllowNetworkSources` | `false` | Permitir que el usuario indique URLs RTSP/HTTP |

> `AllowNetworkSources` viene desactivado a propósito. En un despliegue accesible
> desde fuera, dejar que un visitante elija la URL de origen convierte al servidor
> en un cliente de red arbitrario contra la red interna.

---

## Cómo está implementado ByteTrack

ByteTrack (Zhang et al., ECCV 2022) parte de una observación simple: **un detector
baja la confianza justo cuando un objeto se ocluye parcialmente**, que es
precisamente cuando el tracker más necesita evidencia. Filtrar por umbral tira esa
información. ByteTrack la aprovecha en una segunda pasada.

El ciclo por fotograma, en `Tracking/ByteTracker.cs`:

1. Separar las detecciones en confianza **alta** y **baja**.
2. Predecir con Kalman la posición de todos los tracks vivos.
3. **Primera asociación**: tracks activos *y perdidos* contra las detecciones de
   confianza alta, por distancia IoU ponderada con la confianza (`fuse_score`).
4. **Segunda asociación**: los tracks que quedaron huérfanos contra las
   detecciones de confianza baja. Aquí es donde se sostienen las oclusiones.
5. Reconciliar los tracks sin confirmar y dar de alta los nuevos.
6. Retirar los que agotaron el buffer.

Piezas propias, sin dependencias de álgebra lineal externas:

- `KalmanFilter.cs` — filtro de velocidad constante de 8 estados
  `(cx, cy, aspecto, alto, + velocidades)`, con ruido escalado por la altura de la
  caja, igual que SORT/DeepSORT/ByteTrack.
- `Cholesky.cs` — descomposición y resolución de sistemas; sustituye a
  `scipy.linalg.cho_factor` / `cho_solve`. Regulariza si la covarianza pierde la
  definición positiva por error numérico acumulado.
- `LinearAssignment.cs` — Jonker-Volgenant con potenciales duales, equivalente a
  `scipy.optimize.linear_sum_assignment`. Admite matrices rectangulares.

### Diferencia deliberada con la implementación de referencia

ByteTrack original es **agnóstico a la clase**. Aquí el emparejamiento es
**consciente de la clase por defecto** (`--class-agnostic` lo desactiva), porque en
una terminal portuaria un contenedor y el camión que lo transporta se solapan casi
por completo y no deben intercambiar identidad ni eliminarse mutuamente en la
deduplicación de tracks.

---

## Pruebas

```bash
dotnet test
```

109 pruebas, sin necesidad de descargar pesos entrenados:

- **Asignación lineal** contra fuerza bruta exacta, en 200 instancias aleatorias
  de tamaños cuadrados y rectangulares.
- **Álgebra**: Cholesky reconstruye la matriz, resuelve sistemas y sobrevive a una
  matriz singular.
- **Kalman**: aprende una velocidad constante, extrapola a través de un hueco y
  mantiene la covarianza simétrica y positiva tras 500 iteraciones.
- **ByteTrack**: identidades estables, recuperación tras oclusión, caducidad del
  buffer, rechazo de falsos positivos de un solo fotograma, y un par de pruebas
  enfrentadas que demuestran que **las detecciones de confianza baja mantienen el
  track vivo** y que desactivar esa etapa lo rompe.
- **Detección**: letterbox de ida y vuelta, NMS por clase y agnóstica, e
  interpretación de las tres familias de salida YOLO con tensores sintéticos.
- **ONNX real**: `fixtures/tiny-yolo-fixture.onnx` (400 bytes, generado por el
  script que lo acompaña) ejercita el camino completo de inferencia. Su primera
  caja puntúa la media de los píxeles de entrada, lo que permite comprobar que el
  preprocesado llega de verdad al modelo.
- **Pipeline completo**: genera vídeo sintético con OpenCV, lo procesa y verifica
  que el fichero anotado se escribe y se vuelve a leer.

---

## Rendimiento

La inferencia domina el coste; el seguimiento es despreciable (~0.4 ms por
fotograma con una decena de objetos). Para vídeo en tiempo real:

- Usa `--provider cuda` (requiere el paquete `Microsoft.ML.OnnxRuntime.Gpu` en vez
  del de CPU) o `--provider directml` en Windows.
- Un modelo `n` o `s` a 640 px basta para contenedores, que son objetos grandes.
- En vivo, el lector fija el buffer de captura a 1 fotograma para que el análisis
  no se vaya retrasando respecto a la cámara.

---

## Problemas conocidos

**`DllNotFoundException: OpenCvSharpExtern` en Linux.** Los binarios nativos que
publica OpenCvSharp para `linux-x64` están compilados contra Ubuntu 22.04. En
Ubuntu 24.04 o Debian 12 faltan librerías (`libavcodec.so.58`, `libtiff.so.5`,
`libtesseract.so.4`...). Comprueba cuáles con:

```bash
ldd bin/Debug/net10.0/runtimes/linux-x64/native/libOpenCvSharpExtern.so | grep "not found"
```

La salida más limpia es ejecutar en un contenedor basado en Ubuntu 22.04. En
Windows y macOS no aparece.

**La ventana de reproducción no abre.** `Cv2.ImShow` necesita entorno gráfico. En
un servidor o contenedor, `mvision play` lo detecta, lo avisa y sugiere usar
`export` o el visor web; no falla.

---

## Referencias

- Zhang et al., *ByteTrack: Multi-Object Tracking by Associating Every Detection
  Box*, ECCV 2022.
- Bewley et al., *Simple Online and Realtime Tracking* (SORT), ICIP 2016 — el
  filtro de Kalman y la formulación del estado.
- Jonker & Volgenant, *A Shortest Augmenting Path Algorithm for Dense and Sparse
  Linear Assignment Problems*, Computing 38, 1987.
