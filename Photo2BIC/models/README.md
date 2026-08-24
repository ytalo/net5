# Modelos

Photo2BIC funciona sin ningun modelo: los dos motores de Tesseract solo necesitan
el binario del sistema, y los cuatro de nube solo credenciales. Este directorio es
para el motor `onnx`, que es opcional.

Los `.onnx` no se versionan: pesan demasiado para el repositorio (ver
`.gitignore`).

## Reconocedor de texto CRNN/CTC

Un modelo que lea **una linea de texto ya recortada** y devuelva la secuencia de
caracteres. No localiza texto; de eso se ocupa MSER, igual que con Tesseract.

La razon para molestarse en conseguirlo es que Tesseract viene del mundo de los
documentos escaneados y se le nota: sobre un rotulo con sombra dura, pintura
descascarillada o angulo, un modelo entrenado con **texto de escena** lee bastante
mejor.

```bash
# PaddleOCR: modelo de reconocimiento en ingles, exportado a ONNX.
pip install paddleocr paddle2onnx
paddle2onnx --model_dir en_PP-OCRv4_rec_infer \
            --model_filename inference.pdmodel \
            --params_filename inference.pdiparams \
            --save_file rec.onnx --opset_version 12
```

```bash
photo2bic read puerta.jpg --engines onnx,tesseract \
          --onnx-model models/rec.onnx \
          --onnx-charset ../Video2BIC/charsets/bic-alphanumeric.txt
```

El alfabeto por defecto son los 36 alfanumericos en mayusculas, que es lo unico
que puede aparecer en un codigo BIC. Si el modelo emite un numero de clases que no
cuadra con el alfabeto, la aplicacion lo dice al arrancar en vez de devolver texto
sin sentido.

Se comparte formato con Video2BIC: el mismo `.onnx` sirve para los dos, porque el
reconocedor es literalmente la misma clase (`Video2BIC.Core.Ocr.OnnxTextRecognizer`).

## Proveedor de ejecucion

Por defecto CPU, que para una fotografia suelta sobra. Con muchas fotografias y
una GPU disponible:

```bash
photo2bic batch fotos/ --engines onnx --onnx-model models/rec.onnx --proveedor Cuda
```
