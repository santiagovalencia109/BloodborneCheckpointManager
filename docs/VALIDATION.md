# Validación

## Versión 0.5 — puente externo Supermedo

49 pruebas del núcleo aprobadas, incluidas 9 pruebas del cliente sobre pipes reales. Las nuevas verificaciones cubren preferencias antiguas sin inyección, identidad por contenido tras renombrar y rechazo de captura/restauración en conexiones solo de observación. Build .NET Release sin advertencias ni errores; prueba nativa de ABI aprobada. Interfaz inglés/español: 12 vistas a dos tamaños, conservación de selección/búsqueda/nombre y ciclo Windows sintético aprobados.

El ejecutable original de Supermedo fue detectado y conectado sin reemplazarlo. En saves aislados, F5 capturó 2 viales; tras gastar uno, F9 volvió al título, restauró el checkpoint y Continue recuperó 2. También se verificó que mantener Cross durante la restauración no activaba Continue antes de soltarlo. El usuario confirmó que las teclas del manager 0.5 recuperan el vial sin pregunta Sí/No. La conexión automática del manager quedó registrada. Evidencia y límites: [EXTERNAL_BRIDGE_EXPERIMENT.md](EXTERNAL_BRIDGE_EXPERIMENT.md).

## Versión 0.2 — interfaz y reinicio controlado

Build Release correcto, sin advertencias ni errores. 28 pruebas del núcleo aprobadas: las 18 originales y 10 adicionales para orden preflight/cierre/restauración/arranque, configuración de arranque inválida, checkpoint corrupto antes de cierre, timeout, fallo de restauración sin arranque, fallo de arranque conservando resultado, cancelación antes/después de restaurar, compatibilidad de ajustes antiguos y rechazo de argumentos inline de scripts.

La prueba Windows adicional inicia un ejecutable sintético propio, detecta su ruta exacta, le pide cerrar la ventana y comprueba su salida y el intervalo sin procesos. Aprobada. Permitió detectar y corregir que se retiraba dos veces la extensión de nombres con puntos (por ejemplo `Checkpoint.UiTests.exe`), lo cual podía dejar sin detectar algunos ejecutables personalizados.

Se renderizaron las tres pestañas a 1120×820 y 980×740, con datos de demostración y ajustes aislados, sin registrar atajos. Se revisaron visualmente las pantallas; Ajustes utiliza desplazamiento vertical. Reproducir con:

```powershell
dotnet run --project tests/Checkpoint.UiTests -c Release -- artifacts/preview
```

Esta prueba crea ventanas de prueba transparentes; no abre/cierra Bloodborne ni usa los ajustes del usuario. Si detecta otro juego/proceso coincidente, omite el test de ciclo de vida. El test debe iniciarse mediante `dotnet run` o `dotnet Checkpoint.UiTests.dll` para que el proceso padre sea distinto del apphost hijo.

El usuario confirmó la restauración de saves de la primera entrega. No se ha ejecutado aquí el nuevo ciclo contra Bloodborne ni se han comprobado F5/F9 en una sesión real. Detectar el proceso lanzado no verifica que el juego llegue al título o cargue al personaje. La recarga en caliente sigue siendo una investigación estática; no se aplicó un parche al port.

## Primera entrega (histórico)

Fecha: 12 de octubre de 2026. Windows 10 x64, SDK .NET 10.0.101 compilando destinos `net8.0` y `net8.0-windows`; runtime .NET 8 instalado. Build Release sin advertencias ni errores. Publicación dependiente del runtime, sin paquetes de terceros.

18 pruebas automatizadas de integración aprobadas, sobre árboles sintéticos en directorios temporales:

1. Ida y vuelta de checkpoint, renombrado, eliminación de archivos extra del destino mediante reemplazo del árbol y conservación de emergencia/original.
2. Juego abierto bloquea creación y restauración antes de mutar saves.
3. Archivos añadidos a un checkpoint invalidan su integridad.
4. Rutas con traversal en manifiesto rechazadas.
5. Origen y biblioteca solapados rechazados.
6. No se restaura ni lista un checkpoint de otra raíz.
7. Escritor con archivo bloqueado impide captura.
8. Saves truncados y temporales `.bbtmp` rechazados.
9. Fallo después de registrar intención, antes de mover carpetas: recuperación del estado previo.
10. Fallo después de mover original: recuperación del estado previo.
11. Fallo después de instalar checkpoint: recuperación del estado previo.
12. Detector real de procesos rechaza el ejecutable del proceso de pruebas en ejecución.
13. Arranque detectado entre renombrados interrumpe y permite recuperar al cerrar.
14. Modificación de bytes sin cambiar tamaño rechazada por SHA-256.
15. Bloqueo del destino funciona incluso usando otra biblioteca.
16. Archivo de sistema completamente a cero rechazado.
17. Captura fallida no aparece en el listado como checkpoint válido.
18. Diario con rutas de recuperación ajenas rechazado.

