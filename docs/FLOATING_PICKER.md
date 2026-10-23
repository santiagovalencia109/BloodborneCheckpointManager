# Floating checkpoint picker — 0.6

Press **F11** while the manager is open to show or hide the floating window. It stays above ordinary windows, works with the manager minimized, and uses the configured save root/library. Search by name, select a row with the mouse or arrows, then press Enter, double-click, or choose **Restore selected**. Escape hides the window. **Manager** shows the main window. Emergency backups are hidden until explicitly included. F9 continues to restore the latest manual checkpoint, independent of this selection.

The picker uses the same verification, emergency backup, journal and bridge lease as the main window. It captures the exact selected checkpoint before starting and disables further operations while busy. Successful restoration hides the picker; errors remain visible. For live restoration, wait for **Checkpoint restored** before Continue in the game. Offline restoration retains its confirmation and closed-game requirement.

Topmost WinForms windows work over windowed/borderless games. Exclusive fullscreen can obscure the picker; use the launcher's borderless/windowed option if needed. This is an external window, not a DirectX injected overlay. A global F11 conflict is reported in the manager; use its selector button instead or free that shortcut.

## Settings explained

- **Help & recovery**: recovery from an interrupted file replacement, not the ordinary checkpoint selection screen. Refresh/configure the library to inspect for a pending journal. Recovery stays disabled if no interruption is pending. Close the game before recovering; it returns the files to their pre-restore state and preserves displaced files.
- **Supermedo Live Mode**: create and restore checkpoints while the supported game is running. Open the manager before starting the game.
- **Closed Game Mode**: manage checkpoints with the game closed.

Advanced controls are hidden as of 0.8.2. Previously saved options remain in the profile.

For a fresh live session, open the manager before starting the game from your preferred launcher. A game started without its bridge is observation-only and must be restarted for live save/restore. If the bridge was already connected, reopening the matching manager during that game works with the same save configuration. Future versions may support safe first attachment after startup; version 0.6 does not promise this.

## Artwork

The supplied rune image is embedded unchanged as `Assets/checkpoint.png` and converted to a multi-size Windows application icon. The FEXTRALIFE watermark remains. This project does not assert a new license for that artwork; check the original rights before public redistribution. Additional language labels are external JSON resources documented in TRANSLATING.md.
