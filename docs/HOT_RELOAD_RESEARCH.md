# Investigación: restaurar sin cerrar Bloodborne

**Actualización:** la investigación estática siguiente se conserva como antecedente. Se implementó el puente y se compiló una copia aislada del port; captura, restauración en el título y Continue pasaron un primer recorrido real. El usuario confirmó recuperación de ecos y viales. El estado actual, límites y resultados están en [LIVE_EXPERIMENT.md](LIVE_EXPERIMENT.md).

Inspección estática del commit `c67ce7808f1b5be9013348f0e5119d7615c0babc` de visy. No se ejecutó un binario del juego ni se validaron direcciones contra el eboot del usuario. La referencia local no fue modificada. Las direcciones siguientes son las documentadas en ese commit; no son direcciones absolutas para escribir desde el manager.

## Resultado

Hay piezas reutilizables. La vía más pequeña que merece una prueba es **volver al título, restaurar con acceso a saves bloqueado y continuar dentro del mismo proceso**. No hay evidencia suficiente todavía para afirmar que volver al título invalide todas las cachés o que Continue relea el personaje. Si lo hace, esta vía puede ahorrar el reinicio del runtime/GPU. Si no, habrá que identificar la operación de descarga/recarga del motor.

No encontré una API externa existente que ofrezca “restaurar y recargar”. Esta versión del manager mantiene esa función desactivada. No se ha añadido escritura remota de memoria ni un parche sin comprobar.

## Piezas comprobadas