No se ejecutó Bloodborne, no se accedió a saves reales y no se hicieron pruebas interactivas de WinForms ni de registro global de F5/F9. Las simulaciones de interrupción son excepciones en puntos controlados; no equivalen a pruebas físicas de corte de energía. Tampoco se midieron rendimiento, unidades no NTFS, escenarios cloud, discos llenos o interferencia de antivirus.

Antes de confiar progreso importante a la aplicación, realizar el recorrido manual del README con una copia desechable de un save, comprobar el resultado en el juego y conservar una copia externa. Las limitaciones del detector de procesos y la ausencia de recarga en caliente están documentadas explícitamente.
# Desarrollo experimental

33 pruebas del manager aprobadas, incluida negociación por pipe real con motor simulado, barrera, origen incorrecto y desconexión durante restauración. Build Release sin advertencias ni errores. El resultado de las pruebas reales del port se registra en [LIVE_EXPERIMENT.md](LIVE_EXPERIMENT.md); las secciones siguientes corresponden a entregas anteriores.


## 0.6 UI and floating picker — 2026-10-12

49 core checks passed, including isolated pipe negotiations and restore failure paths. Release builds completed without warnings or errors. UI checks cover exact selected checkpoint dispatch (not newest fallback), failure remaining visible, empty/busy/repeated-input guards, successful close, minimized manager, and English/Spanish state preservation. Sixteen rendered previews were inspected at regular/minimum sizes. A synthetic Windows process test covered launch, exact-path detection, close request and quiet period.

The native bridge is unchanged from the user-accepted 0.5 build (SHA-256 D77DAF57EC163B98D27EA8B739536F88DF6B5CED4D93F6FF2E445C50C8A86AF4). Prior F5/F9 vial recovery was confirmed by the user on original Supermedo. Automated picker tests substitute the restoration callback; they do not establish real gameplay recovery through F11 or visibility over exclusive fullscreen. Those require the manual isolated-game check. Historical limitations above refer to their original release dates.

The 0.6 manager connected successfully to isolated Supermedo PID 15820. Its log records restoration of "Live probe 16-45-12" at 18:34:07 local time with a preserved original directory. This confirms the running restore completed; visual F11 behavior and loaded gameplay state still await user feedback.

## Manual F11 acceptance and fresh game-first startup

The user confirmed the floating picker is visible over the game and restores the selected checkpoint (0.6 isolated fixture).

A separate fresh game-first test closed only the identified isolated manager, confirmed no game or probe watcher was running, started the original supported native executable using the existing isolated save root, waited for the guest to mount SPRJ0005, and only then started manager 0.6. The order is recorded in the local fixture's game-first-order.json: game PID 19956, guest ready 2026-10-12 21:18:20.036 -05:00, manager PID 40460 opened 21:18:20.099 -05:00.

The manager detected the running game and logged observation-only mode. A real pipe greeting independently reported guestVerified=true, stage=observer, capture=false, restore=false, inputBarrier=false and the reason: game started before the bridge. No checkpoint capture or restore was requested for this test. Detection after startup works; live capture/restore on a fresh session started before the manager is not supported by the current adapter. This is not a test of reopening a manager on a process whose bridge was connected earlier.

## 0.7.1 adaptive detection acceptance

The installed updated Supermedo runtime (000C7C9BD5FEF00C1D28D5DFB9FBCE2A4A18756653A363D84AC31E4D9EBAE15A) passed the embedded runtime contract without a whole-file hash allowlist. The 0.7 manager automatically connected before guest startup and enabled live capabilities. The user confirmed checkpoint creation/restoration through the UI. Logs record one capture and two restores in the isolated fixture. After a manager-only upgrade to 0.7.1, the user confirmed F5/F11 shortcuts worked, with the game still running and the matching existing DLL reused.

49 existing core checks plus five contract checks have passed. Synthetic relocation accepts changed PE layout/identity and rejects modified critical code/references, missing symbols, export mismatches, malformed/overlapping/truncated PE and changed I/O surfaces. Actual-file checks accept unrelated file changes and reject critical opcode mutation. The native tick ABI check passed. Real Windows competing shortcut registration/retry tests passed. Sixteen latest UI previews preserve English/Spanish selection/search/name and picker behavior; the process lifecycle fixture was skipped for this run because the isolated game was open (it was already verified on prior releases).

Current bridge SHA-256: 6DEEDC1F4D0C0E142EF8FC0A23E672F827DA3A41F6C5116FFC6F33FD7D2F5742. The package has 223 matching English/Spanish translation keys. Updated source/runtime packages include the native layout header and embedded contract. No original saves were restoration targets, and no original game executable was replaced.

## 0.8.1 acceptance — 2026-10-15

The user confirmed live checkpoints work again on the updated Supermedo runtime in the isolated fixture, then confirmed the new multi-selection deletion removes only selected checkpoints. See UPDATE_0_8.md for native layout changes, source review and regression evidence. The complete pre-game core run passed 57 checks; the later storage run passed 54 checks including six new batch checks, with nine previously passing simulated pipe checks explicitly skipped while the game was open. Native ABI check and Release builds passed. No original saves were restored or deleted by these tests.
