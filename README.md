# GambleChoice for M-USE

Add the risk-choice task to an **existing** M-USE Unity project. You do not need a second M-USE installation or matching Git history.

GambleChoice supports 2–6 choices, up to six outcomes per choice, integer token changes from −6 to +6, six probability representations, token or numeric amounts, and a virtual token bank. Legacy gain-only protocols remain supported.

## Install on Windows

1. Download and extract the release ZIP. Keep its files together.
2. Close your M-USE project in Unity. Run **GambleChoiceManager.exe**.
3. Browse to your existing Unity project (the folder containing `Assets`, `Packages`, and `ProjectSettings`) and the Unity **2020.3.40f1** editor executable.
4. Click **Preview**, then **Connect / Update**. The manager installs task assets, adds the registry entry and build scene, launches Unity in the background, and runs software checks. A failed check rolls back installation changes.
5. Open your project in Unity. Select the generated `GambleChoiceConfigs` folder as M-USE's Config Folder and a separate validation Data Folder. Run your normal M-USE task-selection workflow.

Use **USE → Gamble Choice → Setup wizard** to edit choices, probabilities, signed amounts, representations and timing, and export a new configuration folder. Exports never replace an existing folder. The generated Mixed profile is a demonstration, not a qualified participant protocol.

The manager EXE needs no Python installation. Unity and the existing M-USE project are required. The executable is unsigned; source and reproducible build instructions are included.

## Update or disconnect

Run a newer manager release and choose the same project. Preview and **Connect / Update** validate the existing tracked files before updating. Local edits stop the update instead of being overwritten.

**Disconnect** removes only files installed by this manager and its exact registry/build-list additions. It checks for external scene, prefab, asset and code references first. Exported configurations, recordings and unrelated changes remain. If installed files were edited, review them first; there is no destructive force option. Existing manual installations are never silently adopted.

The local transaction journal and backups are under `UserSettings/GambleChoiceModule`. **Recover** restores an interrupted transaction when its files have not subsequently changed. An `operation.lock` after a crash must only be removed after confirming no manager/Unity operation remains active. Keep `UserSettings` out of your project's commits.

## Compatibility

Supported release target: Windows, Unity **2020.3.40f1**, M-USE's standard task/trial/session templates, InputBroker, Newtonsoft JSON and uGUI. See [compatibility and evidence](docs/COMPATIBILITY.md) for exact tested commits and limitations.

Repository owner, name and Git history do not matter. Framework API changes do. Capability checks catch known mismatches, followed by actual Unity compilation and model/scene/export checks on the destination. Unknown forks are candidates until those checks pass. This does not certify all stock tasks or hardware.

Installation modifies only the task payload and two shared integration files:

- `Assets/_USE_Tasks/USE_Tasks_CustomTypes.cs`: owned using/registration sections.
- `ProjectSettings/EditorBuildSettings.asset`: GambleChoice task scene entry.

It does not replace the M-USE framework or update Unity/package versions. M-USE must already be functional on the target machine. Framework bugs outside the module remain the host project's responsibility.

## Source and command line

Clone this repository anywhere outside your Unity project's `Assets` folder, or download its source ZIP. Python **3.9+** runs the manager using only the standard library; the desktop interface additionally uses Tkinter.

```powershell
python manager.py
python manager.py preview "D:/MyLab/M-USE/USE_CORE"
python manager.py connect "D:/MyLab/M-USE/USE_CORE" --unity "C:/Program Files/Unity/Hub/Editor/2020.3.40f1/Editor/Unity.exe"
python manager.py validate "D:/MyLab/M-USE/USE_CORE" --unity "C:/Program Files/Unity/Hub/Editor/2020.3.40f1/Editor/Unity.exe"
python manager.py disconnect "D:/MyLab/M-USE/USE_CORE"
```

Do not drag `payload` directly into Unity: that bypasses registration, ownership tracking, conflict checks and rollback.

## Data, debugging and development

- [Configuration reference](docs/CONFIGURATION.md): lotteries, funding, displays and data semantics.
- [Debugging and architecture](docs/DEBUGGING.md): component map, output locations and failure recovery.
- [Apparatus qualification](docs/APPARATUS_QUALIFICATION.md): separate native input and hardware checks.
- [Build and release](docs/RELEASING.md): tests, Windows EXE and ZIP generation.

Analyze a recorded `events.jsonl` with `python analysis/analyze_gamble_choice.py path/to/events.jsonl`. Raw journals remain authoritative. `fixtures` contains labelled **model-only** test data; keep it out of participant datasets.

Software checks do not establish native touchscreen behavior, measured reward delivery, display scanout, or animal comprehension. Schema 2 uses virtual tokens only.

## License and attribution

MIT; see [LICENSE](LICENSE). Built for the [Multitask Unified Suite for Experiments](https://github.com/Multitask-Unified-Suite-for-Expts/M-USE). This is an independently maintained task module, not an official M-USE release. M-USE framework code is not bundled.
