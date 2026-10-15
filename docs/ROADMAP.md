# Fase futura: checkpoints durante el juego y recarga en caliente

Actualización 0.5: el puente externo para el Supermedo original verificado pasó captura/restauración de viales y confirmación manual de F5/F9. El manager detecta el proceso aunque lo arranque otro launcher. Esta primera versión debe conectarse durante el arranque; incorporar conexiones seguras a sesiones ya iniciadas y otros builds requiere adaptadores y pruebas adicionales. Véase [EXTERNAL_BRIDGE_USAGE.md](EXTERNAL_BRIDGE_USAGE.md).

La versión 0.3 experimental ya implementa el puente y tiene un primer recorrido positivo de captura, restauración en título y Continue, con recuperación de ecos/viales confirmada por el usuario. El estado actual está en [LIVE_EXPERIMENT.md](LIVE_EXPERIMENT.md); el texto siguiente conserva el plan de la versión 0.2. Pendiente: ciclos repetidos, persistencia tras autosave, fallos en el port real y recarga directa dentro del mundo.

Actualización 0.2: el usuario ya confirmó la restauración de saves de la primera entrega. Se implementaron nueva interfaz y flujo de cierre/espera, restauración y arranque configurable; el ciclo Windows pasó una prueba con proceso sintético. La siguiente investigación concreta, con funciones identificadas, problemas de exclusión y el experimento de volver al título, está en [HOT_RELOAD_RESEARCH.md](HOT_RELOAD_RESEARCH.md). Aún no se ha validado recarga del personaje sin reiniciar el proceso.

El primer paso de aceptación es probar esta versión con una copia desechable de una partida real del commit documentado: crear, avanzar, salir, restaurar y comprobar personaje/progreso/metadatos. Repetir por separado para solo y party. Validar además botones, atajos globales y su conflicto con otras aplicaciones en una sesión interactiva.

La captura con juego abierto requiere un protocolo cooperativo del port. `runtime_file_save_activity` y los contadores internos de escritores son indicios útiles, pero no son una API externa ni bastan por sí solos para bloquear nuevas escrituras. Una futura extensión tendría que exponer un canal IPC local autenticado y versionado: adquirir una barrera de guardado, esperar cierre de escritores, vaciar buffers, entregar una generación estable, copiar y liberar la barrera. Un timeout debe liberar siempre el juego sin publicar un checkpoint incompleto.

La restauración en caliente requiere además un punto seguro del motor: detener acceso a saves, descargar estado y cachés de slots, restaurar, remontar y recargar el personaje/mapa mediante funciones verificadas de esa versión. No se inventan direcciones de memoria ni se intenta lograrlo suspendiendo hilos arbitrariamente. Si no hay una ruta de recarga fiable, la alternativa será salida y reinicio controlados, manteniendo los argumentos y perfil del launcher verificados.

En party, la sincronización de participantes y autoridad del progreso necesita diseño y pruebas aparte. No se habilitará por reutilizar la solución para un jugador. Cada versión del port deberá negociar capacidades y rechazar protocolos desconocidos.

Criterios de aceptación: pruebas repetidas frente a autosave, fallos durante cada fase, expiración del bloqueo, caída de ambos procesos, ausencia de pérdida del último backup válido, coherencia entre disco y estado cargado, y regresiones para solo/party. Hasta entonces, el modo con juego cerrado sigue siendo el único soportado.
