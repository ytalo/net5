"""
Genera 'tiny-yolo-fixture.onnx', un modelo minusculo con la misma forma de salida
que YOLOv8 (1, 4 + clases, anclas).

No detecta nada: existe para que las pruebas puedan ejercitar el camino real de
inferencia de YoloDetector (preprocesado, ONNX Runtime, parseo, letterbox y NMS)
sin descargar cientos de megabytes de pesos entrenados.

La puntuacion de la primera ancla es la media de los pixeles de entrada, de modo
que una prueba puede comprobar que el preprocesado llega de verdad al modelo:
con un fotograma negro la confianza es 0 y con uno blanco es 1.

Uso:  python3 make_fixture_model.py
"""

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

SIZE = 64          # entrada pequena para que la prueba sea instantanea
CLASSES = 2
ANCHORS = 3
FEATURES = 4 + CLASSES

# Ancla 0: caja centrada en (32, 32) de 20x16. Su puntuacion de clase 0 la fija
# la media de la imagen.
# Ancla 1: duplicado casi exacto de la 0, para que la NMS tenga algo que suprimir.
# Ancla 2: caja distinta con clase 1 y confianza fija alta.
base = np.zeros((1, FEATURES, ANCHORS), dtype=np.float32)
base[0, :4, 0] = [32.0, 32.0, 20.0, 16.0]
base[0, :4, 1] = [33.0, 32.5, 20.0, 16.0]
base[0, :4, 2] = [10.0, 50.0, 8.0, 8.0]
base[0, 4, 1] = 0.80      # clase 0, ancla duplicada
base[0, 5, 2] = 0.70      # clase 1, ancla independiente

# Solo la puntuacion de clase 0 del ancla 0 depende de la entrada.
mask = np.zeros((1, FEATURES, ANCHORS), dtype=np.float32)
mask[0, 4, 0] = 1.0

graph = helper.make_graph(
    nodes=[
        helper.make_node("ReduceMean", ["images"], ["mean"], keepdims=0),
        helper.make_node("Mul", ["mask", "mean"], ["scaled"]),
        helper.make_node("Add", ["base", "scaled"], ["output0"]),
    ],
    name="tiny-yolo-fixture",
    inputs=[helper.make_tensor_value_info("images", TensorProto.FLOAT, [1, 3, SIZE, SIZE])],
    outputs=[helper.make_tensor_value_info("output0", TensorProto.FLOAT, [1, FEATURES, ANCHORS])],
    initializer=[
        numpy_helper.from_array(base, "base"),
        numpy_helper.from_array(mask, "mask"),
    ],
)

model = helper.make_model(
    graph,
    opset_imports=[helper.make_operatorsetid("", 13)],
    producer_name="maritimevision-tests",
)
model.ir_version = 8          # compatible con ONNX Runtime 1.20

onnx.checker.check_model(model)
onnx.save(model, "tiny-yolo-fixture.onnx")
print("escrito tiny-yolo-fixture.onnx")
