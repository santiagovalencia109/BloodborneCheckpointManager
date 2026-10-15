# F9 con cierre y reinicio — versión 0.2

## Configurar una vez

En Ajustes activa **Reabrir el juego después de restaurar**. Selecciona un **Archivo de arranque** existente y su **Directorio de trabajo** habitual. El ejecutable del juego sirve para identificar el proceso; el arranque puede ser otro, como `run.bat`. Si el arranque es `.exe`, puedes proporcionar argumentos. Para `.bat`/`.cmd`, deja Argumentos vacío: guarda parámetros y variables en tu propio script probado o en los ajustes del launcher. El manager no copia la línea de comandos ni el entorno de un proceso abierto y no supone las rutas del port.

El directorio de trabajo se propone como la carpeta del archivo cuando lo seleccionas, pero debes comprobarlo. Asegúrate de que el arranque utiliza el mismo `user` y modo solo/party que la carpeta de saves configurada. **Guardar ajustes** está al final de la pestaña: usa su barra de desplazamiento.

Un launcher gráfico puede requerir pulsar su botón Jugar. El manager detecta el proceso del juego por la ruta configurada; no pulsa Continuar ni confirma que el personaje haya terminado de cargar.

## Dos opciones de cierre

**Predeterminada:** F9 espera hasta 120 segundos a que salgas normalmente desde el juego. Así puedes conservar el progreso reciente en el backup de emergencia.

**Solicitar cierre de ventana:** pide cerrar solo un proceso cuya ruta completa coincide con el ejecutable configurado. Si hay cero o varias coincidencias, acceso denegado, ausencia de ventana o no termina, aborta sin restaurar. Los alias adicionales bloquean operaciones pero nunca se cierran automáticamente. No se cierra el launcher ni se usa `Process.Kill` contra el juego.

**La X del port inspeccionado llama a `TerminateProcess` dentro del propio port, sin solicitar un último guardado al motor.** Pedir ese cierre no significa “guardar y salir”: puede perderse progreso aún no persistido. El backup preserva los archivos que queden en disco cuando termine el proceso. Si necesitas conservar el progreso más reciente, sal primero normalmente. La confirmación de F9 explica el modo elegido.

## Secuencia

1. Comprobar configuración de arranque y checkpoint antes de pedir el cierre.
2. Esperar salida, o pedir cierre y esperar. Se exigen dos segundos seguidos sin los procesos detectados.
3. Volver a comprobar el estado, crear backup y restaurar con el mecanismo existente.
4. Solo si la restauración terminó sin error, iniciar una vez el arranque configurado.
5. Esperar hasta 60 segundos a detectar el ejecutable por ruta. Detectado no equivale a jugable ni descarta una caída posterior.

Desactiva relanzadores externos: los controles de procesos no ofrecen exclusión absoluta frente a un arranque concurrente. No abras el juego manualmente durante la transacción.

## Cancelar o resolver un error

- **Cancelar espera** antes de restaurar detiene el flujo. Si ya se solicitó cerrar la ventana, esa solicitud no se puede deshacer.
- Durante la transacción, cancelar deja terminar la restauración y omite el arranque.
- Si el launcher ya se inició, cancelar solo deja de esperar; no lo termina.
- Si falla la restauración, no se arranca el juego. Usa Recuperación si hay un diario pendiente.
- Si falla el arranque o tarda más de 60 s, el resultado dice **restaurado, arranque sin confirmar**. Abre el juego manualmente; no hace falta restaurar otra vez. El launcher puede seguir ejecutándose.

La actualización conserva manifiestos, configuración anterior y biblioteca. Los respaldos de emergencia están ocultos por defecto; marca la casilla de la pestaña Checkpoints para mostrarlos.
