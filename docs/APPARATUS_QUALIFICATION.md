# GambleChoice apparatus qualification record

Status: **not performed for the expanded release**. Save a completed copy with an actual protocol identifier, operator, date, machine/device identifiers, executable SHA-256, config manifest and separate validation dataset. Reference that record in `QualificationReference` only after review. Do not use participant identifiers or data for qualification.

| Check | Procedure | Evidence to retain |
|---|---|---|
| Native hold threshold | Real mouse and intended touchscreen: shorter than, at, and longer than the configured hold; release and drag outside; return without a new press | Native input/frame traces and result journal; external recording if timing matters |
| Single-pointer ownership | Hold with one finger, add a second finger, release/cancel the first | Device video and event traces proving no ownership transfer |
| Layout | Every button in 2–6 choice layouts, intended resolutions/scaling, every representation and six-outcome legends | Device captures and hit records, including screen edges |
| Focus and interruption | Change focus, pause, calibration hotkeys, abort before and after settlement | Abort codes, exact M-USE/journal matches, bank arithmetic and no repeated settlement |
| Transitions | A stock task before and after GambleChoice; multiple blocks and task re-entry | Normal session logs, task/block counts and no changed stock-task behavior |
| Actual-time endurance | Run a representative long protocol with native inputs and pauses | Full journal, frame data, independently validated extraction and storage growth |
| Display timing | Photodiode/scanout measurement against the configured onset/reveal/transfer windows and event codes | Instrument traces, measured distribution, hardware/display settings |
| Photometry/comprehension | Measure outcome colors on the actual display; verify numeric/grid/signed semantics with the intended population | Calibration report and comprehension procedure/results |
| Physical reward (schema 1 only) | Existing calibrated SyncBox/valve procedure, real pulse/volume measurements including zero and maximum train | Instrument traces and measured volume; queued commands alone do not qualify delivery |
| Loss semantics | Review endowed bank, reset boundary, stop/debt policy, practice isolation and compensation semantics | Approved protocol and examples of zero, loss, gain, omission, stop and settled abort |

Schema 2 uses virtual tokens only. Any future physical cash-out, external symbol mapping, continuous reward amounts or more than six choices/outcomes requires an explicit further implementation and qualification pass. They are not enabled by setting a qualification reference.
