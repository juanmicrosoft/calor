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

**Binary operators** (operand widths normalized by sign/zero-extension, `:763–788`):
`+  -  *  /  %  ==  !=  <  <=  >  >=  &&  ||  &  |  ^  <<  >>`
Division, modulo, comparisons, and right-shift are signedness-aware (`ShouldUseUnsignedComparison :718–740`): both-unsigned → unsigned ops; mixed → unsigned only when the signed operand is a provably non-negative literal, else signed.

**Unary operators:** `!` (bool), unary `-` (integer).

**Conditional:** `(cond ? a : b)` → Z3 ITE (`:420–430`).

**Quantifiers:** `forall` / `exists` with integer-typed bound variables; **implication** `(-> p q)` (`:454–524`).

**Self-reference `#`** in refinement predicates (`:332–342`).

**Arrays:** element access **only on a simple variable base** (no computed/nested/method-returned arrays, `:534`); array length (simple variable base only). An array first seen at an access site defaults to i32 elements (see divergence D6).

**User-type fields:** `obj.Field` and dot-paths, modeled as uninterpreted functions; result sorts come from the user-type registry when supplied, else default to i32/BitVec32 (see divergence D7).

**String operations (exhaustive):** `Length`, `Contains`, `StartsWith`, `EndsWith`, `Equals`, `IsNullOrEmpty`, `IndexOf` (2- and 3-arg), `Substring` (3-arg), `SubstringFrom`, `Concat`, `Replace` (first occurrence). Everything else is out (see §3).

## 3. Explicitly NOT modeled (→ `Unsupported`, runtime check retained)

1. **Function and method calls of any kind** in contracts (`CallExpressionNode → null`, `:245`). Calls-in-contracts via callee summaries is Phase 2b work and will carry `Assumed` status, never `Proven`.
2. **All floating-point** types and literals.
3. **String operations:** `ToUpper`, `ToLower`, `Trim`, `TrimStart`, `TrimEnd`, `PadLeft`, `PadRight`, `Split`, `Join`, `Format`, `ToString`, `IsNullOrWhiteSpace`, all Regex operations (`:1353–1362`).
4. **`StringComparison` modes:** accepted syntactically, **ignored semantically** — verification is ordinal-only; non-ordinal modes add a warning but the proof proceeds (`:863–870`). Treat culture-sensitive string contracts as unverified.
5. **Computed array bases** (method returns, nested accesses) for element access or length.
6. **`object`/`dynamic`, delegate types** as variables.
7. **Generic-typed values** (including `Option<T>`/`Result<T,E>`-typed ones) — they fall to uninterpreted sorts at best; contracts over their *contents* are not modeled. Constrains Phase 0 fixture authoring.
8. Anything not listed in §2 (default case `:246`).

## 4. Known semantic divergences from C# (tracked as defects per strategy §5.2 rule 4)

| # | Divergence | Consequence |
|---|---|---|
| D1 | **No narrow-type promotion** (`:33–39`): `byte + byte` wraps at 8 bits; C# promotes to `int` (400 stays 400) | False positives *and* negatives on `byte`/`short` arithmetic contracts |
| D2 | **Integer literals always signed 32-bit** (`:223`); out-of-range values truncate | Long-range contracts mis-modeled |
| D3 | **Z3 strings cannot be null** (`:44–48`): `IsNullOrEmpty` tests length==0 only | Null-vs-empty indistinguishable |
| D4 | **Ordinal-only string comparison** (§3 item 4) | Culture-sensitive contracts unverified without loud failure |
| D5 | Contract `§S` holds **only on normal return**; exceptional paths unverified (strategy 2b item 6) | Exception-heavy code has weaker guarantees than the word "Proven" suggests |
| D6 | Arrays first seen at an access site default to **i32 elements** (`:544–551`) | Element-width mismatch possible |
| D7 | User-type fields default to **i32** without a registry (`:1185`) | Field-width/signedness mismatch possible |

## 5. The second translator (bug-pattern checkers) — differences

`BoundExpressionTranslator` (in `Analysis/BugPatterns/Patterns/DivisionByZeroChecker.cs:503–694`) backs the div-by-zero/overflow/index checkers. It is **narrower and signed-only**:

- **Adds:** math functions via ITE — `abs`, `min`, `max`, `clamp`, `sign` (both `math.x` and bare names).
- **Removes vs the primary:** strings, arrays, field access, quantifiers, implication, conditional, self-ref, bitwise/shift operators, all type aliases (`System.*`, `intNN`), width normalization.
- **Signed-only everywhere** — unsigned types are mis-modeled by its comparisons and div/mod.

Do not assume checker findings and contract proofs share a model; they don't.

## 6. Maintenance rule

Any change to `ContractTranslator`'s accepted forms MUST update this document in the same PR. When Phase 2b lands the positive-whitelist rearchitecture, this document becomes generated output and the hand-maintained version is retired.
