# AudioDúo 2.3 · Windows 11

## Ajuste manual de tres parlantes

La pantalla principal reúne una tarjeta por parlante físico: MS-2054BT, JHW-V339 y cable. Cada tarjeta tiene un número grande, deslizador y botones de ±1, ±10 y ±50 ms. **Más milisegundos lo hacen sonar después; menos, antes.** Los cambios se aplican y guardan sin reconectar.

Pulsa **Probar golpes** y usa **Silenciar** para comparar dos parlantes a la vez. **Solo** permite escuchar uno y se desactiva al pulsarlo otra vez. Ambas opciones conservan las conexiones y los volúmenes guardados; se restablecen al detener la sesión o abrir la calibración. Si una copia estaba desactivada, su tarjeta muestra **Activar**.

El monitor principal muestra solo los parlantes configurados. VB-CABLE es el puente interno y la salida 7.1 Surround Sound pertenece al programa de mejora de audífonos; no se presentan como parlantes adicionales. **Configurar salidas** despliega la ruta de origen y la primera copia; **Más parlantes** conserva la gestión avanzada. Para usar el puente, la salida de Windows debe ser **CABLE Input**.

## Monitor conjunto y calibración con micrófono

Cada parlante configurado tiene una fila con su nombre en el monitor compartido. **Congelar** detiene todas las ondas a la vez, sin detener el sonido. Las salidas desactivadas se identifican expresamente, sin inventar una señal. Las salidas detectadas adicionales siguen disponibles en **Más parlantes**.

**Calibrar con micrófono** permite marcar los parlantes que realmente están conectados y elegir la entrada por cable. Pausa música/videos, sitúa el micrófono en tu asiento y pulsa **Iniciar medición**. La prueba usa un tono de preparación y tres tonos identificables por salida. Compara el audio digital con el sonido capturado usando las marcas de tiempo QPC de WASAPI. Rechaza lecturas sin señal, inestables o sin marcas válidas. El micrófono se libera al terminar o cancelar; no se guarda ni se transmite la grabación.

También se mide el coste de la ruta real de copia. Los resultados son aproximados, dependen del micrófono y del ambiente, y se revisan antes de pulsar **Usar esta calibración**. Los ajustes anteriores se respaldan en archivos `.antes-calibrar`. No se instala ningún controlador ni se cambia automáticamente la salida predeterminada de Windows.

### Puente silencioso

Si hay una salida analógica libre, la calibración puede usarla como origen silencioso. Windows envía allí el audio; AudioDúo mantiene esa salida silenciada y distribuye copias independientes a los Bluetooth, cada una con su retraso. Así se puede compensar también el parlante que antes era la salida directa.

Para usar ese plan, selecciona la salida analógica indicada en **Sonido de Windows** y luego pulsa **Conectar audio**. La casilla **Puente silencioso** identifica este modo. Al detener o salir se restaura el estado de silencio anterior de esa salida. La X mantiene la distribución en segundo plano.

La salida usada como puente no puede ser además uno de los parlantes audibles. En la configuración con audífonos conectados para usar el micrófono, permite sincronizar los dos Bluetooth. Para sumar nuevamente los parlantes de cable habría que liberar su salida, usando otro origen o una salida virtual. El plan sin puente se conserva como alternativa cuando las diferencias de latencia permiten compensar la copia.

### Tres parlantes con VB-CABLE

Con VB-CABLE instalado por separado desde el sitio oficial, selecciona **CABLE Input (VB-Audio Virtual Cable)** como salida de Windows y como origen de AudioDúo. Usa la salida estéreo **CABLE Input**, no **CABLE In 16ch**. Activa **Puente silencioso** y configura los dos Bluetooth y la salida de cable como tres copias distintas. Ahora cada parlante audible tiene volumen, canales y retraso propios; el cable deja de recibir directamente el audio sin retrasar.

Al calibrar, marca los tres parlantes físicos. El puente virtual permanece fuera de esa selección y se utiliza para medir el coste de las tres rutas. La calibración conserva el puente configurado cuando está libre y, en su defecto, prefiere VB-CABLE frente a una salida analógica. El instalador del controlador no forma parte de la distribución de AudioDúo. Sigue la indicación de reinicio del fabricante después de instalarlo.

