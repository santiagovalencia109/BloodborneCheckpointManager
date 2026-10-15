# Puente experimental: pruebas reales del 12 de octubre de 2026

## Versión 0.3.1: F9 automático y protección de entrada

La repetición manual posterior a los dos recorridos controlados volvió a fallar. La traza mostró salida al título seguida de carga del mundo antes de terminar la restauración. Se eliminó la confirmación Sí/No del modo en vivo y se añadió una barrera en `scePadReadState` que neutraliza teclado, mando, replay, entrada del director y archivo de prueba mientras la operación está pendiente. Un botón mantenido permanece neutral hasta soltarse, incluso después de liberar el bloqueo. Una interrupción tras comenzar la sustitución conserva bloqueados entrada y archivos.

El regreso verificado del motor llega primero a la pantalla de título; su menú de comandos aún no existe sin otra entrada. Ahora se reconoce esa llegada tras emitir la petición verificada de salida, observar descarga del mundo y fin de carga. Se mantienen ausencia de descriptores, 750 ms estables y la barrera de I/O; no se acepta mundo ausente por sí solo. La entrada bloqueada impide empezar otra partida durante esa espera.

Prueba real de esta revisión: PID 15284, 0 viales antes de restaurar, checkpoint `15d5e6889f9c4959972ed7f77e691f54`. Se mantuvo Cross desde un segundo después de iniciar la adquisición. La restauración terminó sin entradas manuales al título, conservó original `.bcm-dae14018b84c416690dfd9ab1cf09f12.original`, y una consulta posterior confirmó título=true, mundo=false a pesar del botón mantenido. Tras soltarlo y entrar normalmente, el mismo proceso informó **2 viales**. No se midió un intervalo exacto de duración ni se probó recuperación de balas (eran 0).

Build .NET sin errores/advertencias; **35 pruebas del manager aprobadas**, incluidas rechazo de port sin barrera de entrada y de concesión sin controles bloqueados. Pruebas nativas de barrera/entrada mantenida y de título aprobadas.

**Validación interactiva final:** se abrió el manager 0.3.1 con el perfil aislado. Su registro confirmó restauraciones completadas a las 14:24:43 y 14:25:14 (America/Bogota), y un checkpoint creado a las 14:25:02. Ante la prueba de gastar un vial, F9 automático y recuperar los 2 viales sin pregunta Sí/No, el usuario confirmó que ahora funciona perfectamente. Esto valida el recorrido de F5/F9 en esta copia y revisión; no equivale a probar todos los estados del motor o recuperación de balas/almacenamiento.

SHA-256 del ejecutable nativo probado: `90C08F4C96D61755BC9F04B525FA70597CA03CD4FCD2FE0CD363698689CDFA87`.

## Corrección tras la prueba de F9 desde la interfaz

El usuario confirmó creación con F5, pero informó que F9 no recuperaba los viales. El registro mostró dos rechazos por timeout: **no hubo sustitución de saves**. La prueba positiva anterior había comenzado con el título ya abierto; no validaba todo el recorrido desde la partida.

Se sustituyó la ventana de 400 ms por evidencia de entrada al título ligada al ciclo de carga. Una consulta nueva de sus textos puede llegar mientras aún hay mundo/carga; se conserva hasta terminar la descarga. Entrar a un mundo o iniciar otra carga invalida textos antiguos. `test_checkpoint_title.cpp` cubre descarga lenta, consulta única, texto obsoleto y carga que invalida el título; pasó durante la compilación. Se conservan las condiciones de mundo ausente, carga finalizada, 750 ms estables y barrera de archivos.

Dos recorridos controlados reales usaron el proceso PID 37948 y el checkpoint `15d5e6889f9c4959972ed7f77e691f54`:

| Recorrido | Al guardar | Antes de restaurar desde el mundo | Tras título, restauración y Continue |
|---|---:|---:|---:|
| 1 | 2 viales | 1 vial | 2 viales |
| 2, mismo checkpoint/proceso | 2 viales | 1 vial | 2 viales |

La diferencia se produjo con la entrada de prueba `triangle` de `BB_PAD_FILE` y se midió con `coop::GoodsCount(1000)` en el hilo principal; el helper verifica bytes de la función de inventario. Ambos reemplazos terminaron con backup y original retenido. Las balas eran 0; **no se comprobó recuperación de balas en estos dos recorridos**. Se utilizó el cliente de diagnóstico con el mismo Core de la interfaz. Queda repetir las teclas de la interfaz con esta revisión del port.

