# GambleChoice configuration reference

Use the installed Unity setup wizard to export a new session folder.

## Outcomes and settlement

Schema 2 requires exactly one of `Options`, `Schedule` or `TrialTable`. An option has an ID and an `Outcomes` array. An outcome contains `Id`, integer `TokenDelta` from **−6 through +6**, finite `Probability` from zero through one, and an optional `#RRGGBB` color. Probabilities must sum to one within 1e−9; they are not normalized. Legacy `Low`, `High`, `PHigh` values must be zero/omitted. Unknown config and outcome fields are rejected, including through the M-USE importer.

One deterministic draw uses `xorshift32-v1`. Schema 2 samples the ordered categorical CDF with half-open intervals: a draw exactly at a boundary enters the next positive-probability interval. Zero-probability outcomes cannot be selected. Schema 1 continues to use its original `u < PHigh` rule.

`Ledger.Settlement` is `VirtualEndowment`. There is **no physical cash-out** in schema 2. Physical reward is rejected; requested pulses remain zero and observed pulses remain null. The default initial bank is 24 tokens and resets each block. `Scope="Task"` retains the bank across blocks within a task instance. Task re-entry starts a new bank. Practice uses a separate bank under the same reset policy.

`InsufficientFunds="Stop"` requires enough funds for every positive-probability outcome of every offered choice. If a loss cannot be funded, the engine finalizes a `Stopped / InsufficientFunds` record before displaying the offer, and M-USE ends that block. It never clips the advertised amount. `AllowDebt` explicitly permits negative balances. Changing the ledger settings resets its bank; do not use policy changes as an implicit replenishment procedure.

Settlement occurs once at the end of the transfer window, including zero outcomes. The transaction ID equals the trial ID. An abort before settlement leaves the bank unchanged; an abort after settlement retains the debit/credit. No reward is retried. An omission has no draw, outcome or transaction. Practice is excluded from adaptive updates.

Signed amounts are displayed beside the matching outcome colors. Gains move filled tokens toward the bank and leave empty source slots in place. Losses move tokens out of the bank. The number is the complete bank balance; the bank icons show at most six tokens. All signs share the configured feedback windows. Schema 2 uses a fixed layout in the same uniform 1920×1080 reference space; legacy `Geometry` settings customize schema 1 only.

## Displays and design

Probability defaults: `Bar`, `Pie`, `HorizontalBar`, `Numeric`, `Grid`, `Donut`. Each choice may override `ProbabilityRepresentation`. Amounts independently select `TokenSlots` or `Numeric` through `AmountRenderer` or the per-choice `RewardRepresentation`. Numeric probability labels use up to 17 significant digits, including scientific notation where needed. Grid requires exact representability at the declared `GridCells` denominator (1–100); it rejects inexact probabilities rather than rounding. Palette assignment rotates the role-color mapping; explicit outcome colors take precedence. `SwapRoles` is reserved for schema 1 and rejected in schema 2.

`TrialTable` rows have `ConditionId`, `Practice`, optional `Renderer`, and `Options`. Rows repeat when trials exceed table length. `Design.Order="Randomized"` independently shuffles condition rows within each complete cycle; the seed reproduces each cycle. `Sequential` preserves their order.

`PositionPolicy="Balanced"` starts with a seeded permutation for each condition, then rotates it across repetitions. Each choice visits every position once per complete choice-count cycle. `PalettePolicy="Balanced"` alternates palette per identity. Incomplete cycles can have unequal exposure; inspect the exported counts. Neither feature establishes balance across different participants or seeds. `Fixed` preserves choice order; `Independent` uses the existing `ShufflePositions` policy.

`Design.PracticeTrials` inserts an initial practice segment using the first source condition; these trials count toward the configured block total. The nonpractice schedule starts from its first row after that segment. Explicit table rows may also be marked Practice. Plan indexing restarts per block (`PlanTrialIndex`); draw replay uses task-wide `TrialIndex`. Legacy independent position/palette streams use the task-wide index as well. The wizard exports a single block. For multiple manually authored blocks, review each block's resolved configuration and logs.

`RepresentationCycle` selects a trial-wide default display, changing after a complete position-rotation cycle. Explicit per-choice and per-row overrides take precedence. CSV import freezes the resolved condition, practice, position, palette and per-choice display assignments and disables further shuffle/balance/cycle transformations. CSV is strict: contiguous zero-based trial indices, contiguous choice rows, unique IDs and consistent metadata. The header is:

```text
Trial,Condition,Practice,ChoiceId,OutcomeId,TokenDelta,Probability,ProbabilityRepresentation,RewardRepresentation,Palette,Color
```

The optional `CertaintyEquivalent` preset runs integer bisection over a named certain choice (`sure`) and lottery (`risky`). The sure choice must have exactly one outcome of probability one; its outcome ID and optional color are preserved. Selecting sure lowers the upper bound to the current offer; selecting risky raises the lower bound. The next offer is the integer midpoint, with division truncated toward zero. Bounds and update counts are recorded before/after each trial. Omissions, aborts and practice do not update it. Bounds reset per block. At one-token resolution repeated offers can persist; this is an exploratory staircase, not a fitted or statistically validated preference estimate. Adaptive CSV exports derive the initial offer from the configured bounds, not a future response-dependent trajectory. Runtime `Options` and snapshots are authoritative.

## Data and checks

```powershell
python analysis/analyze_gamble_choice.py "path/to/events.jsonl" --output "path/to/analysis"
python -m unittest discover -s analysis -p "test_*.py" -v
```

The reader supports mixed schema journals and exports `trials.csv`, `options.csv`, `outcomes.csv`, `events.csv`, `ledger.csv`, and `validation.json`. Schema 2 adds condition/practice, block/plan index, categorical outcome identity, config hash, balances, transaction ID, and adaptive snapshots to the raw record. The reader independently replays draws, verifies accounting/continuity, timing windows, events and adaptive updates, and rejects corrupted, duplicate, truncated and incomplete records. Summary totals explicitly separate practice and nonpractice tokens. Do not pool model fixtures, simulated pointer data, demos, practice or unqualified sessions with participant data.

The October 26 reader also exports schema-2 `contact_rt_ms`, `commit_rt_ms`, `reveal_late_ms`, `iti_ms` and `palette_mode`, matching the legacy response columns. Outcome rows include effective `probability_representation`, `reward_representation`, `color` and `configured_color`. `color` resolves the palette fallback actually used by the renderer; an empty `configured_color` means no explicit override. Legacy option rows include resolved `low_color` and `high_color`. The raw journal remains authoritative. Display/color corruption and an event containing the reserved CSV field `trial_id` fail validation rather than silently changing the display provenance or crashing export.

Numeric probability labels use a smaller font for dense outcome lists to avoid uGUI clipping an entire percentage row. Software visibility checks do not establish legibility or probability comprehension on the target apparatus.

Installed-module software checks: `GambleChoiceModuleChecks.Run` (registry, scene wiring, legacy model/parser regressions, signed settlement, six display constructions and portable configuration export). Reports are under the project’s `UserSettings/GambleChoiceModule` folder. These are software checks, not a native-input or apparatus qualification.

Real native mouse/touch holds, multi-touch, focus changes, display timing/photometry and physical hardware remain separate operator checks. Use [APPARATUS_QUALIFICATION.md](APPARATUS_QUALIFICATION.md). The software cannot establish those results by simulation.