Si el puente ya está conectado y marcas todas sus copias activas, la calibración mantiene abiertas las conexiones. Reproduce los tonos en una copia a la vez y restaura sus volúmenes al terminar o cancelar. **Usar esta calibración** aplica los retrasos en vivo. Si detienes o cambias la conexión entre medir y aplicar, se exige repetir la medición. Esto evita reutilizar una medida de una sesión cuya demora cambió al reabrir el Bluetooth.

Distribuye el sonido de tu PC entre un parlante principal y varias copias con volumen, canales y retraso independientes. Abre **INICIAR.cmd** o **AudioDuo.exe**; conserva `NAudio.dll` junto al ejecutable.

## Empezar

1. En Sonido de Windows, selecciona el Bluetooth principal como salida predeterminada. Selecciona ese mismo dispositivo a la izquierda de AudioDúo.
2. Elige a la derecha el primer acompañante, por ejemplo los parlantes por cable. Pulsa **Conectar audio**.
3. Abre **Más parlantes** para añadir otras salidas. Empareja antes los Bluetooth en Windows y pulsa **Actualizar** si no aparecen. Cada salida debe ser un dispositivo diferente.
4. Pulsa **Probar golpes** y ajusta el retraso de cada copia desde tu asiento. Los botones ±1 y ±10 ms, el número y el deslizador aplican el cambio en vivo. No necesitas detener ni reconectar.

La configuración original usaba MS-2054BT como principal, cable con 310 ms y JHW-V339 como copia adicional. La calibración puede sustituirla por un puente silencioso y nuevos retrasos para las salidas seleccionadas. Los 310 ms del cable no se aplican a los Bluetooth ni representan una medida universal.

## Distribución de canales

El principal reproduce directamente la mezcla original de Windows. En cada **copia** puedes elegir:

| Opción | Señal enviada a la copia |
|---|---|
| Estéreo L/R | Izquierda y derecha originales |
| Solo izquierda | L en ambos canales; útil para un parlante a la izquierda |
| Solo derecha | R en ambos canales; útil para un parlante a la derecha |
| Mono L+R | Promedio de izquierda y derecha en ambos canales |
| Ambiente L−R (simulado) | Diferencia entre izquierda y derecha, atenuada; puede reducir voces centradas y quedar en silencio con contenido mono |

Puedes experimentar con un acompañante a la izquierda y otro a la derecha, o usar estéreo en todos para cubrir la sala. El principal sigue reproduciendo la mezcla completa: esta ruta no permite filtrarlo porque Windows lo reproduce directamente.

La versión distribuye estéreo. No extrae diálogos, subwoofer o efectos traseros independientes de una película 5.1. Para eso habría que capturar el multicanal antes de mezclarlo, por ejemplo mediante una salida virtual y una matriz multicanal. Si la fuente tiene más de dos canales, las copias toman solamente su primer par; se recomienda una fuente estéreo en esta versión.

## Ondas y niveles

La opción **Sonido medido por micrófono** muestra ondas reales de la última calibración en vivo. Cada fila corresponde al primer tono capturado para ese parlante, alineado al envío digital del tono (0 ms). Las llegadas y diferencias se calculan con la mediana de tres tonos. La amplitud se normaliza por fila para facilitar la lectura; no representa igualdad de volumen. Las pruebas son separadas, no una separación de una grabación simultánea de los tres parlantes.

La medición se conserva solo en memoria y no es monitoreo continuo. Al modificar retrasos o conexión, la vista advierte que debes repetir la prueba. La vista digital sigue disponible desmarcando esa opción; sus ondas no sustituyen la medición acústica. Los audífonos usados como micrófono deben conectarse solo a la entrada de micrófono: la salida de cable debe seguir alimentando el parlante físico.

- Las ondas son envolventes estéreo de audio real capturado en las salidas de Windows, incluida cada copia. En silencio no se dibujan señales ficticias.
- Ambas filas comparten escala. Puedes ver 1, 2 o 4 segundos, **Congelar** y arrastrar los cursores A/B para comparar picos. Congelar no detiene el sonido.
- La ampliación vertical automática es la misma en ambas filas y se indica debajo de la gráfica. Permite ver señales débiles sin cambiar el volumen ni los medidores dBFS.
- Los cursores indican la distancia manual entre A y B. No calculan automáticamente el desfase entre filas. El tiempo de llegada de los bloques y el muestreo visual de 5 ms limitan su precisión. No son una medición acústica ni una calibración automática.
- Windows puede mostrar la señal antes del transporte y procesamiento físico Bluetooth. **Hacer coincidir las ondas no garantiza sincronía acústica.** Usa los golpes y la escucha final; una medición acústica precisa requeriría un micrófono.
- Los medidores muestran picos en dBFS, no decibelios de la habitación. La lectura de una salida puede incluir otras aplicaciones que reproduzcan directamente allí.

