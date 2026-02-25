---
layout: default
title: Dependent Types Tutorial
parent: Guides
nav_order: 3
---

# Dependent Types Tutorial

This tutorial walks through refinement types in three parts: basic parameter validation, financial proof obligations, and the MCP-guided agent repair loop. Each part shows where Calor's type-level constraints go beyond what plain C# can express.

---

## Part 1 — API Parameter Validation

### The C# Problem

In C#, parameter constraints live in scattered runtime checks:

```csharp
public void ConfigureServer(int port, string hostname, int maxConnections)
{
    if (port < 1 || port > 65535)
        throw new ArgumentOutOfRangeException(nameof(port));
    if (string.IsNullOrEmpty(hostname))
        throw new ArgumentException("Hostname required", nameof(hostname));
    if (maxConnections <= 0)
        throw new ArgumentOutOfRangeException(nameof(maxConnections));

    // ... actual logic buried after validation
}
```

Every caller must remember to pass valid values. Every callee must remember to validate. Nothing in the type system enforces this — a `port` parameter typed `int` accepts any integer.

### Calor With Inline Refinements

In Calor, constraints are part of the parameter declaration:

```
§F{f001:ConfigureServer:pub}
  §I{i32:port} | (&& (>= # INT:1) (<= # INT:65535))
  §I{str:hostname} | (> (len #) INT:0)
  §I{i32:maxConnections} | (> # INT:0)
  §O{void}
  // ... actual logic, no validation boilerplate
§/F{f001}
```

### With Named Refinement Types

For reusable constraints, define named refinement types:

```
§M{m001:Server}

§RTYPE{r1:Port:i32} (&& (>= # INT:1) (<= # INT:65535))
§RTYPE{r2:NonEmpty:str} (> (len #) INT:0)
§RTYPE{r3:Positive:i32} (> # INT:0)

§F{f001:ConfigureServer:pub}
  §I{Port:port}
  §I{NonEmpty:hostname}
  §I{Positive:maxConnections}
  §O{void}
  // ...
§/F{f001}

§/M{m001}
```

Now `Port`, `NonEmpty`, and `Positive` are reusable across the entire module.

### What Happens in the Emitted C#

Refinement types are erased — the C# output uses base types with runtime guards for public functions:

```csharp
public void ConfigureServer(int port, string hostname, int maxConnections)
{
    // Boundary obligation guards (auto-generated)
    if (!(port >= 1 && port <= 65535))
        throw new InvalidOperationException("Obligation failed: port must satisfy Port");
    if (!(hostname.Length > 0))
        throw new InvalidOperationException("Obligation failed: hostname must satisfy NonEmpty");
    if (!(maxConnections > 0))
        throw new InvalidOperationException("Obligation failed: maxConnections must satisfy Positive");

    // ...
}
```

The key difference: in Calor the constraint is declared once in the type, and the compiler generates the guards. In C# you write them by hand in every function.

---

## Part 2 — Financial Calculations with Proof Obligations

### The Setup

A transfer function where:
- Amounts must be positive
- Balances must be non-negative
- After a transfer, the balance must remain non-negative

```
§M{m001:Banking}

§RTYPE{r1:PositiveAmount:i32} (> # INT:0)
§RTYPE{r2:NonNegBalance:i32} (>= # INT:0)

§F{f001:Transfer:pub}
  §I{NonNegBalance:balance}
  §I{PositiveAmount:amount}
  §O{i32}
  §Q (>= balance amount)                  // sufficient funds

  §B{newBalance:i32} (- balance amount)
  §PROOF{p1:balance-safe} (>= newBalance INT:0)

  §R newBalance
§/F{f001}

§/M{m001}
```

### Z3 Proves the Obligation

The obligation engine runs this verification:

1. **Assume**: `balance >= 0` (from `NonNegBalance`), `amount > 0` (from `PositiveAmount`), `balance >= amount` (precondition)
2. **Compute**: `newBalance = balance - amount`
3. **Check**: Is `newBalance >= 0`?

Z3 proves it: given `balance >= amount` and `amount > 0`, then `balance - amount >= 0`. The proof obligation is **discharged** — no runtime guard needed.

### When Z3 Finds a Counterexample

Remove the precondition:

```
§F{f002:UnsafeTransfer:pub}
  §I{NonNegBalance:balance}
  §I{PositiveAmount:amount}
  §O{i32}
  // No §Q — missing sufficient funds check!

  §B{newBalance:i32} (- balance amount)
  §PROOF{p1:balance-safe} (>= newBalance INT:0)

  §R newBalance
§/F{f002}
```

Z3 finds: `balance = 0, amount = 1 → newBalance = -1`. The obligation **fails** with a concrete counterexample. With the default policy, this is a compilation error.

### The Fix Cycle

The counterexample tells you exactly what's wrong: the function needs `(>= balance amount)` as a precondition. Add it, recompile, and the obligation is discharged.

---

## Part 3 — The MCP-Guided Agent Repair Loop

This is where refinement types and MCP tools combine. Instead of manually diagnosing obligation failures, an AI agent uses five specialized tools to navigate the repair loop with structured data.

### Starting Point

An agent has written code with unconstrained parameters:

```
§M{m001:Calculator}

§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}

§F{f002:IndexArray:pub}
  §I{i32:index}
  §I{i32:size}
  §O{i32}
  §R index
§/F{f002}

§/M{m001}
```

