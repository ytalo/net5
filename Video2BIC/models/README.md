# Modelos

Video2BIC usa dos modelos, y ninguno de los dos se versiona: los `.onnx` pesan
demasiado para el repositorio (ver `.gitignore`).

## 1. Detector de contenedores (obligatorio)

Un modelo YOLO exportado a ONNX que sepa detectar contenedores maritimos.
**COCO no tiene esa clase**, asi que un preentrenado estandar no sirve como
solucion final: lo mas cercano que conoce son `truck`, `train` y `boat`.

```bash
pip install ultralytics

# Modelo propio, entrenado con un dataset de contenedores (Roboflow Universe
# tiene varios publicos de "shipping container" / "container terminal").
yolo detect train data=contenedores.yaml model=yolo11s.pt epochs=100 imgsz=640
yolo export model=runs/detect/train/weights/best.pt format=onnx opset=12 imgsz=640
```

El fichero de etiquetas que se pase con `--labels` debe coincidir exactamente con
el orden de clases del entrenamiento.

Para probar el recorrido completo antes de tener un modelo propio sirve un
preentrenado COCO, aceptando que detectara camiones y no contenedores:

```bash
yolo export model=yolo11n.pt format=onnx opset=12 imgsz=640
video2bic scan -s puerto.mp4 -m yolo11n.onnx -l ../MaritimeVision/labels/coco.names \
               -c truck --container-classes truck
```

## 2. Reconocedor de texto (opcional)

Sin el, la aplicacion usa el binario de Tesseract, que se instala con el gestor
de paquetes del sistema y funciona razonablemente sobre rotulos grandes. Para
video en tiempo real conviene un modelo CRNN/CTC en ONNX, que se ejecuta en el
mismo proceso en vez de lanzar uno por recorte.

```bash
# PaddleOCR: modelo de reconocimiento en ingles, exportado a ONNX.
pip install paddleocr paddle2onnx
paddle2onnx --model_dir en_PP-OCRv4_rec_infer \
            --model_filename inference.pdmodel \
            --params_filename inference.pdiparams \
            --save_file rec.onnx --opset_version 12
```

```bash
video2bic scan -s puerto.mp4 -m models/containers.onnx \
               --ocr-model models/rec.onnx --ocr-charset ../charsets/bic-alphanumeric.txt
```

Si el modelo emite un numero de clases que no cuadra con el alfabeto, la
aplicacion lo dice al arrancar en vez de devolver texto sin sentido.
