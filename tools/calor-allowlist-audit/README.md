# calor-allowlist-audit

The **dogfood utility** (roadmap-v0.13-v0.15 §2.2): a real in-repo tool whose
only source is Calor.

## What it does

Audits `.calor-csharp-allowlist` against the rules that file states in prose but
nothing enforced. The Calor-first guard (`scripts/check-calor-first-diff.sh`)
*reads* the allowlist; it never validates it. So a deleted or renamed C# file
leaves a permanent, silent permission behind — the guard keeps honouring an
entry for a path nothing occupies.

Findings, one per defect:

| check | why it matters |
|---|---|
| entry does not exist on disk | a stale entry keeps permission alive for a path nothing occupies |
| entry listed more than once | duplicates hide which justification comment governs the path |
| entry contains a wildcard | the allowlist's own rule is "keep entries specific; no directory wildcards" |
| entry is not a `.cs` path | the guard only inspects `*.cs`, so the entry can never match |
| entry is under a structurally exempt tree | `tests/`, `bench/`, and `tools/Calor.RoundTrip.Harness/` are already exempt, so the entry grants nothing and misleads a reader into thinking permission was needed |

Existence is only reported for an otherwise well-formed path — a wildcard or
non-`.cs` entry gets one finding, not two about the same defect.

## Pending entries

The Calor-first guard requires an allowlist entry to land **before** the PR that
adds the C# file. Taken together with the staleness check above, that deadlocks:
the guard demands the entry first, and this audit fails it for not existing yet.
The deadlock was real — it blocked the first new `src/` file after this utility
became blocking.

A `# pending` comment on the line immediately above an entry marks it as
pre-registered:

```
# pending: lands with the S1 index PR
src/Calor.Compiler/Indexing/ProjectIndex.cs
```

The guard is unaffected (it skips comments, and the entry still grants
permission). This audit reports the entry informationally instead of failing.

**Pending is a temporary state and clears itself:** once the file exists, a
still-marked entry is a *finding* — remove the marker. Otherwise the entry would
keep its exemption from the staleness check forever, reopening the very hole
this audit exists to close. The marker applies to exactly one entry; a blank
line or any other comment clears it.

Exit code is `0` when clean, `1` on findings, `2` if the allowlist is missing.
Run it from the repository root.

```
dotnet build tools/calor-allowlist-audit/CalorAllowlistAudit.csproj -c Release
dotnet tools/calor-allowlist-audit/bin/Release/net10.0/calor-allowlist-audit.dll
```

## Why it is dogfood, mechanically

- The only source is `allowlist-audit.calr`. There is no `.cs` in this
  directory and there never may be.
- Generated C# lands in `obj/calor/` and is gitignored explicitly, not merely by
  the global rule.
- `scripts/check-dogfood-utility.sh` fails CI if any `.cs` or any build output
  becomes tracked here, or if the `.calr` source disappears. Both faults were
  verified to fail the guard.
- The project is in `Calor.sln` and CI **builds and runs** it, so a Calor
  regression that breaks this program breaks the build — the repo genuinely
  depends on a Calor program.

In 0.14 this utility becomes the first migration subject of the 2.0.0 migrator
(§3.3).

## Known language friction

Written down because dogfooding exists to surface it:

- **#929** — the module prose belongs in `§CT`, but a module-level `§CT`
  currently breaks the dedent that closes a later `§IF`, and this program is
  full of `§IF`. The prose sits in a leading `//` comment instead.
- Integer-to-string has no dedicated form; the report line relies on `+`
  concatenation with a string on the left at each step.
- There is no `continue`, so per-entry checks nest under a single
  `§IF{...} (! skip)` rather than skipping early.
