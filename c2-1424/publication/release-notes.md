## [0.24.0] - 2026-10-07

Calor 0.24 is a soundness release. It repairs verifier defects found by a registered soundness
sweep, and it adds machinery that makes the release evidence reproducible. It adds no new syntax.

This is the first release on NuGet since 0.21.0. The v0.22.0 tag and GitHub pre-release exist,
but the NuGet publish for 0.22.0 was skipped after its performance tests failed, so 0.22.0 never
reached NuGet. Milestone 0.23 was planning-only and shipped no code. If you upgrade from 0.21.0,
read the 0.22.0 entry below as well: its changes are part of this release.

The 0.24 evidence is adjudicated by the maintainer who directed and merged the repairs. It is not
independently adjudicated or independently verified, so every 0.24 evidence claim is at most
*bounded*. No benchmark results are published with this release.

### Advisory: false proofs in 0.21.0 (and the unpublished 0.22.0)

The 0.24 soundness sweep (a fixed, registered set of generated verifier test cases) checked two
builds: the 0.21.0 NuGet package and the v0.22.0 source. In each build it found **7 false
proofs in 4 areas**. A false proof is a "proven" or "valid" verdict for a claim that a concrete
input violates. The published 0.21.0 package is not changed; the defects remain in it.

1. **Runtime checks removed (3 cases).** In these two areas, 0.21.0 could report a proof and
   delete a runtime check that the program then needed.
   - *Proof obligations after reassignment (2 cases).* With `§Q (> x -1)`, then `§ASSIGN x -5`,
     then `§PROOF (> x -1)`, the obligation solver still used the precondition, reported the
     obligation discharged, and removed its runtime check. In 0.24.0 the stale fact is not used.
     The obligation is `Unsupported` (`Calor1124`), and the runtime check stays. This is a
     demotion: 0.24.0 withdraws the claim; it does not prove anything new.
   - *The verification cache confused `INT:1` with `LONG:1` (1 case).* After compiling
     `x + LONG:1`, a later compile of `x + INT:1` reused the cached `Proven`, although
     `int.MaxValue + 1` overflows, and the runtime check was removed. In 0.24.0 the cache key
     includes each literal's width, and the case is refuted as it should be. This is a fix.
2. **Wrong interface acceptance (4 cases).** No runtime check was removed, but the verdict was
   wrong. When a class implements an interface, Calor checks that the class's precondition
   accepts every input the interface accepts. 0.21.0 reported that check as proven
   (`Calor0815`) for class preconditions that throw on, or reject, an input the interface
   allows.
   - *Preconditions that read a string that may be null (3 cases).* In 2 cases,
     `(>= (len s) 0)` throws at `s = null`. In 1 case, `(|| (! (isempty s)) (== s ""))` is false
     at `s = null`. In 0.24.0 all 3 are `Assumed`, not proven, with the new warning `Calor0819`.
     This is a demotion.
   - *A remainder that may divide by zero (1 case),* `(> (% x y) -2)`, which throws at `y = 0`.
     In 0.24.0 the check is refuted with the input `x = 3, y = 0`, reported as an error
     (`Calor0810`). This is a fix.

