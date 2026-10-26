# Compatibility and validation

Release target: Windows and Unity **2020.3.40f1**.

| Host | Exact revision | Evidence |
|---|---|---|
| Official M-USE | `5429d45e44baa3d8d80e1fbe94e1712134ed663e` | Installed into an isolated clean checkout; Unity compilation, 9,444 legacy model/parser assertions and 306 module checks pass |
| Development fork | `02aa5b8b071f380b24255e1e6b1f57ba1222c325` plus existing task integration and startup correction | Unity compilation and the same model/module checks pass |

The official revision was confirmed against upstream HEAD on October 26, 2026. Other repository origins are accepted through capability checks and actual destination validation; they are not automatically certified by sharing a repository name or Unity version.

The module checks cover registry/build-scene integration, camera/canvas scene wiring, all six display constructions with 2/3/6 choices, signed −6…+6 outcomes and one-time virtual settlement across 2–6 choices, and a portable config/icon export. Canvas construction checks are not rendered screenshot validation or native input tests.

Manager unit tests use temporary framework test doubles to check rollback, interrupted transactions, conflicting files/GUIDs, modified-file protection, unrelated shared-file edits, update and disconnect behavior. Data reader tests cover timing/color provenance and malformed records. These test doubles are separate from the actual destination Unity checks above.

Release verification on October 26, 2026:

- **33 installer tests** and **9 data-export tests** passed.
- The independent reader validated **420 newly exported model trials / 1,260 options**, with no issues.
- The extracted Windows EXE previewed and updated the official installation, then passed the destination Unity checks.
- The EXE disconnected all **81 tracked installation changes**; original shared files were restored byte-for-byte, exported configuration remained, and tracked official files were clean.
- The official host compiled and exited successfully in Unity after removal.
- An actual failed destination-validation attempt rolled back to a clean tracked official checkout before the validator was corrected.

See [machine-readable release evidence](validation.json). The desktop interface is supplied; these executable lifecycle tests used its CLI, without taking control of the desktop.

The existing task previously passed broader rendering, data reconciliation and simulated-input audits in its development project. Those historical results do not establish that a newly imported module has passed a full participant-like session in an independent host.

No native touchscreen, apparatus, measured reward-delivery, scanout, animal-comprehension or comprehensive stock-task regression claim is made. Schema 2 delivers virtual tokens only. Use the apparatus qualification procedure for the intended rig.

Known host prerequisite: the development fork's optional eye-tracker startup transition must be registered after its states are attached. The working development checkout already contains that correction. This module does not install private eye-tracker framework changes or silently patch unrelated host code. A host that fails before task loading needs its own framework repair.
