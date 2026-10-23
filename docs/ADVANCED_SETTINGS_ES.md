# Qué hacen los ajustes avanzados

Desde la versión 0.8.2 estos controles están ocultos. Los valores guardados se conservan. Esta página sirve como referencia para perfiles anteriores.

Para jugar, selecciona el ejecutable real, saves, biblioteca y **Modo en vivo de Supermedo**. Abre el manager antes de una sesión nueva del juego y usa tu launcher habitual. **Modo con el juego cerrado** permite gestionar checkpoints sin una partida abierta.

| Opción en inglés | Para qué sirve | Uso habitual en vivo |
| --- | --- | --- |
| Extra game process names | Nombres adicionales del proceso del juego, separados por punto y coma. Evitan restaurar archivos con otro proceso del juego aún abierto. No agregan compatibilidad con nuevos ports. | Vacío; bbport/bb-probe y el ejecutable configurado ya se detectan. |
| Restart game after offline restore | Abre el juego después de reemplazar los saves con el juego cerrado. | Desactivado. F9 en vivo vuelve al título del mismo proceso. |
| Launcher for restart | Archivo .exe/.bat/.cmd que arranca el juego al usar la opción anterior. Puede ser tu launcher de Supermedo. Es distinto del ejecutable nativo usado para detección. | Vacío si no usas reinicio sin conexión. |
| Arguments (.exe) | Parámetros de arranque que necesita ese launcher .exe. No son ajustes gráficos del manager. Los de .bat/.cmd deben estar en el propio script. | Vacío salvo que tu launcher los requiera. |
| Working directory | Carpeta desde la cual se ejecuta ese launcher; sirve para que encuentre sus archivos relativos. Debe ser la carpeta que espera el launcher. | No hace falta para restauración en vivo. |
| Request window close | En una restauración sin conexión, solicita al juego cerrar su ventana y espera a que termine. No mata el proceso ni garantiza guardar el progreso. Sin marcarlo, debes salir normalmente y el manager espera. | Desactivado. |
| Connect to running game | Busca el runtime compatible y comprueba el estado del puente. No abre el juego y no convierte un port distinto en compatible. | La detección normal ya es automática. |

Abrir primero un juego nuevo y después el manager se probó en la copia aislada: lo detecta, pero informa Solo observación y rechaza operaciones en vivo. El puente necesita estar conectado antes de que el juego empiece a utilizar sus saves. Reabrir el manager si el puente ya estaba conectado es un caso diferente; no debe confundirse con ese arranque nuevo.

La pestaña Help & recovery se usa exclusivamente si quedó interrumpido un reemplazo de archivos. Para elegir una partida, usa Checkpoints o F11.