El manager sigue siendo una aplicación independiente. El modo en vivo requiere un módulo opcional compilado dentro del port; el paquete Supermedo instalado no expone este protocolo. La casilla de modo en vivo comienza desactivada. No permite restaurar por el mero hecho de marcarla.

## Instalación verificada

La instalación indicada por el usuario está en `E:\Games\Bloodborne-Windows\bbport-windows`. `Bloodborne.exe` es el launcher; el ejecutable nativo original es `bin\bb-probe.exe`. La raíz real de saves es `user\savedata`, con `1\CUSA00207\SPRJ0005`. El contenido del juego se encontró en `D:\Games\Bloodborne\CUSA03173`. Estos datos pertenecen a esta instalación, no son valores universales.

Se compiló una copia de visy, commit `c67ce7808f1b5be9013348f0e5119d7615c0babc`, en `C:\bcm-build\port`. Se preparó una imagen compatible mediante su propio `boot_image.py`: la imagen preprocesada del paquete Supermedo usa otro tratamiento de TLS y no es intercambiable. No se modificó la instalación original ni sus saves.

## Resultado comprobado

- Una partida real abrió con una copia de un checkpoint existente, en `artifacts/live-fixture-01/user/savedata`.
- La petición de captura llamó a SaveRequest en el hilo principal, esperó escrituras y obtuvo una barrera. Creó el checkpoint `9487cd3a03224dcf87ba0f8942e3fde5`. La telemetría previa indicaba nivel 10 y 991 ecos.
- La petición de restauración hizo regresar al título; el usuario confirmó que lo vio y pulsó Continuar. El puente no concedió la barrera y no sustituyó saves.
- Un segundo intento tampoco concedió la barrera; todavía no se había reemplazado ningún archivo.
- El tercer intento mostró mundo ausente, cero descriptores de saves y textos del título consultados una sola vez. La señal se conserva ahora durante ese intervalo sin mundo/carga; se borra al cargar o entrar en partida.
- Tras recompilar, la restauración del checkpoint `9487cd3a03224dcf87ba0f8942e3fde5` terminó en el título dentro del proceso PID 21380, con backup de emergencia `a7708566499d4bfa9c4286deac137b48` y original retenido. El usuario pulsó Continuar y confirmó que se recuperaron ecos y viales. No se registró un recuento exacto de consumibles.

El primer recorrido se probó mediante el cliente de diagnóstico que utiliza el mismo Core que la interfaz. Después se publicó y abrió el manager 0.3 con el perfil aislado. Su registro confirmó otra captura a las 13:41:56, checkpoint `baa707def83847ad8202990836db5c86`. El recorrido completo de atajos aún espera confirmación del usuario. Las 33 pruebas automatizadas del manager pasaron; las pruebas de pipe simulan el motor y no prueban la recarga de Bloodborne. La prueba nativa de exclusión pasó y la interfaz se renderizó a dos tamaños tras añadir el modo en vivo; el ensayo de arranque/cierre sintético se omitió en esta ejecución porque el juego de prueba estaba abierto.

## Protocolo y recuperación

`BB_CHECKPOINT=1` habilita el módulo; `BB_CHECKPOINT_EXPERIMENTAL_RESTORE=1` permite investigar restauración. Solo, con party desactivado. Pipe local `bcm-checkpoint-v1-<PID>`, ACL del usuario y SYSTEM, rechazo de clientes remotos. El cliente valida PID real del servidor, versión, sesión y ruta de saves.

Captura: SaveRequest, generación modificada, petición de guardado finalizada, ausencia de escritores y 750 ms sin cambios antes de bloquear acceso a archivos. Restauración: título detectado, sin descriptores de saves, 750 ms estables y barrera exclusiva. La barrera incluye operaciones en curso y commits posteriores al cierre del descriptor, así como rutas auxiliares de savedata.

La concesión de captura caduca o se libera al desconectarse. Una restauración que ya empezó a modificar archivos deja el acceso congelado si el manager falla: cerrar el juego y recuperar el diario con el manager. No pulsar Continuar durante la restauración; esperar la confirmación de finalización.

No hay restauración instantánea de RAM. Los dos recorridos con recuentos registrados pasaron con el puente corregido. Faltan verificación específica de balas/almacenamiento, persistencia tras reiniciar, fallos en el port real y repetición de F9 desde la interfaz con esta revisión. Por eso la versión 0.3 sigue siendo experimental.
