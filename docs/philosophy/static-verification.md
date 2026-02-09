---
layout: default
title: Static Contract Verification
parent: Philosophy
nav_order: 6
permalink: /philosophy/static-verification/
---

# Static Contract Verification with Z3

Every contract in Calor gets a runtime check. But with Z3, the compiler can go further: prove that a contract is always satisfied, or find a concrete counterexample showing exactly when it fails. Proven contracts have their runtime checks removed — zero cost for verified correctness.

---

## Overview

Consider a function with multiple code paths:

```calor
§F{f004:ClampPositive:pub}
  §I{i32:x}
  §O{i32}
  §S (>= result 0)
  §IF{if001} (< x 0)
    §R 0
  §ELSE{if001}
    §R x
  §/IF{if001}
§/F{f004}
```

With `--verify`, the compiler uses Z3 to analyze both branches:
- **Path 1:** `x < 0` → returns `0` → `0 >= 0` ✓
- **Path 2:** `x >= 0` → returns `x` → `x >= 0` ✓

Z3 proves the postcondition holds on all paths. The runtime check is elided.

For simpler cases, verification is even more straightforward:

```calor
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x 0)
  §S (>= result 0)
  §R (* x x)
§/F{f001}
```

The square of a non-negative number is always non-negative — Z3 proves this immediately.

---

## Enabling Static Verification

Use the `--verify` flag when compiling:

```bash
calor -i MyModule.calr -o MyModule.g.cs --verify
```

---

## Verification Spectrum

Contracts fall into four categories after verification:

### Proven
The contract is mathematically proven to always hold. The runtime check can be safely elided.

```
// PROVEN: Postcondition statically verified: (result >= 0)
```

### Unproven
Z3 couldn't determine if the contract always holds (timeout, complexity limit, or non-linear arithmetic). The runtime check is preserved.

### Disproven
Z3 found a counterexample showing the contract can be violated. A warning is emitted with the counterexample, and the runtime check is preserved.

```calor
§F{f005:BadDecrement:pub}
  §I{i32:x}
  §O{i32}
  §S (>= result 0)
  §R (- x 1)
§/F{f005}
```

```
warning Calor0702: Postcondition may be violated in function 'BadDecrement'.
  Contract: (>= result 0)
  Counterexample: x = 0
  Result: -1
```

Z3 found that when `x = 0`, the result is `-1`, which violates the postcondition. This is a real bug caught at compile time.

### Unsupported
The contract contains constructs Z3 doesn't support (function calls, strings, floating-point). The runtime check is silently preserved.

---

## Supported Constructs

Z3 verification supports:

| Calor | Z3 | Description |
|:------|:---|:------------|
| `(+ a b)` | `a + b` | Addition |
| `(- a b)` | `a - b` | Subtraction |
| `(* a b)` | `a * b` | Multiplication |
| `(/ a b)` | `a div b` | Division |
| `(% a b)` | `a mod b` | Modulo |
| `(== a b)` | `a = b` | Equality |
| `(!= a b)` | `a ≠ b` | Inequality |
| `(< a b)` | `a < b` | Less than |
| `(<= a b)` | `a ≤ b` | Less or equal |
| `(> a b)` | `a > b` | Greater than |
| `(>= a b)` | `a ≥ b` | Greater or equal |
| `(and p q)` | `p ∧ q` | Logical and |
| `(or p q)` | `p ∨ q` | Logical or |
| `(not p)` | `¬p` | Logical not |

**Supported types:** `i32`, `i64`, `bool`

**Unsupported:** Function calls, strings, floating-point, arrays, objects

---

## How It Works

The verification process for postconditions:

1. **Declare** all parameters as symbolic Z3 variables
2. **Assume** all preconditions hold
3. **Assert** the negation of the postcondition
4. **Check** satisfiability:
   - **UNSAT**: No counterexample exists → Proven
   - **SAT**: Found counterexample → Disproven
   - **UNKNOWN**: Timeout or too complex → Unproven

This approach proves correctness by showing there's no way for the contract to be violated when preconditions are met.

---

## Timeouts

Z3 verification has a 5-second timeout per function. Complex contracts with non-linear arithmetic may timeout and be marked as "Unproven".

---

## Best Practices

### Keep Contracts Simple
Simple arithmetic and comparison contracts verify quickly:

```calor
§Q (>= x 0)
§S (>= result 0)
```

### Separate Concerns
Split complex contracts into multiple simpler ones:

```calor
; Instead of:
§S (and (>= result 0) (< result 100))

; Use:
§S (>= result 0)
§S (< result 100)
```

### Accept Unproven Contracts
Not all contracts can be statically proven. An "Unproven" result doesn't mean the code is wrong—it means Z3 couldn't prove it within time limits.

### Agent Guidance

When generating Calor code:
- **Always add contracts** — they're free for agents to generate and provide verification value
- **Prefer simple arithmetic** that Z3 can verify (comparisons, linear arithmetic)
- **Use `--verify` in CI** — catch disproven contracts before merge
- **Treat Disproven as bugs, not warnings** — Z3 found a concrete counterexample; the contract or implementation needs fixing

---

## Contracts and Effects

Contracts and effects are independent verification dimensions that reinforce each other. Effects verify *what* a function touches (database, network, filesystem). Contracts verify *how* it behaves (preconditions, postconditions). Together, they provide both behavioral and side-effect guarantees — a function that is proven correct by Z3 *and* has verified effects gives strong assurance about its behavior.

---

## Comparison with Runtime-Only Enforcement

| Aspect | Runtime Only | Static + Runtime |
|:-------|:-------------|:-----------------|
| Verification cost | 0 | Compile time |
| Runtime cost | Always | Reduced for proven |
| Bug detection | At runtime | At compile time |
| When bugs found | At runtime (test execution) | At compile time (counterexample) |
| Coverage | All contracts | Supported constructs |

Static verification complements runtime checking—it doesn't replace it. Unproven and unsupported contracts still have runtime checks.

---

## Verification Boundaries

Z3 handles the most common contract patterns. The following fall back to runtime checking:

- **Function calls in contracts**: `§S (> (strlen s) 0)` — unsupported, requires inter-procedural analysis
- **Floating-point arithmetic**: Z3's floating-point theory is too complex for practical verification timeouts
- **Array operations and loop invariants**: Not currently modeled
- **Integer overflow**: Z3 uses arbitrary-precision integers, so it doesn't model 32-bit overflow. A contract proven correct in Z3 could theoretically fail at runtime due to overflow

These boundaries are inherent to SMT solving, not limitations of the Calor implementation. Contracts in these categories still get full runtime enforcement.

---

## Z3 Installation

Z3 is bundled with the Calor compiler via the `Microsoft.Z3` NuGet package. No separate installation is required.

If Z3 native libraries are missing on your platform, the compiler will:
1. Log an informational message
2. Skip static verification
3. Continue compilation normally with all runtime checks

---

## See Also

- [Contracts in Calor](/calor/language/contracts/)
- [Compile Command](/calor/cli/compile/)
- [Z3 Prover](https://github.com/Z3Prover/z3)
