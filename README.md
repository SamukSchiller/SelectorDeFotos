# Selector de Fotos

**Versión 1.0** (octubre de 2026) · [English](README.en.md) · [Descargar](https://github.com/SamukSchiller/SelectorDeFotos/releases/latest) · Licencia [MIT](LICENSE)

Visor portable para elegir fotos de una carpeta grande (por ejemplo, las miles de fotos de una boda): marca favoritas y no deseadas, compara, revisa ráfagas y copia la selección. No necesita instalación: basta con copiar `SelectorDeFotos.exe` y ejecutarlo (Windows 10/11).

![Visor con la tira de miniaturas](docs/screenshots/main.png)

| Cuadrícula (G) | Comparar (C) | Ráfagas (B) |
|---|---|---|
| ![Cuadrícula](docs/screenshots/grid.png) | ![Comparar](docs/screenshots/compare.png) | ![Ráfagas](docs/screenshots/burst.png) |

## Video tutorial

https://github.com/user-attachments/assets/ad137a5e-65ff-4d98-8de3-cf1b6f597cd1

Tutorial básico de uso (1:46): cómo marcar con las flechas, comparar, revisar ráfagas y copiar tu selección. [Ver en GitHub](https://github.com/SamukSchiller/SelectorDeFotos#video-tutorial).

## Descargar e instalar
1. Descarga `SelectorDeFotos-1.0.zip` desde [Releases](https://github.com/SamukSchiller/SelectorDeFotos/releases/latest).
2. Descomprímelo en cualquier carpeta (o en un pendrive) y abre `SelectorDeFotos.exe`.
3. La primera vez, Windows puede mostrar «Windows protegió su PC», porque el programa no está firmado digitalmente. Pulsa **Más información → Ejecutar de todas formas**.

Requisitos: Windows 10 u 11 (usa .NET Framework 4.8, que ya viene con Windows). No escribe en el registro ni instala nada; para desinstalar, borra la carpeta.

## Lo básico
- **Alt** abre el menú de opciones ⚙ (después, flechas y Enter).
- **⚙ → Acerca de** muestra la versión, las carpetas en uso y dónde está el registro de errores.
- **Registro de errores**: si ocurre un error inesperado, se guarda con fecha y detalle en `data\errores.log` de la carpeta de datos (sin carpeta de datos, junto al programa). Las marcas no se ven afectadas; sirve para poder diagnosticarlo.

**Nunca modifica, mueve ni borra las fotos originales.** Las marcas se guardan aparte en `SelectorDeFotos.marcas.json`, dentro de la carpeta de datos.

## Uso
1. **Carpeta de fotos…**: la carpeta que quieres revisar (no se incluyen las subcarpetas).
2. **Carpeta de datos…**: aquí se guardan las marcas, las copias y la lista exportada.
3. Revisa las fotos con el teclado:

| Tecla | Acción |
|---|---|
| ← / → | Foto anterior / siguiente |
| ↑ | Sin marca → favorita · No deseada → sin marca |
| ↓ | Favorita → sin marca · Sin marca → no deseada |
| Ctrl + Z | Deshacer la última marca (vuelve a esa foto) |
| F / Shift + F | Ir a la siguiente / anterior favorita |
| N / Shift + N | Ir a la siguiente / anterior foto sin marca |
| X / Shift + X | Ir a la siguiente / anterior no deseada |
| U o Retroceso | Quitar la marca de la foto actual |
| Espacio | Vista completa: solo la foto en toda la pantalla (sin barra superior, miniaturas ni barra de estado). Todo sigue funcionando (navegar, marcar, zoom, comparar, ráfagas); lo que cambia la distribución (G, M, columnas, posición de la tira) primero la cierra. Espacio o Esc para salir |
| Alt + F / N / X | Mostrar u ocultar favoritas / sin marca / no deseadas (filtro «Mostrar») |
| C | Activar / desactivar comparar (Esc también lo desactiva) |
| B | Activar / desactivar el modo ráfaga (las ráfagas se ven lado a lado) |
| C C (doble, rápida) | Cambiar el formato de comparación: siguiente → anterior → anterior y siguiente |
| M | Miniaturas abajo / a la derecha |
| G | Cuadrícula de miniaturas a pantalla completa |
| Ctrl + G | Ir a una foto: por nombre (IMG_8410 o solo 8410) o por posición (#1156) |
| I | Panel de información: histograma (con aviso si hay zonas quemadas o sombras recortadas), cámara, objetivo, f/, velocidad, ISO, focal |
| E | Mostrar la foto en el Explorador |
| O | Abrir la foto con el programa predeterminado |
| P | Presentación de favoritas |
| Rueda sobre la foto, + / − | Zoom (con zoom, arrastra para mover la foto) |
| Z o clic de rueda (mantener) | Lupa: la foto al 100 % donde está el ratón; sigue al ratón y al soltar vuelve todo como estaba |
| Doble clic | Tamaño real (100%) / restaurar |
| Esc o 0 | Restaurar el zoom. Esc sin zoom sale de lo que esté abierto, en este orden: vista completa, comparar, pantalla completa |
| Inicio / Fin | Primera / última foto |
| RePág / AvPág | Saltar 10 fotos. En la vista de ráfaga: página anterior / siguiente (12 fotos por página); desde la primera o la última, a la ráfaga de al lado |
| Ctrl + «+» / Ctrl + «−» | Ampliar / reducir toda la interfaz (también desde ⚙) |
| Ctrl + 0 | Interfaz al 100 % |
| F11 | Pantalla completa |
| F5 | Volver a leer la carpeta |

El botón **?** de la barra superior muestra estos atajos.

## Marcar con el ratón
**Miniaturas** (tira lateral, tira horizontal o cuadrícula): al pasar el ratón aparecen dos botones redondos:
- **Arriba a la derecha, ✓**: favorita. Otro clic la quita.
- **Arriba a la izquierda, ✕**: no deseada. Otro clic la quita.

El botón se ve relleno de color si la foto ya tiene esa marca. Marcar así **no cambia la foto seleccionada** (la tira no se mueve, así puedes desmarcar enseguida con otro clic) y se deshace con Ctrl + Z, como cualquier marca. En miniaturas muy pequeñas los botones no aparecen.

**Fotos grandes** (visor, cada foto de comparar y cada foto de la vista de ráfaga): al acercar el ratón a la parte de abajo de la foto aparece una barra **[✕ No deseada] [✓ Favorita]**; al alejarse se oculta con un fundido. La marca de esa foto se ve resaltada y otro clic la quita. En la foto actual funciona como ↑ / ↓ (animación, avanzar al marcar); en las demás marca sin cambiar el foco. En fotos estrechas se muestra solo con iconos. Al abrir la app, la barra del visor se ve unos segundos.

**Clic derecho sobre una foto**: arriba están «Favorita», «No deseada» y «Quitar marca». En comparar y en la vista de ráfaga, el menú (marcas, abrir, Explorador, copiar) actúa sobre la foto en la que se hizo clic, cuyo nombre se ve arriba del menú.

## Pantalla
- **Marcas**: con una favorita, la barra inferior se pone verde vivo; con una no deseada, roja. En las miniaturas, las favoritas llevan marco verde y ✓, y las no deseadas aparecen atenuadas con ✕.
- **Recuento**: a la derecha de la barra inferior se ven siempre (también en pantalla completa) las favoritas, las no deseadas y las fotos sin marca.
- **Miniaturas**: la foto actual queda siempre en el centro. El botón de posición (o la tecla M) las lleva abajo o a la derecha. El botón ▮ cambia entre 1, 2 y 3 columnas (lateral) o filas (abajo), por separado para cada posición; el panel mantiene su tamaño. Arrastra el borde de la tira para cambiar su tamaño y usa la barra fina para moverte rápido.
- **Momentos**: una etiqueta naranja con la hora (por ejemplo «13:08») en una miniatura indica que entre esa foto y la anterior pasaron más de 30 segundos, es decir, que empieza un momento nuevo. Sirve para ver de un vistazo qué fotos son del mismo momento (solo al ordenar por fecha).
- **Mostrar**: combina Favoritas / Sin marca / No deseadas. Una foto que acabas de marcar sigue visible hasta que avanzas.

## Opciones (botón ⚙)
El menú está agrupado en submenús: **Visor** (comparar, panel de información, pantalla completa), **Miniaturas** (**Lateral**: a la derecha o a la izquierda, con 1 a 3 columnas; **Horizontal**: abajo o arriba, con 1 a 3 filas; por defecto derecha y abajo, y M cambia entre lateral y horizontal; la cantidad de cada una se puede cambiar sin cambiar la que está en uso; cuadrícula, centrar la actual, justificada, separar momentos, guardar en disco), **Marcas** (avanzar al marcar, icono, animación, borde de la foto actual) y **Ráfagas** (modo ráfaga, agrupar fotos parecidas). A la vista quedan presentación, ir a una foto, importar lista y objetivo; abajo, **Interfaz** (tamaño, barra compacta) y restablecer preferencias. Las opciones activas llevan ✓ y, al pasar el ratón, un texto explica cada una. Los menús de clic derecho usan los mismos submenús.

- **Avanzar al marcar** (desactivada por defecto): al marcar con ↑ / ↓ pasa sola a la siguiente foto (quitar una marca no avanza). En la vista de ráfaga, Shift + ↑ pasa a la primera foto después de la ráfaga.
- **Comparar y el modo ráfaga no van juntos**: activar uno desactiva el otro. Mientras cualquiera de los dos está activo, abajo al centro se ve un indicador con la tecla para desactivarlo (clic sobre él también lo desactiva).
- **Comparar fotos** (tecla C) y **Comparar con**: elige entre «La anterior y la siguiente» (opción por defecto: tres fotos, con la actual en el centro), «La siguiente» (actual a la izquierda y siguiente a la derecha) o «La anterior» (anterior a la izquierda y actual a la derecha). El zoom va sincronizado y las marcas se aplican a la actual.
- **Separar momentos en la tira**: activa o desactiva las etiquetas con la hora.
- **Cuadrícula justificada** (activada por defecto): sin huecos entre fotos. En el lateral, cada fila ajusta su altura para llenar el ancho. Abajo, solo con **1 fila**: cada foto ocupa su ancho natural, con la actual en el centro. Con 2 o 3 filas abajo se usan siempre casillas iguales.
- **Tamaño de la interfaz**: ampliar, reducir o restablecer (del 50 % al 200 %, de 10 % en 10 %). Se recuerda entre sesiones.
- **Borde de la foto actual** (submenú): blanco fijo, **degradado que gira** (por defecto) o **líneas que avanzan**. Se aplica en la tira, la cuadrícula, comparar y la vista de ráfaga; solo se anima el borde de la foto actual.
- **Marca de la foto** (submenú):
  - **Mostrar el icono en el visor** (activada por defecto): ✓ verde o ✕ roja en la esquina superior derecha del visor y de los paneles de comparar. Las miniaturas siempre lo muestran.
  - **Mosaico de la marca en el fondo** (activada por defecto): alrededor de la foto, el fondo del visor (y de cada foto de comparar) se llena de checks verdes o cruces rojas muy suaves, en filas levemente diagonales y más tenues cerca de la foto. Sin marca, el fondo queda liso.
  - **Animar la marca (al cambiar de foto y al marcar)** (activada por defecto): al llegar a una favorita o no deseada, o al marcarla con ↑ / ↓, su icono aparece en el centro y crece mientras se desvanece. Al quitar una marca, su contorno vacío se encoge y se desvanece.
- **Guardar miniaturas en disco** (desactivada por defecto): guarda las miniaturas en `data\miniaturas` dentro de la carpeta de datos, en dos tamaños (400 y 800 px), para que las carpetas grandes carguen mucho más rápido. Se van creando en segundo plano, empezando por las fotos cercanas a la actual, y la foto que estás viendo siempre tiene prioridad. Abajo a la derecha se ve el progreso. Si la desactivas, se borran esas miniaturas para liberar espacio (nunca las fotos ni otros archivos).
- **Barra superior compacta**: solo los nombres de las carpetas, con la ayuda en el botón ?.
- **Restablecer preferencias…**: vuelve a los valores por defecto el tamaño de la interfaz, las miniaturas, comparar, el panel de información, la barra compacta, el objetivo, el orden, los filtros y la presentación. Se mantienen las carpetas, todas las marcas y la opción de guardar miniaturas en disco (con las ya guardadas).
- **Objetivo de favoritas**: por ejemplo 80 para un álbum. Abajo a la derecha se ve «✓ 57 / 80 favoritas»: en verde al llegar y en naranja si te pasas.

## Carpeta de datos
En la raíz están solo tus selecciones (`SelectorDeFotos.marcas.json`), las carpetas de copias de fotos (`seleccionadas`, `sin_marca`, `no_deseadas`) y la lista exportada. Todo lo interno de la app (copia anterior, copias de seguridad, miniaturas y bloqueo) va en la subcarpeta `data`. Las carpetas de datos de versiones anteriores se reorganizan solas la primera vez.

## Seguridad de las marcas
- **Guardado seguro**: cada guardado se escribe primero a un archivo temporal, se fuerza a disco, se verifica y solo entonces reemplaza al anterior. Un fallo o corte de luz no deja el archivo a medias. La versión anterior queda en `data\marcas.bak`.
- **Copias de seguridad**: una al día y otra antes de quitar marcas, en `data\copias_de_seguridad` dentro de la carpeta de datos (se guardan las 30 más recientes).
- **Archivo dañado**: se aparta como `.danado-…` y las marcas se recuperan automáticamente desde la copia más reciente.
- **Archivo bloqueado** (OneDrive, antivirus…): la app avisa y esa sesión no guarda nada, para no sobrescribir las marcas buenas.
- **Dos ventanas a la vez** con la misma carpeta de datos: la segunda queda en solo lectura. Lo avisa al abrir y, mientras dure, se ve **«Solo lectura»** abajo a la izquierda (y en el título de la ventana). Al cerrar la otra ventana, un clic en «Solo lectura» vuelve a leer las marcas y esta ventana empieza a guardar.
- **Al cerrar**: si el último guardado falló (por ejemplo, porque se desconectó el disco), la app avisa y deja seguir para volver a intentarlo.
- **Fotos que ya no existen**: si hay marcas de fotos que se borraron, movieron o renombraron, la app las lista, guarda una copia de seguridad y las quita. Las fotos nuevas no afectan a nada.

## Cuadrícula a pantalla completa (G)
El botón ⊞ de la barra superior (o la tecla **G**) amplía las miniaturas a toda la ventana, con el modelo del lateral.
- **← →** para moverte y **↑ ↓** para marcar, como siempre.
- **+ / −** (o el botón de columnas): menos o más columnas, entre 3 y 10. Por defecto se calculan según el ancho (hasta 8), así que nunca se muestran cientos de fotos a la vez, ni en monitores grandes.
- **Doble clic** en una foto, **Enter**, **Esc** o **G**: volver al visor con esa foto.

## Ráfagas (B)
Las fotos tomadas una detrás de otra (como mucho 2 s entre cada una, según la fecha de captura) forman una **ráfaga**. Se detectan solas al leer las fechas, al instante. El tiempo se puede cambiar en ⚙ → Ráfagas → **Tiempo entre fotos** (1 s: solo ráfagas muy rápidas; 3 s: también disparos más espaciados).
- En la tira, cada foto de una ráfaga lleva una etiqueta pequeña con su posición, por ejemplo **2/5**.
- **B** activa o desactiva el **modo ráfaga** (se recuerda). Mientras está activo, abajo al centro se ve «Modo ráfaga · B desactivar» (clic también lo desactiva) y, cada vez que llegas a una foto de una ráfaga, se ven todas sus fotos lado a lado (hasta 12 por página), con la actual en azul. Con una foto suelta se ve el visor normal.
- **← →** (o clic) eligen, **↑ ↓** marcan como siempre.
- Al pasar de la última con **→** (o de la primera con **←**) se sigue a la ráfaga de al lado sin cerrar la vista; si la foto de al lado es suelta, se vuelve al visor con ella.
- **Shift + ↑**: la actual queda como la **única favorita** de la ráfaga; todas las demás (también las que eran favoritas) pasan a no deseadas. Ctrl + Z lo deshace de una vez.
- **Rueda**: zoom en todas a la vez, en el mismo punto (para comparar caras y ojos); **arrastrar** para moverse; **0** vuelve al tamaño normal.
- **Doble clic** en una foto la abre sola en el visor (desactiva el modo); **B** o **Esc** desactivan el modo (Esc con zoom primero vuelve al tamaño normal).

Con **«Guardar miniaturas en disco»** activado, el mismo proceso de fondo que crea las miniaturas analiza cada foto una vez (resultado en `data\analisis.json`):
- **◎ Más nítida**: en la vista de ráfaga, la foto con más detalle fino del grupo (se mide en la zona más nítida de la foto, así un fondo desenfocado no la penaliza).
- **Fotos parecidas** (opción «Ráfagas: agrupar también fotos parecidas», activada por defecto): une a la ráfaga fotos casi iguales tomadas hasta 90 s después de la anterior (la misma pose repetida). La vista se llama entonces «Ráfaga y fotos parecidas».

## Presentación de favoritas (P)
Muestra las favoritas a pantalla completa, empezando por la actual si es favorita, con fundido entre fotos.
- **Espacio**: pausa / reanudar · **← →** (o clic / rueda): anterior / siguiente · **↑ / ↓**: más / menos tiempo por foto, de 0,5 en 0,5 s (de 0,5 a 30 s) · **H** o **?**: volver a ver los atajos · **Esc** o **P**: salir. Al salir, el visor queda en la foto que se estaba viendo (o en la más cercana que muestre el filtro).
- La velocidad elegida se recuerda.

## Miniatura actual centrada o desplazamiento libre
Por defecto la foto actual queda siempre en el centro de la tira y la lista se mueve a su alrededor. Desactivando **«Miniatura actual siempre centrada»** (⚙ o clic derecho sobre las miniaturas) se usa el desplazamiento libre, como en Lightroom:
- Al hacer clic en una miniatura se selecciona **sin que la tira se mueva**.
- Con las flechas, la tira solo avanza cuando la foto actual se saldría de la vista, y lo justo para que se vea entera.
- La **rueda** y la **barra fina** mueven la vista sin cambiar la foto seleccionada.

Vale para la tira lateral, la tira de abajo y la cuadrícula.

## Clic derecho sobre las miniaturas
Opciones de la tira y de la cuadrícula, con su estado (✓): miniaturas a la derecha o abajo, columnas (lateral) o filas (abajo), cuadrícula a pantalla completa y sus columnas (automático o de 3 a 10), cuadrícula justificada, separar momentos, guardar miniaturas en disco, modo ráfaga y borde de la foto actual.

## Clic derecho sobre la foto
Abrir con el programa predeterminado, «Abrir con…», mostrar en el Explorador y copiar el nombre o la ruta. Debajo, todas las opciones del visor con su estado (✓) y su atajo: comparar y «Comparar con», modo ráfaga, cuadrícula, pantalla completa, panel de información, icono de la marca, animar la marca, borde de la foto actual y presentación de favoritas.

## Copiar, exportar e importar
- **Copiar fotos…**: copia a la carpeta de datos las categorías que elijas: `seleccionadas`, `sin_marca` y `no_deseadas`. La copia es idéntica byte a byte, así que mantiene los metadatos EXIF, y conserva el **mismo nombre** y las **mismas fechas** de creación y modificación. Si la copia ya existe, se omite. Si hay otro archivo distinto con el mismo nombre, no se sobrescribe nunca: la copia se guarda como `nombre (1).jpg`.
- **Importar lista de favoritas…** (menú ⚙): pega una lista de fotos (por ejemplo, las que eligió tu pareja y te mandó por WhatsApp) o abre un .txt. Valen nombres completos, sin extensión o solo el número. La app indica cuántas encontró y cuáles no, y marca las encontradas como favoritas. **Ctrl + Z** deshace toda la importación de una vez.
- **Exportar lista**: guarda los nombres de las favoritas en `favoritas - <carpeta>.txt` y los copia al portapapeles separados por comas, listos para enviar al fotógrafo.

Formatos: JPG, PNG, BMP, GIF, TIFF, WEBP, HEIC/HEIF y AVIF. Para HEIC y AVIF hacen falta las extensiones de Microsoft Store («Extensiones de imagen HEIF» / «Extensión AV1»).

## Compilar
Ejecuta `build.cmd`. Usa el compilador de C# que ya trae Windows (.NET Framework 4.8), así que no hace falta instalar nada. `build.cmd ruta\otro.exe` compila en otra ubicación.

El código está en `src`, repartido en carpetas por tema (la ventana principal es una sola clase, `MainWindow`, dividida en varios archivos):

- `Program.cs`: punto de entrada.
- `Base`: modelos y preferencias (`Models.cs`), lectura de fotos (`Imaging.cs`), cola de carga, funciones de Windows y tema oscuro.
- `Ventana`: armado de la ventana (`MainWindow.cs`), visor con zoom y lupa, comparar, vista completa, borde de foco, panel de información e histograma, ir a una foto, presentación, preferencias y registro de errores.
- `Miniaturas`: tira de miniaturas, desplazamiento libre y cuadrícula.
- `Marcas`: marcas, teclado, filtros y deshacer (`Marks.cs`), barra de marcar sobre las fotos grandes y botones de las miniaturas.
- `Rafagas`: ráfagas y análisis de nitidez y parecido.
- `Datos`: carpetas y guardado seguro, copiar fotos, miniaturas en disco, importar y exportar lista.
- `Menus`: menú ⚙, menús de clic derecho y ayudas para los menús.
- `Dialogos`: una ventana por archivo (copiar, ayuda, objetivo, ir a, importar, presentación, acerca de).
- `Pruebas`: modo de prueba (`--autotest`).

`actualizar.ps1` cierra la app (guardando), compila y la vuelve a abrir; con `-Abrir`, la abre aunque estuviera cerrada.

## Licencia
© 2026 [SamukSchiller](https://github.com/SamukSchiller). Publicado con la licencia [MIT](LICENSE): puedes usarlo, copiarlo, modificarlo y distribuirlo libremente, manteniendo el aviso de autor y de licencia. Los cambios de cada versión están en [CHANGELOG.md](CHANGELOG.md).
