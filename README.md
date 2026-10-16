# Bloodborne Checkpoint Manager

Create checkpoints of your save and return to them whenever you want to retry a fight or recover your saved progress and consumables.

**Currently supports only [Supermedo's port](https://github.com/Supermedo/bloodborne_pc), on Windows x64 in single-player mode.** Some port updates may require a manager update.

Version 0.8.4 is confirmed working by the user with **Supermedo 1.8.2**. Earlier 0.8.x versions were tested with 1.8.1.

![Floating checkpoint picker while playing Bloodborne](docs/images/in-game-picker.png)

## Getting started

Download the `-windows.zip` package from [Releases](https://github.com/santiagovalencia109/BloodborneCheckpointManager/releases). You need the .NET Desktop Runtime 8 x64. Extract the entire package and open `BloodborneCheckpointManager.exe`.

In **Settings**:

1. Select the game executable: `bb-probe.exe`, not the launcher.
2. Select the `savedata` folder used by your installation.
3. Choose a separate folder for your checkpoints.
4. Under **Checkpoint mode**, select **Supermedo Live Mode** and save your settings.

Use **Closed Game Mode** if you want to manage checkpoints with the game closed.

**Live Mode requires the manager to be open before you start the game.** Launch the game from your usual launcher, then wait for the manager to confirm the connection. If the game was already running, close it normally and start it again with the manager open.

![Game settings](docs/images/settings.png)

## Playing

| Key | Action |
| --- | --- |
| **F5** | Create a checkpoint. |
| **F9** | Restore the latest manual checkpoint. |
| **F11** | Open the picker to restore a specific checkpoint. |

Restoring returns the game to the title screen. **Wait for “Checkpoint restored” before selecting Continue.**

You can also create, restore, rename and delete checkpoints from the manager window. Hold **Ctrl** or **Shift** to select several checkpoints, then choose **Delete** to remove them together. **Ctrl+A** selects all checkpoints currently shown. If a shortcut is marked as occupied, close other manager windows or apps using that key. The manager will reclaim it when it becomes available.

![Checkpoint library](docs/images/checkpoints.png)

## Keep in mind

- Checkpoints restore what the game writes to its save files. Your position may not match the exact moment you pressed F5.
- Before restoring, the manager makes an emergency backup of your current save.
- If a restore is interrupted, close the game and follow the [recovery instructions](docs/FLOATING_PICKER.md#settings-explained).

Change the interface language under **Language**. To add a translation, see the [translation guide](docs/TRANSLATING.md).

See the documentation for [build instructions](docs/EXTERNAL_BRIDGE_USAGE.md#build) and [compatibility details](docs/ADAPTIVE_DETECTION.md).

## Source code

Open `BloodborneCheckpointManager.sln` in Visual Studio, or use the .NET SDK. The application is in `src/`, the native bridge in `native/`, and tests in `tests/`.

The `-windows.zip` package contains the ready-to-run app. The `-source.zip` package contains the project. Both are available in [Releases](https://github.com/santiagovalencia109/BloodborneCheckpointManager/releases). Local builds go in `artifacts/`, which is excluded from Git. See [project layout](docs/PROJECT_LAYOUT.md) for the folder structure.
