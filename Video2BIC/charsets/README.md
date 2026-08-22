# Alfabetos del reconocedor de texto

El fichero de alfabeto asocia cada clase que emite un modelo CTC con el caracter
que representa. Tiene que coincidir **exactamente** con el que se uso al
entrenar: un alfabeto desplazado una posicion no da un error, da texto
equivocado.

`bic-alphanumeric.txt` contiene los 36 caracteres que pueden aparecer en un
codigo BIC, un caracter por linea, en el orden habitual de PaddleOCR
(`0-9` y despues `A-Z`). Es tambien el alfabeto por defecto de la aplicacion, asi
que solo hace falta indicarlo con `--ocr-charset` si el modelo usa otro.

Se admiten los dos formatos habituales:

- un caracter por linea, como este fichero;
- todos los caracteres en una sola linea.

La clase «en blanco» del CTC **no se escribe** en el fichero: se indica aparte
con `--ocr-blank first` (indice 0, lo que usan PaddleOCR y la mayoria de CRNN
publicados) o `--ocr-blank last` (el convenio por defecto de `torch.nn.CTCLoss`).