El retraso usa un historial continuo y una transición de unos 35 ms; no vacía la señal ni reinicia dispositivos al ajustar. Cambios grandes pueden producir un cambio audible momentáneo. Volumen y canales también tienen transición gradual.

## Guardado y segundo plano

El ajuste se guarda automáticamente tras una pausa breve y al ocultar o salir. **Guardar este punto** permite recuperar un retraso con **Volver a … ms**. `preferencias.txt` guarda la pareja principal; `parlantes.xml` guarda las copias adicionales.

La **X** y minimizar mantienen el audio junto al reloj. Doble clic en el icono vuelve a mostrar la ventana. **Salir** o **Salir y detener audio** cierran el programa. Abrirlo otra vez muestra la instancia existente. No se inicia automáticamente con Windows.

## Conexión y límites

El origen seleccionado en AudioDúo debe seguir siendo la salida de Windows: puede ser el Bluetooth directo o la salida analógica en modo puente. Si una aplicación tiene su propia salida fija, cámbiala al origen; algunas necesitan reabrirse. Se capturan señales en memoria, sin guardarlas ni enviarlas por Internet.

El volumen principal cambia el volumen del dispositivo en Windows. El volumen de las copias afecta al audio enviado por AudioDúo; también depende de los ajustes físicos y de Windows de cada dispositivo.

La cantidad de Bluetooth simultáneos depende del adaptador, los controladores y los dispositivos. AudioDúo utiliza las salidas activas de Windows; no crea enlaces Bluetooth ni instala controladores. La reproducción compartida adapta el formato si las salidas tienen distintas frecuencias de muestreo.

Una copia adicional que falle puede reintentarse individualmente sin detener las demás. Si falla la pareja principal, se detiene la distribución. **Reintentar** vuelve a abrir esa copia. Cada salida mantiene una cola fija de 30 ms: si un reloj corre más rápido o más lento, la velocidad se corrige de forma imperceptible (máx. 0,4 %) y, tras una pausa del video, la cola se vuelve a llenar a la misma demora.

Solo se puede añadir retraso a las copias, no adelantarlas. Si una copia ya llega tarde a 0 ms, habría que usar como principal el parlante más lento y recalibrar, o implementar una ruta virtual que permita retrasar todas las salidas.

Evita usar el micrófono del Bluetooth durante la escucha para que no cambie a modo de llamada. Contenido protegido o exclusivo puede no capturarse. Reconectar un Bluetooth puede cambiar su latencia.

## Compilar y comprobar

**COMPILAR.cmd** compila una versión candidata, ejecuta las pruebas y sustituye la anterior solo si pasan. Cierra la app con **Salir** antes de compilar. Si falta la biblioteca, `RESTAURAR.ps1` descarga NAudio 1.10.0 desde NuGet y verifica su SHA-256; no se necesita .NET SDK.

El icono tiene tamaños de 16 a 256 px. `assets/AudioDuo.svg` es su fuente vectorial; `herramientas/Artwork.cs` genera el PNG y el ICO sin servicios externos.

Comprobaciones del ejecutable:

- `--self-test`: retraso, transiciones, canales, tres rutas independientes, lectura PCM y silencio.
- `--studio-smoke`: golpes breves, comprobación de las salidas configuradas y cambios de retrasos/canales conservando los mismos dispositivos; restaura los ajustes.
- `--tray-smoke`: X, restauración, minimizar y salida con audio activo.
- `--devices`: lista las salidas activas en `dispositivos.txt`.

Las pruebas de estudio/bandeja se ejecutan con la app normal cerrada. La primera requiere que los parlantes configurados estén activos y el principal sea la salida de Windows. Biblioteca: [NAudio 1.10.0](https://www.nuget.org/packages/NAudio/1.10.0), Microsoft Public License; ver `LICENCIA-NAudio.txt`.