| Pieza | Evidencia | Utilidad y límite |
|---|---|---|
| Solicitar guardado | [game_state.cpp, RequestGameSave/GameSaveTick](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/gpu/shim/party/game_state.cpp#L482) | Encola una petición; el tick del hilo principal llama a SaveRequest (`0x1336DA0`) después de 30 frames estables y con separación mínima de 15 s. Comprueba bytes del código. Pedir no confirma que haya terminado de guardar. |
| Estado de carga/mundo | [game_state.cpp, ReadGameState](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/gpu/shim/party/game_state.cpp#L207) | Lee si hay mundo y carga. Sirve como señal, pero mundo ausente también puede ser una transición, no necesariamente título estable. |
| Actividad de archivos | [runtime_file.c](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/src/runtime_file.c#L193) | `runtime_file_save_activity` devuelve generación y escritores del sistema de archivos invitado. Actualmente es una función interna. |
| Copias mientras corre | [runtime_savepolicy.c](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/src/runtime_savepolicy.c#L368) | El port compara generación y escritores antes/después de copiar y descarta cambios. Reutilizable para investigar captura; no bloquea nuevos accesos ni garantiza por sí solo una transacción completa del motor. |
| Regreso al título | [cutscenes_endings.md, sección 1.4](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/docs/party/cutscenes_endings.md#L127) | La documentación upstream atribuye a EMEVD `2003[27]` una petición a través de SprjEventMan (`0x553B108`, cadena `+0x10`, campo `+8 = 2`). Es evidencia estática upstream, no una prueba nuestra de recarga ni una API lista para usar. |
| Continue automatizado | [party_director.cpp, AutoContinue](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/gpu/shim/party/party_director.cpp#L778) | Automatiza entradas al título, pero depende del enlace party y estado interno de una ejecución. No es una llamada genérica LoadGame ni debe habilitar party para usarla en solo. |
| Entradas de prueba | [runtime_pad.c, BB_PAD_FILE](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/src/runtime_pad.c#L638) | Permite un recorrido reproducible del menú sin automatizar el escritorio. Requiere configurar el entorno al arrancar y verificar la secuencia real de esa versión. |

`GameSaveTick` se ejecuta desde `CoopTick`; no debe suponerse disponible en solo sin revisar la instalación del hook y sus condiciones. El módulo de checkpoints debería tener su propio tick independiente de habilitar co-op.

## Dos límites concretos del contador de escritores

1. `runtime_file.c::do_close()` retira el descriptor de la tabla antes de ejecutar `commit_file()` y aumentar la generación. Hay un intervalo en que el contador puede ser cero y el commit seguir pendiente. Antes de usarlo como barrera hay que contar también commits en vuelo y serializar el permiso para abrir/escribir.
2. [runtime_savedata.c](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/src/runtime_savedata.c#L360) escribe `memory.dat` mediante stdio y gestiona param/icon mediante su propio `write_atomic`. Esas rutas no pasan necesariamente por el contador de `runtime_file`. La barrera debe abarcar datos, metadatos y memoria del save, además de renombrados, borrados y truncados.

Para captura puede intentarse copiar y reintentar con una generación global coherente. Para restauración se necesita exclusión de todos los accesos relevantes, especialmente escritores. Un periodo sin cambios de tamaño/fecha por sí solo no es suficiente.

## Por qué cerrar ventana no prueba un guardado seguro

En [window.cpp](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/gpu/shim/window.cpp#L333), la solicitud de cierre deja de procesar eventos. En [bbgpu.cpp](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/gpu/shim/bbgpu.cpp#L310), el hilo cierra la red y termina el proceso Windows. No espera a SaveRequest/commit. Por eso el nuevo cierre de ventana del manager es opcional y explícito; no se presenta como guardar y salir.

## Experimento siguiente, con save desechable

1. Fijar binario/commit y una copia de `user` exclusiva para pruebas, solo, sin otros procesos. Activar `BB_SAVE_TRACE=1`; añadir trazas temporales para mount/unmount, `memory_io`, commits en vuelo y estado de título.
2. Crear checkpoint A; progresar a B con una diferencia verificable (ecos/inventario y ubicación). Regresar al título normalmente. Registrar qué escrituras siguen produciéndose, descriptores y momento en que cesan. No reemplazar nada aún.
3. Instrumentar una barrera de acceso a saves. Mientras esté adquirida, sustituir B por A con backup/diario; liberar lecturas solo tras verificar el destino. Una desconexión del manager durante el reemplazo debe mantener la barrera y exigir recuperación; no reanudar escrituras sobre un árbol incompleto por timeout.
4. Seleccionar Continue y verificar trazas de apertura/lectura de `userdata*`, datos auxiliares y resultados dentro del juego. Repetir con Load Game. Si conserva B o vuelve a escribirlo, se ha demostrado una caché pendiente: localizar su reinicialización antes de ofrecer la función.
5. Repetir restauraciones, muerte, vuelta al título, cancelaciones y fallos en cada fase. Confirmar que un autoguardado posterior persiste A y que nunca sobrescribe el checkpoint externo. Party queda excluido inicialmente.

El punto decisivo es el paso 4: demostrar que A llega al estado cargado. El código de transporte IPC no es el principal interrogante.

## Integración propuesta, todavía no implementada

Un pequeño módulo opcional del port, separado del manager, expondría un pipe local con ACL del usuario, protocolo versionado y nonce por sesión. Respondería con commit/build, PID, ruta canónica de user, modo y capacidades. Las peticiones del manager se encolarían al hilo principal; el servidor IPC no llamaría directamente al motor.

Estados: `Idle → ReturnToTitle → AwaitIdle → LeaseGranted → RestorePrepared → RestoreCommitted → Reload → VerifyWorld`. La concesión contendría un ID de transacción ligado a PID/instancia y raíz verificados. Antes de restaurar, una caducidad puede liberar la barrera; después de comenzar el reemplazo, una desconexión exige recuperación o salida controlada. El manager conservaría backups y diario existentes, sustituyendo la precondición “proceso cerrado” por una concesión válida emitida por el port. No basta implementar un guard que siempre diga “cerrado”.

El primer entregable experimental sería telemetría y petición de guardado sin restauración. El segundo, el recorrido al título con trazas. Solo tras demostrar la lectura de A se habilitaría restauración en el mismo proceso. No se promete un estado instantáneo de RAM ni restauración dentro del mundo sin pantalla de carga.
