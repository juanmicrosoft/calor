# Verifier ↔ Generated Runtime Differential (F-4)

- **Result:** PASS
- **Whitelist hash:** `6dbdc9c0e1ec122ec1110013cb023ac51109ae5452b55ad00a0b782b471ec463`
- **Mismatches:** 0
- **Forms solver-handled:** 65/65 (100.00%)
- **Forms eliding:** 40/65 (61.54%)
- **Cartesian cells solver-handled:** 1170/1170 (100.00%)
- **Cartesian cells registered:** 1170
- **Generated cases:** 1170 (3 positions × depths 1–3 × 2 polarities per applicable form)

## Typed outcomes

| Outcome | Cases |
|---|---:|
| `assumed` | 150 |
| `proven` | 435 |
| `refuted` | 585 |

## Coverage by category

| Category | Whitelisted | Applicable | Solver-handled | Eliding | Mismatches |
|---|---:|---:|---:|---:|---:|
| `array-element-type` | 8 | 8 | 8 | 0 | 0 |
| `binary-operator` | 18 | 18 | 18 | 18 | 0 |
| `expression-kind` | 15 | 15 | 15 | 10 | 0 |
| `quantifier-bound-variable-type` | 1 | 1 | 1 | 1 | 0 |
| `scalar-type` | 10 | 10 | 10 | 9 | 0 |
| `string-comparison-mode` | 1 | 1 | 1 | 0 | 0 |
| `string-operation` | 10 | 10 | 10 | 0 | 0 |
| `unary-operator` | 2 | 2 | 2 | 2 | 0 |

## Encoding notes

- `expression-kind:SelfRefNode` — The harness binds '#' to an i32 parameter named '__self__' before translation; the emitter's existing SelfRef lowering targets the same generated parameter. This exercises the modeled refinement meaning without claiming ordinary source-level §Q/§S/§PROOF acceptance of an unbound '#'.
- `expression-kind:FieldAccessNode` — The production contract pass and obligation solver derive field types from module class declarations. Probe.Value is a u8 accessor checked against both bounds 0..255 and executed at 255; mapping it as i8 violates the lower bound and mapping it as i32 violates both finite bounds. Proofs remain explicitly Assumed under the nullable-reference model.
- `scalar-type:i64` — The translator models integer literals only through signed i32. The i64 row therefore uses signedness at -1 plus an i32-overflow boundary witness (2 * Int32.MaxValue) rather than claiming coverage of Int64.MinValue/MaxValue literals.
- `scalar-type:u32` — The u32 row combines non-negativity with the wrap boundary of 3 * Int32.MaxValue; this distinguishes 32-bit unsigned arithmetic from u64 without requiring an out-of-model UInt32.MaxValue literal.
- `scalar-type:u64` — The u64 row combines non-negativity with the non-wrapping result of 3 * Int32.MaxValue; it does not claim direct UInt64.MaxValue literal coverage.
- `array-element-types` — Integer array rows apply the same per-type boundary predicates to values[0]. Runtime uses a non-null one-element array with the matching deterministic witness; proofs are therefore conditional only on the production nullable-reference-model assumption.
- `scalar-type:str` — The string row proves non-negative length using the non-null ASCII runtime witness 'ascii'. The solver result remains explicitly conditional on the production string-model assumption; this does not claim nullable or non-ASCII equivalence.
- `string-comparison-mode:Ordinal` — The ordinal row uses the zero-width-joiner witness 'abc'.StartsWith('\u200dabc'): false under Ordinal but true under en-US CurrentCulture. Provable and refutable cells use opposite polarities of that same predicate. Generated runtime is executed under en-US with ambient culture restored.

## Explicit Assumed allowances

`Assumed` is accepted only for provable cells whose form lists the exact production assumption set below. Refutable cells must always be `Refuted`.

