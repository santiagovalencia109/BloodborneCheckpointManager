# Administrar checkpoints (0.3.2)

En **Checkpoints**, selecciona una fila:

- **Restaurar seleccionado** recupera exactamente esa copia, aunque exista otra más reciente. También admite un respaldo de emergencia si activas **Mostrar respaldos de emergencia** y pasa las verificaciones. Con modo en vivo usa el mismo regreso al título y bloqueo de entrada/archivos ya probado; después entra normalmente a la partida. Con modo normal espera al cierre del juego.
- **Renombrar** cambia el nombre visible usando el campo situado debajo del listado.
- **Borrar** pide confirmación y elimina permanentemente la copia seleccionada de la biblioteca. No borra ni modifica la partida activa. Se pueden borrar copias con el juego abierto.

**F5** crea una nueva copia. **F9** restaura el último checkpoint manual, independientemente de la fila seleccionada o el buscador; en modo en vivo sigue siendo automático y no pide Sí/No. Para una copia concreta usa **Restaurar seleccionado**.

Antes de borrar se verifica el ID, que la copia corresponda al perfil y que el árbol no contenga enlaces. No se permite borrar mientras haya una restauración pendiente de recuperar. Se retira primero el checkpoint del listado con un renombrado dentro de la biblioteca, y luego se eliminan sus archivos: si un bloqueo o permiso impide completar la limpieza, el manager muestra y registra la carpeta residual `.deleted-*`. Las demás copias siguen disponibles. Ese caso no ofrece deshacer; borrar es permanente.

No se eliminan automáticamente los directorios `.original` ni los saves del port. Los perfiles, copias y ajustes existentes siguen siendo compatibles. Esta versión no necesita recompilar otra vez el puente 0.3.1 que ya funciona en la copia de prueba.

## Verificación

Build .NET Release sin advertencias/errores. 33 pruebas sin IPC aprobadas, incluidas cinco nuevas para eliminación aislada con juego abierto, perfil incorrecto, fallo de limpieza, bloqueo por diario pendiente y restauración explícita de una copia antigua. El intento de la suite completa se detuvo al detectar el juego real de prueba abierto: los siete casos de pipe no se repitieron en esta sesión. La ejecución sin IPC omite esos casos expresamente; no se contabilizan como aprobados. El puente y su restauración por F9 ya habían pasado la validación de la versión 0.3.1.

Las tres pestañas se renderizaron a 1120×820 y 980×740. Se revisó la biblioteca a tamaño mínimo para confirmar que Restaurar seleccionado y Borrar aparecen completos. No se borraron checkpoints reales del usuario durante las pruebas.

## Batch deletion (0.8.1)

Hold Ctrl or Shift to select several checkpoints, then click Delete (count) or press Delete. Ctrl+A selects only currently visible rows. A single confirmation lists the count and names. Only the selected library copies are removed; the live game save is unchanged. Rename/restore require one selected row. Refresh and language changes preserve selected rows that remain visible. If a batch cannot finish, the manager reports the completed count; locked cleanup files are logged rather than silently reported as physically deleted.
