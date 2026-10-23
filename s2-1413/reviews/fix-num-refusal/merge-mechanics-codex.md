# S2 #1413 fix-num-refusal — Codex merge-mechanics check (decision Q11; not a review round)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), run in a checkout at merge commit `52a77bfa` (parents `c04b97b3` and origin/main `7d94d095`). The prompt listed the maintainer-accepted resolutions and asked for confirmation that each is the union of both parents, that the merge adds no other edit, that the `c04b97b3` repair content is otherwise unchanged, and that the manifest totals are main's plus the branch deltas.

Result: **CONFIRMED** (run 2). Run 1 returned NOT-CONFIRMED only because its prompt's exception list for item (3) omitted edits identical on both sides; the maintainer-directed rerun (run 2) corrected that list and changed nothing else.

## Run 1 (verbatim)

Note: NOT-CONFIRMED solely because the prompt's exception list for item (3) omitted one test edit (`S2CacheLiteralWidthTests.cs:280-284`) that main already contained byte-for-byte (blob `12b6eedd` in the repair, main, and the merge), which a merge deduplicates. Items (1), (2), and (4) were confirmed, and the run states that the repair content is preserved.

VERDICT: NOT-CONFIRMED

The merge mechanics pass, but condition **(3)’s exact diff equality** has one additional, benign exception.

1. **Conflict resolutions pass.** The three source hunks retain both parents’ lines exactly, selecting only `"1.22"` over `"1.21"`:
   - `src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs:183–194`: INFERRED-WIDTH prefix plus nested-quantifier check.
   - `src/Calor.Compiler/Verification/Z3/Cache/VerificationCacheEntry.cs:89–98`: both comments plus `"1.22"`.
   - `src/Calor.Compiler/Verification/Z3/Z3Verifier.cs:78–89`: IsOverflowSensitive plus NonNullModelUnsat.
   
   `CHANGELOG.md:24–81` is also an exact keep-both union. No resolution changes logic beyond the authorized version selection.

2. **No other merge edits found.** `--remerge-diff` could not create temporary objects under the read-only sandbox. Read-only reconstruction using `git merge-file --object-id -p --diff-algorithm=histogram` found exactly the five listed conflict files. All content outside conflict regions and all remaining tree entries match the clean merge.

3. **Literal patch equality fails; repair content remains preserved.** Beyond the listed exceptions, `tests/Calor.Compiler.Tests/S2CacheLiteralWidthTests.cs:280–284` appears in `git diff ffa75e8bf c04b97b3` but disappears from `git diff 52a77bfa^2 52a77bfa`. Main already contains that identical edit. The repair, main, and merge all have the same file blob, `12b6eedda9ace3231508e9092f9b0c80ddeb272f`. This is automatic deduplication, not a dropped repair.

4. **Manifest arithmetic passes for all 13 projects.**
   - `eng/test-manifest.json:8`: Compiler = **12780 + (12651 − 12641) = 12790**.
   - `eng/test-manifest.json:106`: Verification = **448 + (436 − 411) = 473**.
   - Every other branch delta is zero. The full JSON equals main plus those deltas and the branch’s unchanged note suffixes.

The sole reason for NOT-CONFIRMED is the additional already-on-main test edit omitted from condition (3)’s exception list.

## Run 2 (verbatim)

VERDICT: CONFIRMED

HEAD is `52a77bfa`, with parents `c04b97b3` and `7d94d095` (`origin/main`). Their merge base is `ffa75e8b`.

