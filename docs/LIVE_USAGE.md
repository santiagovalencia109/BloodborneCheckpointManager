# Usar F5/F9 en vivo (0.3.2 experimental)

Requiere el port compilado con `native/visy`, no basta actualizar el manager. El paquete habitual Supermedo no expone este puente. El modo normal con juego cerrado sigue disponible.

## En la copia de prueba de este equipo

1. Abre `artifacts/windows-v0.3.2-experimental/BloodborneCheckpointManager.exe --data-directory <ruta absoluta a artifacts/live-fixture-01>`. Ese perfil ya selecciona `C:\bcm-build\port\out\bbport.exe`, los saves aislados y su biblioteca.
2. Cierra cualquier manager anterior para liberar los atajos globales. Conserva abierta la copia aislada del juego. No abras el juego original a la vez.
3. Antes del combate, pulsa **F5**: espera a que el estado confirme checkpoint creado. Solicita guardado al motor y captura todo el árbol, incluido inventario y almacenamiento persistidos.
4. Gasta algún vial/bala o avanza. Pulsa **F9**: no pide confirmación en modo en vivo. Vuelve al título y restaura automáticamente el último checkpoint manual, aunque haya otro seleccionado. Los controles del juego quedan neutralizados durante la operación; cualquier botón mantenido debe soltarse antes de volver a aceptarse. Espera el estado **Checkpoint restaurado**.
5. Pulsa **Continuar**. Comprueba posición, ecos, viales y balas. El juego decide posición y reposición desde el almacenamiento; no es un estado de RAM.

La copia se arranca con `tools/launch-live-fixture.ps1 -FixtureRoot <ruta absoluta a la copia>`. Las rutas predeterminadas de ese script son las verificadas en este equipo; en otro deben configurarse. Usa una imagen preparada por la misma revisión de visy. El script requiere el entorno de compilación `C:\bcm-build` y los archivos del juego del usuario; no es un instalador universal.

## Compilar el módulo

El manager se compila por separado con el SDK .NET. Para el port, crea un checkout limpio del commit visy `c67ce7808f1b5be9013348f0e5119d7615c0babc` con sus submódulos y aplica `tools/apply-visy-addon.py <checkout>`. No ejecutes ese patcher dos veces sobre el mismo árbol. `tools/prepare-clang-compat.py` y `tools/build-native-port.sh` documentan las adaptaciones y dependencias usadas con MSYS2 Clang64 en este equipo; el último fija las rutas locales de compilación. El código añadido conserva su licencia GPL.

Arranca en solo con `BB_CHECKPOINT=1`, `BB_CHECKPOINT_EXPERIMENTAL_RESTORE=1`, `BB_PARTY=0`, `--user` a una copia exclusiva y la misma ruta de savedata en el manager. No intercambies imágenes preprocesadas entre forks sin comprobar su compatibilidad. El manager valida el PID del servidor y la raíz que reporta antes de copiar.

## Si falla

Un rechazo antes de conceder barrera deja los archivos intactos. Si se interrumpe una restauración iniciada, el port mantiene congelado el acceso: cierra la copia del juego y usa Recuperación con el mismo perfil. Conserva backups y directorios `.original`. No abras otra partida antes de recuperar.

Una única prueba positiva no garantiza todos los menús o condiciones. Ver [evidencia y pruebas pendientes](LIVE_EXPERIMENT.md). La recarga directa dentro del mundo y party siguen fuera de este modo.
