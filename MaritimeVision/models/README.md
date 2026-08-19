# Modelos

Coloca aquí los modelos YOLO exportados a ONNX. Los ficheros `.onnx` no se
versionan (ver `.gitignore`): pesan demasiado para el repositorio.

## Obtener un modelo

```bash
pip install ultralytics

# Modelo propio, entrenado con un dataset de contenedores.
yolo detect train data=contenedores.yaml model=yolo11s.pt epochs=100 imgsz=640
yolo export model=runs/detect/train/weights/best.pt format=onnx opset=12 imgsz=640

# O un preentrenado COCO, solo para probar el pipeline (no detecta contenedores).
yolo export model=yolo11n.pt format=onnx opset=12 imgsz=640
```

El fichero de etiquetas de `../labels/` debe coincidir exactamente con el orden de
clases con el que se entrenó el modelo. Si no coincide, la aplicación lo avisa por
la salida de error.
