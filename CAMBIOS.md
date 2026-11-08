# AudioDúo · reconexión robusta (08-11-2026)

- Un parlante borrado y emparejado de nuevo (Windows le da otro identificador) se reconoce por su nombre y conserva su configuración; nunca se asigna el mismo aparato a dos funciones.
- Parlantes nuevos: se agregan solos (opción en Ajustes) con «Deshacer»; «Ahora no»/«Deshacer» se recuerdan también por nombre.
- Botón «Quitar» en cada parlante adicional y «Quitar apagados» para limpiar los que ya no están.
- Si Windows cambia la salida mientras suena, la app ofrece volver con un clic.
- Hasta tres reintentos automáticos tras un fallo pasajero del audio.
- Corregido: con Ajustes abierto alguna vez, «Salir» y el cierre externo no cerraban la app.

# AudioDúo · interfaz nueva (16-10-2026)

- Ventana rediseñada: estado en vivo, barra de avisos, volumen general, tarjetas con medidor de nivel, ondas plegables y Ajustes aparte.
- Corregido: contenido desplazado o cortado al maximizar, botón Conectar que parecía activo, casilla del micrófono rota, onda vacía del parlante con mejora 7.1 (ahora se observa la salida de cable donde suena).
- Volumen: cada parlante tiene su volumen propio; el general aplica los mismos dB a todos con la curva de Windows.
- Conexión automática: al abrir, al encender o reconectar un parlante guardado vuelve a sonar solo; un parlante apagado se espera sin perder su configuración; los parlantes nuevos se ofrecen para agregarlos.
- «Conectar» pone el parlante principal como salida de Windows. Opción de iniciar con Windows junto al reloj (--tray).

# AudioDúo 2.3

- Corrección principal de sincronía: cada salida usaba una cola sin control que crecía o se vaciaba sola (0–600 ms) y se vaciaba de golpe al pasar de 600 ms. Por eso la demora de cada parlante cambiaba con el tiempo y entre sesiones, y ninguna calibración se mantenía. Ahora cada salida tiene una cola fija de 30 ms con corrección continua de deriva entre relojes.
- Conversión propia a la frecuencia de cada parlante (48 kHz → 44,1 kHz en Bluetooth), sin el remuestreador interno de NAudio y su búfer adicional.
- Captura del puente cada ~15 ms en lugar de bloques de ~50 ms; menos demora total frente al video.
- Informe de calibración: se aclara qué es la demora propia del parlante y se muestra la demora total frente al video.
- Vista acústica: cada fila se escala según el tono detectado, no según ruido o ecos.
- Pruebas automáticas nuevas con relojes desfasados, pausas y cambio de frecuencia.

# AudioDúo 2.1

- Todas las salidas visibles en la pantalla principal y en el mismo monitor con nombres, estados y escala compartida.
- Controles de copias adicionales integrados en la pantalla principal.
- Calibración acústica con micrófono, tonos identificables, marcas QPC y validación de consistencia.
- Compensación del coste de la ruta de copia; bloqueo de ajustes imposibles en modo directo.
- Puente silencioso por salida analógica para retrasar todos los Bluetooth de forma independiente, sin instalar un controlador.
- Respaldo de los ajustes anteriores al aplicar una calibración y restauración del silencio de la salida al detener.
- Corrección del dibujo y de la distribución de tarjetas al cambiar el tamaño de la ventana.

- Interfaz oscura con tarjetas de salida y controles de ajuste fino.
- Icono propio en el ejecutable, la ventana y la bandeja.
- Envolventes de cada salida real, niveles dBFS, escala compartida, pausa visual y cursores.
- Retraso en vivo con historial continuo y transición, sin detener/conectar.
- Varios parlantes con canales, retrasos y volúmenes independientes.
- Matriz estéreo: L/R, izquierda, derecha, mono y ambiente simulado.
- Preferencias por salida, guardado automático y recuperación del punto calibrado.
- Reintento individual y recuperación de acumulación de audio.
- Una sola instancia por sesión; volver a abrir muestra la ventana existente.
- La X conserva la reproducción en segundo plano.

Próximas mejoras: compensación continua de deriva entre relojes y entrada multicanal antes de la mezcla estéreo para distribuir 5.1 auténtico. El monitor digital y la medición acústica cumplen funciones distintas.