- `scalar-type:str` — string-model
- `array-element-type:i8` — reference-model
- `array-element-type:i16` — reference-model
- `array-element-type:i32` — reference-model
- `array-element-type:i64` — reference-model
- `array-element-type:u8` — reference-model
- `array-element-type:u16` — reference-model
- `array-element-type:u32` — reference-model
- `array-element-type:u64` — reference-model
- `expression-kind:StringLiteralNode` — string-model
- `expression-kind:ArrayAccessNode` — reference-model
- `expression-kind:ArrayLengthNode` — reference-model
- `expression-kind:FieldAccessNode` — reference-model
- `expression-kind:StringOperationNode` — string-model
- `string-operation:Length` — string-model
- `string-operation:Contains` — string-model
- `string-operation:StartsWith` — string-model
- `string-operation:EndsWith` — string-model
- `string-operation:Equals` — string-model
- `string-operation:IsNullOrEmpty` — string-model
- `string-operation:IndexOf` — string-model
- `string-operation:Substring` — string-model
- `string-operation:SubstringFrom` — string-model
- `string-operation:Concat` — string-model
- `string-comparison-mode:Ordinal` — string-model

## Per-form coverage

| Form | Solver-handled | Cases | Pre | Post | Obligation | Elides | Mismatches |
|---|:---:|---:|---:|---:|---:|:---:|---:|
| `scalar-type:i8` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:i16` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:i32` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:i64` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:u8` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:u16` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:u32` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:u64` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:bool` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:str` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:i8` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:i16` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:i32` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:i64` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:u8` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:u16` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:u32` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:u64` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:IntLiteralNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:BoolLiteralNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:StringLiteralNode` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:ReferenceNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:BinaryOperationNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:UnaryOperationNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ConditionalExpressionNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ForallExpressionNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ExistsExpressionNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ImplicationExpressionNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ArrayAccessNode` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:ArrayLengthNode` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:FieldAccessNode` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:StringOperationNode` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:SelfRefNode` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Add` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Subtract` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Multiply` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Divide` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Modulo` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Equal` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:NotEqual` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:LessThan` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:LessOrEqual` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:GreaterThan` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:GreaterOrEqual` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:And` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Or` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:BitwiseAnd` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:BitwiseOr` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:BitwiseXor` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:LeftShift` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:RightShift` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `unary-operator:Not` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `unary-operator:Negate` | yes | 18 | 6 | 6 | 6 | yes | 0 |
| `string-operation:Length` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:Contains` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:StartsWith` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:EndsWith` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:Equals` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:IsNullOrEmpty` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:IndexOf` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:Substring` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:SubstringFrom` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:Concat` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `string-comparison-mode:Ordinal` | yes | 18 | 6 | 6 | 6 | no | 0 |
| `quantifier-bound-variable-type:declarable-alias` | yes | 18 | 6 | 6 | 6 | yes | 0 |

## Fail-safe controls

| Scenario | Channel | Typed status | Guard retained | Runtime | Result |
|---|---|---|:---:|---|:---:|
| unsupported | postcondition | `unsupported` | yes | `guardfailed` | pass |
| unsupported | obligation | `unsupported` | yes | `guardfailed` | pass |
| timeout | postcondition | `timeout` | yes | `guardfailed` | pass |
| timeout | obligation | `timeout` | yes | `guardfailed` | pass |
| solver-error | postcondition | `unknown` | yes | `guardfailed` | pass |
| solver-error | obligation | `unknown` | yes | `guardfailed` | pass |
| unavailable | postcondition | `unavailable` | yes | `guardfailed` | pass |
| unavailable | obligation | `unavailable` | yes | `guardfailed` | pass |
| assumed | postcondition | `assumed` | yes | `guardfailed` | pass |
| assumed | obligation | `assumed` | yes | `guardfailed` | pass |

## Oracle

Every case is emitted twice. The runtime assembly is compiled from the guard-forced emission (`ElideProvenGuards = false`); the opt-in emission is inspected separately to measure actual postcondition/obligation elision. `proven`/`discharged` must execute without a guard failure, `refuted`/`failed` must fire the generated guard, and every non-decisive status must retain the guard. The generator also requires the declared target form to occur in every base expression and rejects vacuous proofs.

