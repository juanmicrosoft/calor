# Verifier ↔ Generated Runtime Differential (F-4)

- **Result:** PASS
- **Whitelist hash:** `6dbdc9c0e1ec122ec1110013cb023ac51109ae5452b55ad00a0b782b471ec463`
- **Mismatches:** 0
- **Forms covered:** 65/65 (100.00%)
- **Forms eliding:** 40/65 (61.54%)
- **Cartesian cells executed:** 1170/1170 (100.00%)
- **Generated cases:** 1170 (3 positions × depths 1–3 × 2 polarities per applicable form)

## Typed outcomes

| Outcome | Cases |
|---|---:|
| `assumed` | 144 |
| `proven` | 432 |
| `refuted` | 576 |
| `unsupported` | 18 |

## Coverage by category

| Category | Whitelisted | Applicable | Covered | Eliding | Mismatches |
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

## Per-form coverage

| Form | Cases | Pre | Post | Obligation | Elides | Mismatches |
|---|---:|---:|---:|---:|:---:|---:|
| `scalar-type:i8` | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:i16` | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:i32` | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:i64` | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:u8` | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:u16` | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:u32` | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:u64` | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:bool` | 18 | 6 | 6 | 6 | yes | 0 |
| `scalar-type:str` | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:i8` | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:i16` | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:i32` | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:i64` | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:u8` | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:u16` | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:u32` | 18 | 6 | 6 | 6 | no | 0 |
| `array-element-type:u64` | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:IntLiteralNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:BoolLiteralNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:StringLiteralNode` | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:ReferenceNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:BinaryOperationNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:UnaryOperationNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ConditionalExpressionNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ForallExpressionNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ExistsExpressionNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ImplicationExpressionNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `expression-kind:ArrayAccessNode` | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:ArrayLengthNode` | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:FieldAccessNode` | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:StringOperationNode` | 18 | 6 | 6 | 6 | no | 0 |
| `expression-kind:SelfRefNode` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Add` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Subtract` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Multiply` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Divide` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Modulo` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Equal` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:NotEqual` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:LessThan` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:LessOrEqual` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:GreaterThan` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:GreaterOrEqual` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:And` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:Or` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:BitwiseAnd` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:BitwiseOr` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:BitwiseXor` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:LeftShift` | 18 | 6 | 6 | 6 | yes | 0 |
| `binary-operator:RightShift` | 18 | 6 | 6 | 6 | yes | 0 |
| `unary-operator:Not` | 18 | 6 | 6 | 6 | yes | 0 |
| `unary-operator:Negate` | 18 | 6 | 6 | 6 | yes | 0 |
| `string-operation:Length` | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:Contains` | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:StartsWith` | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:EndsWith` | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:Equals` | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:IsNullOrEmpty` | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:IndexOf` | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:Substring` | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:SubstringFrom` | 18 | 6 | 6 | 6 | no | 0 |
| `string-operation:Concat` | 18 | 6 | 6 | 6 | no | 0 |
| `string-comparison-mode:Ordinal` | 18 | 6 | 6 | 6 | no | 0 |
| `quantifier-bound-variable-type:declarable-alias` | 18 | 6 | 6 | 6 | yes | 0 |

## Fail-safe controls

| Channel | Typed status | Guard retained | Runtime | Result |
|---|---|:---:|---|:---:|
| postcondition | `unsupported` | yes | `guardfailed` | pass |
| obligation | `unsupported` | yes | `guardfailed` | pass |
| postcondition | `timeout` | yes | `guardfailed` | pass |
| obligation | `timeout` | yes | `guardfailed` | pass |
| postcondition | `unknown` | yes | `guardfailed` | pass |
| obligation | `unknown` | yes | `guardfailed` | pass |
| postcondition | `unavailable` | yes | `guardfailed` | pass |
| obligation | `unavailable` | yes | `guardfailed` | pass |
| postcondition | `assumed` | yes | `guardfailed` | pass |
| obligation | `assumed` | yes | `guardfailed` | pass |

## Oracle

Every case is emitted twice. The runtime assembly is compiled from the guard-forced emission (`ElideProvenGuards = false`); the opt-in emission is inspected separately to measure actual postcondition/obligation elision. `proven`/`discharged` must execute without a guard failure, `refuted`/`failed` must fire the generated guard, and every non-decisive status must retain the guard. The generator also requires the declared target form to occur in every base expression and rejects vacuous proofs.

