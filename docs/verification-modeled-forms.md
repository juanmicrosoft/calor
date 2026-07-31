# Z3 Verification — Modeled-Forms Whitelist

**Status:** Normative, documentation-grade (v1, 2026-07-02)
**Role:** This is the enumerated definition of the *sound subset* — the expression forms the contract prover actually models. Phase 0 wedge fixtures are authored against this list; a contract using anything outside it receives `Unsupported` (runtime check retained). See [`plans/agent-native-strategy.md`](plans/agent-native-strategy.md) §4–§5 for the plan context; the enforcement rearchitecture (positive whitelist in code, replacing the exception-fallback sites across `Z3Verifier.cs` and `ContractTranslator.cs`) is Phase 2b item 5.
**Source of truth:** `src/Calor.Compiler/Verification/Z3/ContractTranslator.cs` (primary translator, ~1,600 lines), `Verification/Z3/Z3Verifier.cs` (driver). Every claim below carries a file:line reference verified against v0.6.7. When code and this document disagree, the code is the bug or this document is stale — either way, file it.

All arithmetic is modeled with Z3 **bit-vectors** (two's-complement, fixed-width), not unbounded integers (`ContractTranslator.cs:17–20`).

---

## 1. Types modeled as Z3 variables

Names are case-insensitive and normalized (`NormalizeTypeName`, `:656–677`). Accepted forms and sorts:

| Accepted names | Z3 sort | Width | Signed |
|---|---|---|---|
| `i8`, `sbyte`, `int8`, `System.SByte` | BitVec | 8 | yes |
| `i16`, `short`, `int16`, `System.Int16` | BitVec | 16 | yes |
| `i32`, `int`, `int32`, `System.Int32` | BitVec | 32 | yes |
| `i64`, `long`, `int64`, `System.Int64` | BitVec | 64 | yes |
| `u8`, `byte`, `uint8`, `System.Byte` | BitVec | 8 | no |
| `u16`, `ushort`, `uint16`, `System.UInt16` | BitVec | 16 | no |
| `u32`, `uint`, `uint32`, `System.UInt32` | BitVec | 32 | no |
| `u64`, `ulong`, `uint64`, `System.UInt64` | BitVec | 64 | no |
| `bool`, `boolean`, `System.Boolean` | Bool | — | — |
| `string`, `str` | Z3 String (Seq) | — | — |
| `T[]` where `T` is an integer type above | Array: BitVec64 → BitVec(width of T) | 64-bit index | element per T |
| Any other non-empty name (user class; `?` suffix stripped) | Uninterpreted sort, one per name | — | — |

- Every array variable auto-creates a companion `<name>$length` variable of type **u32** (`:647–650`).
- **Not modelable as variables:** `f32`/`f64`/`float`/`double`/`single`/`decimal`; `object`/`dynamic`; `Func`/`Action`/delegate types; arrays of strings, bools, floats, or user types (`:606`, `:638–640`, `DiagnoseUnsupportedType :1597–1610`).

## 2. Expression forms modeled

**Literals:** integer (always emitted as signed 32-bit — see divergence D2), boolean, string. **Float literals are not modeled** (`:244`).

**References:** simple variable references; dotted paths (`a.b.c`) resolve as chained uninterpreted field functions on user-type sorts (`ResolveDotPath :275–327`).

**Binary operators** (operand widths normalized by sign/zero-extension):
`+  -  *  /  %  ==  !=  <  <=  >  >=  &&  ||  &  |  ^  <<  >>`
Division, modulo, comparisons, and right-shift are signedness-aware (`ShouldUseUnsignedComparison`): both-unsigned → unsigned ops; mixed → unsigned only when the signed operand is a provably non-negative literal. **Typing refusals (W1 Slice 1 — `DiagnoseUnmodeledBitVecTyping`):** arithmetic and shifts where BOTH operands are sub-32-bit are `Unsupported` (C# promotes narrow integers to int; the solver would wrap at the narrow width — D1); mixed signed/unsigned comparison, equality, and division where the signed operand is NOT a provably non-negative literal are `Unsupported` (C# compares via promotion to a wider signed type; a same-width solver comparison reads the raw bit pattern — `-1 == 4294967295u` would hold). A 32-bit-or-wider operand rescues narrow arithmetic: width normalization extends the narrow side first, matching promotion. **Division and modulo inside contract expressions carry divisor-nonzero side conditions** (collected with the body collector's position rules); a proof that used them reports **`assumed`** with the named `exceptional-paths:contract-division` assumption, never plain `Proven` — and divisors in conditionally-evaluated positions (short-circuit RHS, `?:` arms) make the obligation `Unsupported` (D8, closed W1 Slice 1). *Residual:* `Z3ImplicationProver` (the M-G4 weakening check) still totalizes division — an implication verdict over dividing contracts can be wrong in either direction; treat those as indeterminate until it adopts the same side conditions.

**Unary operators:** `!` (bool), unary `-` (integer, 32-bit-or-wider operand only — narrow negation refused, D1's unary form).

**Conditional:** `(cond ? a : b)` → Z3 ITE (`:420–430`).

**Quantifiers:** `forall` / `exists` with integer-typed bound variables; **implication** `(-> p q)` (`:454–524`).

**Self-reference `#`** in refinement predicates (`:332–342`).

**Arrays:** element access **only on a simple variable base** (no computed/nested/method-returned arrays); array length (simple variable base only). An array must be **declared with an element type** — an array first seen at an access or length site is `Unsupported` (the old i32-element default guessed a width; D6, closed W1 Slice 1).

**User-type fields:** `obj.Field` and dot-paths, modeled as uninterpreted functions; result sorts **must come from the user-type registry** — a field the registry doesn't know is `Unsupported` (the old i32 default guessed a width/signedness; D7, closed W1 Slice 1).

**String operations (exhaustive):** `Length`, `Contains`, `StartsWith`, `EndsWith`, `Equals`, `IsNullOrEmpty`, `IndexOf` (2- and 3-arg), `Substring` (3-arg), `SubstringFrom`, `Concat`. Everything else is out (see §3) — including `Replace`, un-whitelisted W1 Slice 1 (D9).

## 3. Explicitly NOT modeled (→ `Unsupported`, runtime check retained)

1. **Function and method calls of any kind** in contracts (`CallExpressionNode → null`, `:245`). Calls-in-contracts via callee summaries is Phase 2b work and will carry `Assumed` status, never `Proven`.
2. **All floating-point** types and literals.
3. **String operations:** `Replace` (first-vs-all-occurrence divergence, D9 — refused W1 Slice 1), `ToUpper`, `ToLower`, `Trim`, `TrimStart`, `TrimEnd`, `PadLeft`, `PadRight`, `Split`, `Join`, `Format`, `ToString`, `IsNullOrWhiteSpace`, all Regex operations.
4. **`StringComparison` modes:** accepted syntactically, **ignored semantically** — verification is ordinal-only; non-ordinal modes add a warning but the proof proceeds (`:863–870`). Treat culture-sensitive string contracts as unverified.
5. **Computed array bases** (method returns, nested accesses) for element access or length.
6. **`object`/`dynamic`, delegate types** as variables.
7. **Generic-typed values** (including `Option<T>`/`Result<T,E>`-typed ones) — they fall to uninterpreted sorts at best; contracts over their *contents* are not modeled. Constrains Phase 0 fixture authoring.
8. Anything not listed in §2 (default case `:246`).

## 4. Known semantic divergences from C# (tracked as defects per strategy §5.2 rule 4)

| # | Divergence | Consequence / status |
|---|---|---|
| D1 | **No narrow-type promotion**: `byte + byte` wraps at 8 bits; C# promotes to `int` (400 stays 400) | **Closed by refusal (W1 Slice 1):** arithmetic/shifts/negation on all-sub-32-bit operands → `Unsupported`. Comparisons on narrow operands stay modeled (extension matches promotion). Full promotion modeling deferred |
| D2 | **Integer literals always signed 32-bit**; out-of-int32 literals are **refused** (cache 1.7) | Within-range signedness context still unmodeled; the truncation half is closed |
| D3 | **Z3 strings cannot be null** (`IsNullOrEmpty` tests length==0 only) | Null-vs-empty indistinguishable |
| D4 | **Ordinal-only string comparison** (§3 item 4) | Culture-sensitive contracts unverified without loud failure |
| D5 | Contract `§S` holds **only on normal return**; exceptional paths surface as `assumed` (D-G2.5) | Exception-heavy code has weaker guarantees than the word "Proven" suggests |
| D6 | Arrays first seen at an access/length site defaulted to **i32 elements** | **Closed by refusal (W1 Slice 1):** undeclared arrays → `Unsupported` |
| D7 | User-type fields defaulted to **i32** without a registry entry | **Closed by refusal (W1 Slice 1):** unregistered fields → `Unsupported` |
| D8 | **Contract-expression division/modulo was totalized** — no divisor-nonzero side conditions | **Closed (W1 Slice 1):** §Q/§S divisors carry side conditions with the body collector's position rules; proofs demote to `assumed` (`exceptional-paths:contract-division`); conditional-position divisors → `Unsupported`. Residual: `Z3ImplicationProver` still totalizes (noted §2) |
| D9 | **`string.Replace` modeled as first-occurrence** while .NET replaces all occurrences (was documented in §2 but untabled — the W1 kickoff's T1 finding) | **Closed by refusal (W1 Slice 1):** `Replace` un-whitelisted → `Unsupported` |
| D10 | **Mixed signed/unsigned comparison** defaulted to signed same-width comparison; C# promotes to a wider signed type (`-1 == 4294967295u` held) | **Closed by refusal (W1 Slice 1):** mixed comparisons without a non-negative-literal signed operand → `Unsupported` |

## 5. The second translator (bug-pattern checkers) — differences

`BoundExpressionTranslator` (in `Analysis/BugPatterns/Patterns/DivisionByZeroChecker.cs:503–694`) backs the div-by-zero/overflow/index checkers. It is **narrower and signed-only**:

- **Adds:** math functions via ITE — `abs`, `min`, `max`, `clamp`, `sign` (both `math.x` and bare names).
- **Removes vs the primary:** strings, arrays, field access, quantifiers, implication, conditional, self-ref, bitwise/shift operators, all type aliases (`System.*`, `intNN`), width normalization.
- **Signed-only everywhere** — unsigned types are **refused at declaration** (checker reports no verdict rather than a signed-semantics one; guarantees plan D-G2.3). Its `%` uses `bvsrem` (C# remainder semantics), matching the primary translator since the G1 fixes.

Do not assume checker findings and contract proofs share a model; they don't.

## 6. Maintenance rule

Any change to `ContractTranslator`'s accepted forms MUST update this document in the same PR. When Phase 2b lands the positive-whitelist rearchitecture, this document becomes generated output and the hand-maintained version is retired.

## Appendix A — machine-enumerated whitelist (generated)

The authoritative enumeration lives in code: `ModeledForms` in
`src/Calor.Compiler/Verification/Z3/ContractTranslator.cs` (guarantees plan
D-G2.3). The block below is generated from `ModeledForms.RenderWhitelist()` and
byte-checked by `ModeledFormsTests.Doc_GeneratedAppendix_MatchesCodeWhitelist` —
when the whitelist changes, regenerate this block from the test's failure
output.

**Scope:** the whitelist gates the `§Q`/`§S` contract path in `Z3Verifier`.
The obligation solver, implication prover, and guard discovery instantiate
the translator directly and remain ungated (their fallback behavior is
unchanged); extending the gate to those paths is future work.

<!-- BEGIN GENERATED WHITELIST (ModeledForms.RenderWhitelist) — do not edit by hand -->
```
scalar-types: i8, i16, i32, i64, u8, u16, u32, u64, bool, str
array-element-types: i8, i16, i32, i64, u8, u16, u32, u64 (with synthetic $length)
expression-kinds: IntLiteralNode, BoolLiteralNode, StringLiteralNode, ReferenceNode, BinaryOperationNode, UnaryOperationNode, ConditionalExpressionNode, ForallExpressionNode, ExistsExpressionNode, ImplicationExpressionNode, ArrayAccessNode, ArrayLengthNode, FieldAccessNode, StringOperationNode, SelfRefNode
binary-operators: Add, Subtract, Multiply, Divide, Modulo, Equal, NotEqual, LessThan, LessOrEqual, GreaterThan, GreaterOrEqual, And, Or, BitwiseAnd, BitwiseOr, BitwiseXor, LeftShift, RightShift
unary-operators: Not, Negate
string-operations: Length, Contains, StartsWith, EndsWith, Equals, IsNullOrEmpty, IndexOf, Substring, SubstringFrom, Concat
quantifier-bound-variable-types: any declarable type except floating-point (unmodeled types become uninterpreted sorts)
```
<!-- END GENERATED WHITELIST -->
