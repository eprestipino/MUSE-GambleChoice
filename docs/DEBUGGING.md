# Architecture and debugging

The manager runs outside Unity so installation can modify the registry and task sources together, before Unity attempts compilation. It checks the selected project, Unity version, required framework API names, packages, filename conflicts and Unity GUID collisions. API name checks are an early filter; compilation is the actual signature check.

Every mutation is recorded before writing. Each tracked file includes its original and installed bytes. Updates preserve the original uninstall baseline. Pending transactions can be recovered; rollback refuses to overwrite subsequent edits. Operations are serialized by a lock, and open Unity projects are rejected. Partial writes use temporary files and replacement.

## Task flow

`Session → GambleChoice_TaskLevel → block → GambleChoice_TrialLevel → GambleRuntime → GambleEngine`

`Offer → AwaitReveal → Reveal → Transfer → Reward → ITI → Finished`

- `GambleConfig`, `GambleConfigJson`, `GambleProtocol`: strict configuration parsing/validation.
- `GamblePreparedProtocol`, `GambleSchedule`: resolved trial, position, palette and representation choices.
- `GambleOutcomeSampler`, `GambleRandom`: deterministic sampling and reproducible draw provenance.
- `GambleEngine`, `GambleLedger`: qualified holds, timing, single commitment and settlement.
- `GambleView`, `GamblePresentation`: probability/magnitude geometry and labels.
- `GambleRuntime`: InputBroker, Unity frames, journal, frame data, and reward adapter.
- Task/TrialLevel: M-USE lifecycle, normal framework data columns and summaries.
- `GambleCompiler`, `GambleChoiceWizard`: portable session export and interactive setup.

## Output locations

Integrated task journals live beneath M-USE's selected Data Folder. Normal trial data also includes `GambleRecordJson`. Standalone demo data uses Unity's `Application.persistentDataPath/GambleDemo`; it never assumes a repository parent directory. `--gamble-smoke` uses a separate explicitly supplied validation directory.

Manager validation logs and reports: `<Unity project>/UserSettings/GambleChoiceModule/`. `unity.log` contains compiler/check details. `checks.json` has a unique run ID, and `model/csharp-checks.json` contains model assertion evidence. `GambleChoiceConfigs` is the initial demonstration export. Setup wizard exports go wherever the operator selects.

## Diagnose by symptom

| Symptom | Inspect first |
|---|---|
| Preview refuses installation | Reported missing API/package, occupied filename, GUID collision or unsupported Unity version |
| Unity compilation fails | `unity.log`; manager should roll back its task/integration changes |
| Missing task in selection | Selected Config Folder and its TaskMappings; enabled scene and registry |
| Incorrect picture | Resolved configuration snapshot, representation precedence, GamblePresentation/View |
| Incorrect lottery result | Stored options, draw, sampler version and chosen outcome index |
| Incorrect bank | BalanceBefore/After, transaction ID, practice flag, ledger scope and funding policy |
| Missing/incomplete recording | Runtime recording error, storage permissions, partial journal; independently validate it |
| Disconnect stops | Local changes to installed files or external references; preserve/review edits before retrying |
| Interrupted manager | Check active operations, inspect installation journal, then Recover |

Do not retry ambiguous physical rewards. A software timestamp is not measured scanout; queued pulses are not measured delivery. Keep model fixtures, simulated holds, rendered validation and participant datasets separate.

## Working on the module

`payload` is the installable source of truth in this standalone repository. Preserve existing `.meta` GUIDs. Update `module.json` hashes after changes using `python tools/release.py manifest`, run manager and analysis tests, then validate installation into a separate supported M-USE checkout. Never test destructive lifecycle operations against the only copy of a modified lab project.
