# s1-ledger-001 — D-S1.1 ledger read: the ledger is the wrong instrument

**Run:** 2026-08-04. `harness run MediatR Serilog FluentValidation --dotnet dotnet` — full round-trip
(convert, build, recover, test, compare), producing the per-file conversion loss ledger.
**Corpus pins:** MediatR `fb309026`, Serilog `0597ddfb`, FluentValidation `71b3c60c`.
**Deliverable:** WS-S1 D-S1.1 — "read the ledger as a **marginal-recovery ranking**: a one-loss-away
file histogram, ranked by files-made-native-if-fixed", feeding **D-S1.1a**'s early abort.

## What D-S1.1 asked for, and what it found

| Project | Native | Denominator | `NativeFraction` | Files needed for M-S1 (0.70) |
|---|---:|---:|---:|---:|
| FluentValidation | 74 | 139 | 0.532 | **+24** |
| MediatR | 15 | 32 | 0.469 | **+8** |
| Serilog | 44 | 110 | 0.400 | **+33** |

**One-loss-away files — the entire ranked work-list D-S1.1 specified:**

| Project | Loss kind | Files made native if fixed |
|---|---|---:|
| MediatR | `InteropPreserved` | 4 |
| Serilog | `PreprocessorStripped` | 2 |
| FluentValidation | `InteropPreserved` | 1 |
| | **Total** | **7** |

**Against a need of 65.** There are no multi-kind loss files anywhere, so 7 is not a floor — it is the
ledger's *entire* achievable recovery, on any ranking, at any effort.

## The finding: the gap is not made of losses

| Status | FluentValidation | MediatR | Serilog | Total |
|---|---:|---:|---:|---:|
| `Replaced` (native) | 74 | 15 | 44 | **133** |
| `Reverted` | 38 | 6 | 43 | 87 |
| `CompileError` | 20 | 2 | 15 | 37 |
| `EmitSyntaxError` | 6 | 5 | 6 | 17 |
| `Replaced` (with-loss) | 1 | 4 | 2 | **7** |
| **Denominator** | 139 | 32 | 110 | **281** |

Of the **148-file** non-native gap:

- **7 files (4.7%)** converted and carry a **declared loss** — the loss ledger.
- **141 files (95.3%)** **never converted or compiled at all** — reverted by build recovery, or failed
  with a compile/emit error.

**D-S1.1's premise is wrong.** It assumed fidelity work means fixing *declared losses* — the honest
degradations the ledger records. It doesn't: 95% of the gap is the converter failing outright. The
ledger is a faithful instrument pointed at 4.7% of the phenomenon.

## D-S1.1a — the early abort fires as written, and firing it would be an artifact

D-S1.1a: *"If the ledger's cumulative achievable recovery cannot reach the M-S1 bar, PP-S1 resolves
miss at D-S1.1, before D-S1.2 is funded."*

Read literally, **the trigger fires**: 7 achievable against 65 needed, on every project.

**But resolving PP-S1 = miss on this would repeat the exact error this program has already made
twice** — retiring on an instrument artifact. PP-S1 claims *converter fidelity is movable*. What has
been shown is that the **loss ledger** cannot move it, which is a fact about the work-list's source,
not about fidelity. The precedents are on the record: the §4 go/no-go would have retired the venue on
a pre-widening ceiling of 9 that was an artifact of our own mutation operator; D-5's threshold
inverted with the operator set and was referred rather than obeyed.

**And the bar is reachable from the right work-list.** Recomputing against the failure statuses:

| Project | Need | Available in `Reverted` + `CompileError` + `EmitSyntaxError` |
|---|---:|---:|
| FluentValidation | +24 | 64 |
| MediatR | +8 | 13 |
| Serilog | +33 | 64 |

M-S1 is **not structurally unreachable** on any project — it requires fixing 38% / 62% / 52% of that
project's conversion failures. That is hard, and it is emitter-correctness work rather than
loss-ledger work, but it is not the "the 40–53% is structural" world PP-S1's miss branch describes.

**This record does not adjudicate PP-S1.** Both readings are stated and the disposition is referred,
exactly as D-5 was. Deciding unilaterally to continue would be repricing after seeing the answer;
deciding to abort would retire an L-sized workstream on a proxy that covers 5% of its subject.

## What D-S1.1 should have been, and is now

The ranked work-list for WS-S1 is **not** loss kinds. It is:

1. **`Reverted` — 87 files (59% of the gap).** Converted and replaced, then reverted by build
   recovery: the project did not compile with them in. Highest-yield and least understood.
2. **`CompileError` — 37 files (25%).** The converted file did not compile.
3. **`EmitSyntaxError` — 17 files (11%).** The emitter produced syntactically invalid output — the
   most clearly-a-bug category, and the smallest.

Ranking *within* those categories by failure cause is the D-S1.1 that should be run, and it needs
the harness to record **why** each file failed, which it currently does not aggregate.

## Corroboration

The `NativeFraction` figures (0.532 / 0.469 / 0.400) reproduce the Slice B measurement and the
funnel probe's per-project numbers exactly, so this is the same conversion behaviour measured by a
third independent path.

## What this record does not claim

- **No adjudication of PP-S1**, M-S1, or the §4 go/no-go.
- **Not that the 141 failures are easy.** No cause analysis has been done; "reachable in principle"
  is a statement about arithmetic, not effort.
- **Not that the 7 ledger files are worthless** — they are simply not a route to the bar.
- One incidental defect found: `harness run` crashes with an unhandled `Win32Exception` when
  `~/.dotnet/dotnet` is absent, because — unlike `gen-tasks` and `enumerate-supply` — it has no PATH
  fallback. Worked around with `--dotnet dotnet`; recorded, not fixed here.