### Step 1: Discover Missing Refinements

The agent calls `calor_suggest_types`:

```json
{
  "source": "§M{m001:Calculator} ..."
}
```

Response:

```json
{
  "success": true,
  "suggestions": [
    {
      "function_id": "f001",
      "parameter_name": "b",
      "current_type": "i32",
      "suggested_predicate": "(!= # INT:0)",
      "reason": "used_as_divisor",
      "confidence": "high",
      "calor_syntax": "§I{i32:b} | (!= # INT:0)"
    },
    {
      "function_id": "f002",
      "parameter_name": "index",
      "current_type": "i32",
      "suggested_predicate": "(>= # INT:0)",
      "reason": "used_as_index",
      "confidence": "high",
      "calor_syntax": "§I{i32:index} | (>= # INT:0)"
    }
  ]
}
```

The tool analyzed the function bodies and detected that `b` is used as a divisor (must be non-zero) and `index` is used as an array index (must be non-negative).

### Step 2: Add Refinements and Check Obligations

The agent applies the suggestions and calls `calor_obligations`:

```json
{
  "source": "§M{m001:Calculator}\n§F{f001:Divide:pub}\n  §I{i32:a}\n  §I{i32:b} | (!= # INT:0)\n  §O{i32}\n  §R (/ a b)\n§/F{f001}\n..."
}
```

Response:

```json
{
  "success": true,
  "summary": {
    "total": 2,
    "discharged": 0,
    "failed": 0,
    "boundary": 2
  },
  "obligations": [
    {
      "id": "obl_0",
      "kind": "RefinementEntry",
      "function_id": "f001",
      "description": "b must satisfy (!= # INT:0)",
      "status": "Boundary"
    },
    {
      "id": "obl_1",
      "kind": "RefinementEntry",
      "function_id": "f002",
      "description": "index must satisfy (>= # INT:0)",
      "status": "Boundary"
    }
  ]
}
```

Both are `Boundary` — the functions are public, so the compiler can't verify what callers pass. Runtime guards will be emitted automatically.

### Step 3: Discover Guards for Failed Obligations

If the agent had private functions with failed obligations, it would call `calor_discover_guards`:

```json
{
  "source": "...",
  "obligation_id": "obl_0"
}
```

Response:

```json
{
  "success": true,
  "guards": [
    {
      "obligation_id": "obl_0",
      "description": "Add precondition requiring b != 0",
      "calor_expression": "§Q (!= b INT:0)",
      "insertion_kind": "precondition",
      "confidence": "high",
      "validated": true
    },
    {
      "obligation_id": "obl_0",
      "description": "Add if-guard returning error",
      "calor_expression": "§IF{g1} (== b INT:0) → §R (err \"division by zero\")",
      "insertion_kind": "if_guard",
      "confidence": "medium"
    }
  ]
}
```

Guards are ranked by confidence. The `validated: true` flag means Z3 confirmed that adding this guard would discharge the obligation.

### Step 4: The All-in-One Repair Tool

For the full repair loop in a single call, the agent uses `calor_diagnose_refinement`:

```json
{
  "source": "...",
  "policy": "default"
}
```

Response:

```json
{
  "success": true,
  "policy": "default",
  "summary": {
    "total": 2,
    "discharged": 0,
    "boundary": 2
  },
  "patches": [
    {
      "obligation_id": "obl_0",
      "obligation_status": "Boundary",
      "policy_action": "AlwaysGuard",
      "patch_kind": "precondition",
      "calor_code": "§Q (!= b INT:0)",
      "description": "Add precondition for b",
      "confidence": "high",
      "discharges_obligations": ["obl_0"]
    }
  ]
}
```

The `patches` array gives the agent exactly what to insert and where, with obligation IDs showing which problems each patch resolves.

### Why This Matters for Agent Workflows

The traditional agent approach to fixing a runtime error:

1. Read error message → guess the fix → try it → compile → read new error → repeat

The MCP-guided approach:

1. `calor_suggest_types` → know exactly which parameters need constraints
2. `calor_obligations` → see all obligation statuses with counterexamples
3. `calor_discover_guards` → get Z3-validated fix suggestions ranked by confidence
4. `calor_diagnose_refinement` → get the full repair plan in one call

The tools **prune the agent's search space**. Instead of guessing, the agent gets structured data about what's wrong and exactly how to fix it.

---

## MCP Tool Reference

| Tool | Purpose | When to Use |
|:-----|:--------|:------------|
| `calor_suggest_types` | Detect parameters needing refinements | First — before adding any refinements |
| `calor_obligations` | Generate and verify all obligations | After adding refinements — see what's proven |
| `calor_discover_guards` | Find guards for failed obligations | When obligations fail — get fix candidates |
| `calor_suggest_fixes` | Get ranked fix strategies | When you need alternative approaches |
| `calor_diagnose_refinement` | Full repair loop in one call | When you want everything at once |

---

## See Also

- [Refinement Types Reference](/calor/syntax-reference/refinement-types/) — Complete syntax reference
- [Contracts](/calor/syntax-reference/contracts/) — Preconditions and postconditions
- [Static Verification](/calor/philosophy/static-verification/) — Z3-based verification
- [MCP Server](/calor/cli/mcp/) — All MCP tools
