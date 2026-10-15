# Verificación del port

Inspección realizada el 12 de octubre de 2026 mediante clon local del repositorio público. Commit fijado: `c67ce7808f1b5be9013348f0e5119d7615c0babc`. Las conclusiones se aplican a este commit, no a todas las versiones del port. El clon de referencia está fuera del proyecto del manager y no se necesita para compilarlo.

## Evidencia primaria

- [runtime_savedata.c](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/src/runtime_savedata.c#L1): implementa `libSceSaveData` sobre directorios del host. `root()` y `target_paths()` construyen `<user>/<savedata o savedata_party>/<user id>/<title id>/<dir name>`. Los metadatos viven en `<dir name>.sce_sys` y `<title id>.memory/memory.dat`.
- [runtime_savepolicy.c](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/src/runtime_savepolicy.c#L23): `runtime_savepolicy_dir()` selecciona `savedata_party` para party y `savedata` para solo. Define `0x140000` bytes para userdata/backup 0000–0009 y `0x40000` para 0010. Su comprobación de saves también detecta sistema todo a cero y otras advertencias que el manager no replica por completo.
- [runtime_file.c](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/src/runtime_file.c#L372): `open_for_commit()` usa una copia `*.bbtmp`; `commit_file()` sincroniza y renombra al cerrar. Esto protege un archivo, no hace una transacción única de todo el árbol de saves. No proporciona al manager una API externa de bloqueo.
- [probe.c](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/src/probe.c#L1451): interpreta `--user`; si no se especifica utiliza `user` relativo al contexto de ejecución.
- [run.bat](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/run.bat#L231): el proceso habitual es `out\bbport.exe`, sustituible por `BB_PROBE`; `BB_USER_DIR` prevalece sobre `<BB_DATA_DIR>\user`. Pasa el directorio con `--user`. El script cambia primero a su propia carpeta, puede cargar ajustes del launcher y puede relanzar party.
- [launcher/bbport_launcher.py](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/launcher/bbport_launcher.py#L250): traslada `user_dir` de sus ajustes a `BB_USER_DIR`. No basta deducir los saves del directorio del `.exe`.
- [build.bat](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/build.bat#L146): copia `bbport.exe` a `bb-probe.exe`.
- [Flujo party](https://github.com/visy/bloodborne_pc_windows_port/blob/c67ce7808f1b5be9013348f0e5119d7615c0babc/docs/party/ingame_flow.md#L82): documenta cachés de saves montados y lista de slots; cambiar archivos bajo el juego activo no garantiza recarga.

## Estructura y decisiones

El repositorio contiene el runtime nativo en `src/`, GPU en `gpu/`, launcher Python en `launcher/`, scripts, pruebas y documentación. No es un proyecto .NET. El manager se desarrolla aparte y utiliza exclusivamente archivos del host y enumeración estándar de procesos Windows. No se ha supuesto un servicio IPC, endpoint o función exportada de recarga.

Se elige respaldar la raíz del modo completa para incluir datos auxiliares y múltiples slots. La selección es explícita, con validación estructural; no se modifica `bbport.ini`, variables de entorno ni ajustes del launcher. La evidencia determina rutas relativas y mecanismo de escritura, pero no verifica dónde está instalada la copia del usuario ni qué parámetros la ejecutan.