1. **The resolutions match the authorized mechanics.** [ContractHasher.cs:183](src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs#L183) retains the repair’s `INFERRED-WIDTH` prefix and main’s nested-quantifier check, verbatim. [VerificationCacheEntry.cs:89](src/Calor.Compiler/Verification/Z3/Cache/VerificationCacheEntry.cs#L89) retains both comment blocks in version order and selects `"1.22"` at line 98. [Z3Verifier.cs:78](src/Calor.Compiler/Verification/Z3/Z3Verifier.cs#L78) retains both `IsOverflowSensitive` and `NonNullModelUnsat`, verbatim. [CHANGELOG.md:24](CHANGELOG.md#L24) retains both sides’ entries. No merge-authored logic change appears.

2. **There are no other edits.** Git’s read-only `merge-tree` output identifies exactly those five conflict files: three source files, CHANGELOG, and manifest. Reconstructing its automatic result and comparing the complete trees found **17,043 non-conflict entries matching, zero mismatches**. Text outside the conflict spans also matches byte-for-byte in all five files. `git show --remerge-diff` could not execute because this sandbox prohibits its temporary object directory; the checks used read-only three-way reconstruction instead.

3. **The frozen repair is preserved.** Comparing base→repair with main→merge gives identical added/deleted line sequences for 19 files. The remaining differences are precisely the three source resolutions, CHANGELOG ordering, manifest arithmetic, and the already-shared cache test. The source diff’s shared blank separators account for its insertion-count differences.

   All **25 repair-touched files** are accounted for below. “Repair” means the merge blob equals `c04b97b3`; “clean union” means Git’s automatic three-way combination matches exactly, with the repair’s added/deleted lines unchanged.

| File:line | Merge blob | Result |
|---|---|---|
| [CHANGELOG.md:24](CHANGELOG.md#L24) | `70256314` | Keep-both union |
| [verifier-runtime-differential.json:30](bench/phase0-agent-native/verifier-runtime-differential.json#L30) | `554d317b` | Repair |
| [verifier-runtime-differential.md:16](bench/phase0-agent-native/verifier-runtime-differential.md#L16) | `4a42e3da` | Repair |
| [eng/test-manifest.json:8](eng/test-manifest.json#L8) | `c6e10b49` | Authorized resolver |
| [Ast/ExpressionNodes.cs:26](src/Calor.Compiler/Ast/ExpressionNodes.cs#L26) | `d9d88f5d` | Repair |
| [Parsing/Lexer.cs:2330](src/Calor.Compiler/Parsing/Lexer.cs#L2330) | `e9c3f6f4` | Repair |
| [Parsing/Parser.cs:4898](src/Calor.Compiler/Parsing/Parser.cs#L4898) | `69e92600` | Repair |
| [Parsing/Token.cs:446](src/Calor.Compiler/Parsing/Token.cs#L446) | `10056234` | Repair |
| [Verification/ExpressionSimplifier.cs:31](src/Calor.Compiler/Verification/ExpressionSimplifier.cs#L31) | `026525b9` | Repair |
| [Cache/ContractHasher.cs:183](src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs#L183) | `ac58a75c` | Keep-both union |
| [Cache/VerificationCacheEntry.cs:89](src/Calor.Compiler/Verification/Z3/Cache/VerificationCacheEntry.cs#L89) | `db29002c` | Both comments; version 1.22 |
| [Z3/ContractTranslator.cs:76](src/Calor.Compiler/Verification/Z3/ContractTranslator.cs#L76) | `a7367b11` | Clean union |
| [KInduction/KInductionProver.cs:115](src/Calor.Compiler/Verification/Z3/KInduction/KInductionProver.cs#L115) | `b9282bf3` | Clean union |
| [Z3/Z3Verifier.cs:78](src/Calor.Compiler/Verification/Z3/Z3Verifier.cs#L78) | `d8fc2f2a` | Keep-both union |
| [Analysis/KInductionTests.cs:515](tests/Calor.Compiler.Tests/Analysis/KInductionTests.cs#L515) | `ed600faa` | Repair |
| [Calor.Compiler.Tests.csproj:26](tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj#L26) | `0244f063` | Repair |
| [ProductionOverflowRuntimeTests.cs:316](tests/Calor.Compiler.Tests/ProductionOverflowRuntimeTests.cs#L316) | `16cd89f2` | Repair |
| [S2CacheLiteralWidthTests.cs:280](tests/Calor.Compiler.Tests/S2CacheLiteralWidthTests.cs#L280) | `12b6eedd` | Repair = main = merge |
| [S2NumericDeterminismTests.cs:1](tests/Calor.Compiler.Tests/S2NumericDeterminismTests.cs#L1) | `3ce285c8` | Repair |
| [NumericExecutableSemanticsTests.cs:4](tests/Calor.Verification.Tests/NumericExecutableSemanticsTests.cs#L4) | `0de791a4` | Repair |
| [OverflowSoundnessBenchmark.cs:60](tests/Calor.Verification.Tests/OverflowSoundnessBenchmark.cs#L60) | `a04d3c40` | Repair |
| [S2NumericRefusalTests.cs:1](tests/Calor.Verification.Tests/S2NumericRefusalTests.cs#L1) | `7352dec9` | Repair |
| [VerifierRuntimeDifferential/DifferentialGate.cs:28](tests/Calor.Verification.Tests/VerifierRuntimeDifferential/DifferentialGate.cs#L28) | `508433b2` | Repair |
| [Calor.Verification.Tests/VerifierTests.cs:79](tests/Calor.Verification.Tests/VerifierTests.cs#L79) | `9795fc78` | Clean union |
| [W1Slice1SoundnessTests.cs:66](tests/Calor.Verification.Tests/W1Slice1SoundnessTests.cs#L66) | `6cb8434b` | Repair |

The change at `S2CacheLiteralWidthTests.cs:280–284` is already present on main: all three blobs are `12b6eedd`. Its absence from main→merge is automatic deduplication, not a dropped repair.

4. **Manifest totals are correct.**

| Project | Base | Repair | Branch delta | Main | Merge |
|---|---:|---:|---:|---:|---:|
| Compiler — [manifest:8](eng/test-manifest.json#L8) | 12,641 | 12,651 | +10 | 12,780 | **12,790** |
| Verification — [manifest:106](eng/test-manifest.json#L106) | 411 | 436 | +25 | 448 | **473** |

Every other project has zero branch delta and retains main’s total. Skip counts are unchanged. Both modified notes equal main’s note plus the branch’s exact appended suffix. The entire parsed manifest equals the expected resolver result.