A related defect was found outside the sweep (#1493). On Windows, two different non-ASCII
identifiers could in principle become one solver variable and so prove a false contract. It was
not observed in practice. 0.24.0 fixes it.

**What to do.** Upgrade to 0.24.0 and regenerate your C#. C# that 0.21.0 generated still lacks
any runtime check that 0.21.0 removed; the upgrade alone does not change it. The verification
cache format changed, so cached verdicts from older versions are recomputed automatically.

**What the sweep does not show.** The sweep samples registered forms. It is not a proof that the
verifier is sound.

- It ran 710 cases per build, on one platform (macOS arm64), twice. Both runs gave the same
  verdicts and the same findings. One counterexample differed between runs; both violated the
  property.
- Of its 100 registered rows (groups of related cases), 79 found no counterexample within the
  sweep's budget. That means only that none was found. 44 of those 79 rows contain no proven
  case at all, so they support no claim about `Proven` results. 7 rows were outside the
  registered scope and were not run.
- Whole-compiler soundness is not established. Other false proofs may exist in forms the sweep
  did not sample.

The sweep's other 27 findings per build were not false proofs. 8 were true proofs of forms that
the registration says must not be reported `Proven`. 17 were spurious refutations: reported
counterexamples that the program cannot produce. 2 were further stale cache verdicts with the
same cause as the cache case above. 0.24.0 fixes or visibly demotes all 34 findings. A demoted
form becomes `Assumed` or `Unsupported` and keeps its runtime check. The entries under **Fixed**
give the details.

### Added

- **Warning `Calor0819` ("Assumed, not proven") for interface contract checks (#1413).** It
  marks a check that holds only under an assumption the solver cannot model, such as a string,
  array, or user-type value that may be null. See the interface entry under **Fixed**.
- **Reproducible release evidence.** The repository now records, under `docs/plans/`, the 0.24
  evidence contract, the registered soundness sweep and its results (#1311), a disposition for
  every finding (#1413), and a registered verifier determinism protocol (#1421). A release can
  be published only through a gate that checks one adjudication record (#1410).

### Changed

- **Benchmark publication refuses incomparable results (#1422).** The benchmark workflow now
  proposes one headline file, built only from the B1 results packet (the 0.24 pair-equivalence
  results). The workflow fails and opens no pull request in any of these cases:
  - an included pair is not `EQUIVALENT`, or a registered pair changed;
  - the method changed without an evidence-contract amendment merged first;
  - the regenerated packet differs;
  - the provenance commit is not a full SHA on `main`.

  A failed run tries to close open benchmark-results pull requests. Across different
  methods it prints no delta. The `allow_weaker_methodology` override is removed. Adjudication
  of what is published is a separate gate (#1410).
- **The agent refactoring job no longer commits to `main`.** It uploads its results and fails
  when it cannot read a pass rate, instead of recording 0.
- **Older website benchmark numbers are labeled historical.** They stay published, marked as not
  comparable under the 0.24 method.
- **Z3 native libraries reach every consumer through one verified build step (#1420).** The
  compiler build validates the bootstrapped Z3 files, then hands the same set to test projects,
  `Calor.Tasks`, the `calor` tool package, and the SDK package. Packages carry natives for
  linux-x64, linux-arm64, osx-arm64, win-x64, and win-arm64. Before, a build could pass
  validation yet ship no native library, and the Z3 tests in that consumer then skipped silently.
- **CI fixture checks report what they actually do (#1241).** A script that claimed an AST round
  trip (parse, emit, parse) compiled each fixture once and compared nothing. It is replaced by one
  that says it compiles each fixture once. An explicit list now gives the expected outcome for all
  509 tracked `.calr` fixtures: 428 compile, 51 must be rejected, and 30 are known failures that
  never count as passes.

### Fixed

- **Three numeric forms are refused or demoted as the verification contract requires (#1413).**
  The 0.24 soundness registration lists three numeric forms that must not be reported `Proven`.
  The verifier proved all three. Each proof was true, but it was a claim the registered rules do
  not allow. Now:
  - Arithmetic, shifts, and negation whose operands are all narrower than 32 bits (`i8`, `u8`,
    `i16`, `u16`) are `Unsupported`. This is divergence D1 in
    `docs/verification-modeled-forms.md`. Comparisons on narrow values are still modeled.
  - An `INT:` literal outside the 32-bit range, such as `INT:3000000000`, is `Unsupported`
    (divergence D2). Spell it `LONG:` to have it modeled. The verifier keeps the refusal even
    when simplification would fold the literal away. A k-induction loop proof (an inductive
    proof of a loop invariant) is `Unsupported` for the whole loop when a `for` bound or step,
    or any literal in a `while` condition or body, is outside the 32-bit range.
  - In a checked module, a postcondition (`§S`) with checked arithmetic is now `Assumed` with the
    `checked-arithmetic` assumption, unless the operand types and literal values show that no
    operation can overflow. This holds even when the preconditions rule the overflow out. Before,
    such postconditions were `Proven`, and their runtime checks were removed; the checks are now
    kept. The overflow decision is a fixed rule, not a solver query. Each operand's range comes
    from its type, or from its value if it is a literal. The rule asks whether every result fits
    the promoted result type. For example, `i32` plus `LONG:1` and `i32` times `u32` (computed in
    64 bits) cannot overflow, so they stay `Proven`. `i32` plus `i32` can overflow, so it is
    `Assumed`. Because no solver is consulted, the verdict does not depend on the platform or on
    solver time. A guard inside the postcondition does not count either. So `(-> (< value
    Int32.MaxValue) (== (- (+ value 1) 1) value))` is `Assumed`, and the unselected branch is still
    never evaluated. In the verifier-runtime differential report (#1135), 6 of the 1,170 cases
    move from `Proven` to `Assumed`: the provable `i64` and `u64` postconditions, which guard a
    64-bit product. The totals are now 429 `Proven`, 156 `Assumed`, and 585 refuted. Proof
    obligations, preconditions, and interface checks are unchanged: the registered row covers
    postconditions only.

  The verification cache format changes (0.24.0 uses format 1.22), so older entries are
  invalidated.
- **Text reaches the solver with .NET's meaning (#1413, #1493).** A string literal is now sent to
  Z3 one UTF-16 code unit at a time, so `"é"` has length 1 there, as `"é".Length` does in .NET.
  Before, it had length 2 (one per UTF-8 byte), and a true postcondition such as
  `(<= (len result) 1)` was reported as possibly violated (`Calor0712`) with a counterexample
  the program cannot produce. A backslash in a literal is no longer read as a Z3 escape.
  `s.Substring(i, n)` and `s.Substring(i)` now carry their range conditions, so a
  counterexample is not an input where that substring throws. A substring in a conditionally
  evaluated position, or one whose range reads a local binding, makes the result
  `Unsupported`. `IndexOf` with a start index is now
  `Unsupported`: the generated C# ignores the start index, so the solver must not model one.
  Separately, Z3 symbol names for non-ASCII identifiers are now escaped to ASCII. On Windows,
  two different identifiers outside the code page (say `ж` and `щ`) could become one solver
  variable, which could in principle prove a false contract. This was not observed in
  practice. Proofs that touch strings stay `Assumed` and
  keep their runtime checks. A precondition set that is unsatisfiable only in the solver's
  null-free model is no longer reported as vacuous or unsatisfiable when a null could satisfy
  it (through `==`, `Equals`, or `IsNullOrEmpty` on a parameter); the result is `Unsupported`.
  The verification cache format changes (0.24.0 uses format 1.22), so older entries are
  invalidated.
- **No proof is claimed for a nested quantifier (#1413).** A postcondition with a bounded
  `forall` inside another `forall` was reported `Proven`, although the compiler then rejected
  its runtime check (`Calor0326`). As a conservative restriction, the verifier now does not
  verify any quantifier nested inside another. This covers contracts, proof obligations and
  the preconditions and facts they assume, interface contract checks, and guard validation.
  The result is `Unsupported` and the runtime check is kept, even for nested forms the
  compiler can check at run time. The rule applies to the contract after simplification (a
  nested quantifier that simplifies to `true` is still proven). The verification cache never stores or serves such a
  result. A k-induction invariant with a conjunct the prover cannot parse is no longer
  reported proven from the conjuncts it could parse.

- **Three causes of run-to-run and platform-dependent verifier verdicts are fixed (#1135).**
  Whether the verifier is now deterministic on every supported platform is decided by the
  registered determinism protocol (#1421), which runs after this change.
  - **Run to run.** The contract verifier, the obligation solver, and the implication prover now
    check each query in a fresh Z3 context with the same settings. A Z3 context that Calor did
    not create has unknown settings, so its queries are still checked in that context. Before,
    Z3 reused the ids of terms that .NET's garbage collector had released, so the order Z3
    searched in depended on when the GC ran. The same query could take a different amount of
    solver work from one run to the next. Once in CI, a normally fast query hit the 5-second
    timeout and failed the release-critical oracle on an unchanged tree. Each solver check now
    costs slightly more time. A check no longer reuses search state from earlier checks, so a refuted
    contract can report a different, equally valid counterexample, and a query close to the
    timeout can end differently than before.
  - **Windows strings.** A string literal with a non-ASCII character reached Z3 in the Windows
    code page instead of UTF-8. For example, `"é"` had length 1 on Windows but 2 on Linux and
    macOS, so a postcondition about its length was refuted (`Calor0712`) on Windows only. All
    platforms now send the same encoding: first the UTF-8 byte model, and in the final 0.24.0
    the UTF-16 code units described in the "Text reaches the solver" entry above. A proof that
    depends on string semantics is still reported as assumed and keeps its runtime check. The
    verification cache format changes (0.24.0 uses format 1.22), so old cached verdicts
    are recomputed once.
  - **User-level cache on Windows.** The default verification cache and the user effect
    manifests (`~/.calor`) now honor `USERPROFILE` on Windows, as NuGet does. Linux and macOS
    are unchanged.

- **Two more kinds of unreachable counterexample are withheld (#1413).** A failed obligation is
  a compile error: `Calor1140` for a `§PROOF`, `Calor1121` for a refinement. It is no longer
  reported in two cases where its counterexample may be an input that never reaches the
  obligation.
  - **Something evaluated before the obligation may throw on that input.** That includes any of
    these:
    - an earlier statement;
    - another operand in the same statement;
    - an enclosing `if`, `elseif`, or loop condition;
    - a precondition, parameter refinement, or constructor initializer (`§BASE`/`§THIS`);
    - a compiler-inserted refinement guard on a binding, rebinding, or assignment.

    Forms that may throw include checked or dividing arithmetic, a call, a member read, a
    substring, a length or string query on a local that may be null, and an earlier `§PROOF`
    guard. When the module declares operator overloads, conversions, or raw C# members, every
    non-literal form counts.
  - **The obligation reads a property, whose getter the solver does not model.** This includes
    a property that hides an inherited field, properties of nested types, a `Length` property read
    by `§LEN`, and, when the module has raw C# members, any member read.

  The result is `Calor1124` ("unsupported"), and the runtime check stays. This can withhold
  counterexamples that were real. Properties are matched by name, so a field that shares a name
  with any property in the module is also withheld. These checks never make an obligation proven
  or discharged.

- **Proof obligations no longer use facts that an assignment made stale (#1413).** With
  `§Q (> x -1)`, then `§ASSIGN x -5`, then `§PROOF (> x -1)`, the obligation solver still
  assumed the precondition, reported the obligation discharged, and removed its runtime check.
  A precondition or parameter refinement is now ignored for every obligation in a body that
  may write a name it reads, wherever that write is. Writes include `ref`/`out` arguments and
  writes through an aliased `ref`/`in` parameter. A fact that reads an array element or field
  is ignored once the body can change them, including through a call, a property getter, an
  indexer, or `foreach`. A body with raw C# (`§RAW`, `§CS`), unsafe or pointer code, or a
  lambda gets no facts at all; raw C# in a parameter refinement or precondition makes every
  obligation of that function `Calor1124`. A failed obligation
  (`Calor1121`/`Calor1140`, a compile error) is now reported only when the solver's state
  matches the program's at that point: no reassigned name, no unasserted enclosing guard,
  and no earlier loop or exit. Otherwise the result is `Calor1124` ("unsupported") and the
  runtime check stays. Statements that throw before the obligation are covered by the entry
  "Two more kinds of unreachable counterexample are withheld" above.
  New facts: an `else`/`elseif` body knows that the earlier conditions were false, and a
  parameter of a named refinement type (`§I{Pos:x}`) satisfies its predicate on entry.
- **Interface contract checks no longer prove what can throw (#1413).** When a class implements
  an interface, Calor checks that the class's precondition accepts every input the interface
  accepts. That check treated `s.Length` as defined for a null string and `x % y` as defined
  for `y = 0`, and reported "Precondition weakening proven" (`Calor0815`) for preconditions that
  throw on such inputs. The check now models when a quantifier-free integer contract can throw:
  an unconditional zero divisor, or checked overflow. When such a class precondition throws on
  an input the interface accepts, Calor reports an LSP error (`Calor0810`) with that input as
  the counterexample. The interface's precondition limits which inputs its postcondition must
  cover. A proof that depends on string, array, or user-type values is reported as the new
  warning `Calor0819` ("Assumed, not proven"), because the solver cannot model them as null.
  A counterexample over such values is not claimed, because index, substring, and null
  failures are not modeled. In those cases, and for conditional divisors and quantified
  contracts, the check says that it could not decide (`Calor0816`). This includes the check
  that inherited guarantees are compatible. Two identical contracts always count as
  compatible. Calor reports the inheritance as valid (`Calor0814`) only when every check was
  proven or the contracts are identical. The syntactic fallback no longer treats `x != c` as
  weaker than `x == c`. The weakening check (`calor verify --weakening-check`) reports
  contracts as incomparable when the two files use different overflow policies.

- **The verification cache no longer mixes up `INT:1` and `LONG:1` (#1413).** The cache key
  hashed an integer literal by its value only. After compiling `x + LONG:1`, a later compile
  of `x + INT:1` reused the cached `Proven`, although `int.MaxValue + 1` overflows; the
  runtime check was then removed. Keys now include each literal's width, signedness, and
  (for real literals) float/double/decimal kind. The cache format changes (0.24.0 uses format 1.22), so every
  older entry is invalidated.

- **An empty clause body no longer swallows the next statement (#1485).** A
  clause such as `§CA` (catch) with no indented lines used to pull the next
  statement at the same column into its body. The program compiled with no
  diagnostic and behaved differently: the converted X-TRYCATCH-01 program
  returned `1` instead of `2`. A clause now owns only the lines indented
  under it, so an empty body stays empty. The fix covers every construct with
  an indented body: `§IF`/`§EI`/`§EL`, `§L`, `§WH`, `§DO`, `§EACH`, `§EACHKV`,
  `§USE`, `§TR`/`§CA`/`§FI`, `§SYNC`, `§UNSAFE`, `§FIXED`, `§W` and its `§K`
  cases, `§LAM`, `§PP`/`§PPE`, property and event accessors, `§F`/`§AF`, `§CL`,
  `§EN`, `§EEXT`, `§DC`, and `§CT`. Two committed programs were parsed wrongly
  before: an empty abstract class in a conversion snapshot used to adopt the
  next three classes as nested classes, and a one-line `§CT` context in an
  edit-script fixture used to swallow the module's only function. A tolerated
  explicit closer such as `§/TR{id}` still ends a body written at the
  opener's own column.

