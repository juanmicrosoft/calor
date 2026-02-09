---
layout: default
title: Effects & Contracts Enforcement
parent: Philosophy
nav_order: 3
---

# Effects & Contracts Enforcement

Why effects and contracts enforcement is only practical in coding agent languages - and why this changes everything.

---

## The 50-Year Struggle

Computer scientists have known since the 1970s how to make software more reliable:

- **Effect Systems**: Track side effects at compile time (Gifford & Lucassen, 1986)
- **Design by Contract**: Specify preconditions and postconditions (Meyer, 1986)
- **Dependent Types**: Encode invariants in the type system (Martin-Löf, 1972)

These techniques can **mathematically prove** that code behaves correctly. They catch entire categories of bugs before the code ever runs.

**So why isn't all software written this way?**

---

## The Human Bottleneck

Every attempt to bring verification to mainstream programming has hit the same wall: **human annotation burden**.

### Haskell's Effect System

```haskell
-- Haskell requires effect annotations everywhere
readFile :: FilePath -> IO String
writeFile :: FilePath -> String -> IO ()

-- Every function that uses IO must declare it
processConfig :: FilePath -> IO Config
processConfig path = do
    contents <- readFile path  -- Forces IO in type signature
    return (parseConfig contents)
```

**Result**: Haskell remains a niche language. Most programmers find the discipline too burdensome for everyday work.

### Eiffel's Design by Contract

```eiffel
-- Eiffel requires manual contract annotation
deposit (amount: INTEGER)
    require
        positive_amount: amount > 0
        account_open: is_open
    do
        balance := balance + amount
    ensure
        balance_increased: balance = old balance + amount
    end
```

**Result**: Eiffel never achieved mainstream adoption. The annotation overhead was too high for most development teams.

### Java's Checked Exceptions

```java
// Java forces you to declare every exception
public void processFile(String path) throws IOException, ParseException {
    String content = Files.readString(Path.of(path));  // throws IOException
    parse(content);  // throws ParseException
}
```

**Result**: Developers circumvent the system with `throws Exception` or catch-all blocks. The signal is lost.

### C# Code Contracts (2008-2016)

The most relevant historical parallel for Calor: Microsoft shipped Code Contracts as a first-party .NET library targeting the same ecosystem Calor compiles to.

```csharp
// System.Diagnostics.Contracts (2008-2016)
public int Withdraw(int amount)
{
    Contract.Requires(amount > 0);
    Contract.Requires(amount <= Balance);
    Contract.Ensures(Balance == Contract.OldValue(Balance) - amount);

    Balance -= amount;
    return Balance;
}
```

Code Contracts offered static checking (via cccheck), runtime enforcement, and documentation generation. It had Microsoft's full backing and integration into Visual Studio.

**Result**: Retired in 2016. The static checker was too slow for large codebases. The annotation burden was too high — developers had to manually write and maintain every contract. When faced with deadlines, teams stopped writing contracts, and the value proposition collapsed. The project was archived without a replacement.

**What Calor learned**: The verification tooling was sound; the failure was in who was expected to write the annotations. When agents generate contracts, the core problem that killed Code Contracts — annotation burden — disappears entirely.

### The Pattern is Clear

Every verification system that relies on human discipline has failed to achieve mainstream adoption. The annotation burden is simply too high for the productivity demands of commercial software development.

---

## The Agent Difference

Coding agents change this equation fundamentally:

| Human Developers | Coding Agents |
|:----------------|:--------------|
| Find annotation tedious | Generate annotations for free |
| Forget to update contracts | Maintain perfect consistency |
| Skip verification under time pressure | Never cut corners |
| Resist learning new syntax | Adapt instantly to any syntax |
| Trade safety for productivity | Safety IS productivity |

**When an agent writes code, the annotation cost is zero.**

This isn't incremental improvement. It's a phase transition that makes previously impractical techniques suddenly viable.

---

## What Calor Makes Possible

### Compile-Time Effect Verification

```
§F{f001:ProcessOrder:pub}
  §I{Order:order}
  §O{bool}
  §E{db:rw}                  // Declares: only database effects

  §C{SaveOrder} order       // OK: SaveOrder has db:rw effect
  §C{SendEmail} order       // ERROR: SendEmail has net:w effect
§/F{f001}                   //        not declared in §E{db:rw}
```

**The compiler catches this.** Not a linter warning. Not a code review comment. A hard error that blocks compilation.

In traditional languages, this bug ships to production. In Calor, it's impossible.

### Interprocedural Effect Analysis

Calor doesn't just check individual functions. It analyzes the entire call graph:

```
error Calor0410: Function 'ProcessOrder' uses effect 'net:w'
                 but does not declare it
  Call chain: ProcessOrder → NotifyCustomer → SendEmail → HttpClient.PostAsync
```

The compiler traces the effect violation through any depth of calls. You can't hide a side effect by burying it in helper functions.

### Effect Resolution Through .NET Interop

How does the compiler know that `HttpClient.PostAsync` has a `net:w` effect? Through **effect manifests** — `.calor-effects.json` files that declare effects for external .NET code:

```json
{
  "version": "1.0",
  "namespace": "System.Net.Http",
  "types": {
    "HttpClient": {
      "methods": {
        "GetAsync": { "effects": ["net:r"] },
        "PostAsync": { "effects": ["net:w"] },
        "SendAsync": { "effects": ["net:rw"] }
      },
      "defaultEffects": ["net:rw"]
    }
  }
}
```

Built-in manifests cover the .NET BCL. Project-local manifests handle NuGet packages. The resolution order is: specific method → type wildcard → `defaultEffects` → `namespaceDefaults` → unknown (compile error in strict mode).

This means effect verification extends beyond Calor code to the entire .NET ecosystem. See the [Effect Manifests guide](/calor/guides/effect-manifests/) for details.

### Runtime Contract Verification

```
§F{f001:Withdraw:pub}
  §I{i32:amount}
  §I{Account:account}
  §O{i32}
  §Q (> amount 0)                          // Precondition
  §Q (>= account.balance amount)           // Precondition
  §S (== account.balance (- old_balance amount))  // Postcondition
  // ...
§/F{f001}
```

Every contract becomes executable verification:

```csharp
// Generated C# code
public static int Withdraw(int amount, Account account)
{
    if (!(amount > 0))
        throw new ContractViolationException(
            "Precondition failed: amount > 0",
            functionId: "f001",
            kind: ContractKind.Requires,
            line: 5);

    if (!(account.balance >= amount))
        throw new ContractViolationException(
            "Precondition failed: account.balance >= amount",
            functionId: "f001",
            kind: ContractKind.Requires,
            line: 6);

    var old_balance = account.balance;

    // ... implementation ...

    if (!(account.balance == old_balance - amount))
        throw new ContractViolationException(
            "Postcondition failed",
            functionId: "f001",
            kind: ContractKind.Ensures,
            line: 7);

    return result;
}
```

### Static Contract Verification

Runtime checks catch violations when they happen. But with Z3, the compiler can go further — proving contracts correct at compile time:

```
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

```
// PROVEN: Postcondition statically verified: (result >= 0)
//   Path 1: x < 0 → result = 0 → 0 >= 0 ✓
//   Path 2: x >= 0 → result = x → x >= 0 ✓
```

Z3 proves both code paths satisfy the postcondition. The runtime check is elided — zero cost for verified correctness. When Z3 finds a counterexample instead, it reports the exact inputs that violate the contract.

[Learn more: Static Contract Verification](/calor/philosophy/static-verification/)

---

## The Virtuous Cycle

When agents both write and verify code, a powerful feedback loop emerges:

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  Agent generates code  ───►  Compiler verifies effects     │
│         ▲                           │                       │
│         │                           ▼                       │
│  Agent fixes violations  ◄───  Errors with call chains     │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

1. **Agent generates code** with effect declarations and contracts
2. **Compiler verifies** effects are properly declared — including through .NET interop via built-in manifests
3. **Z3 proves** contracts where possible, eliding runtime checks for proven contracts
4. **Errors include call chains** showing exactly where violations occur
5. **Agent fixes violations** with precise information
6. **Repeat until verified**

This cycle happens at machine speed. What would take a human team days of debugging completes in seconds.

---

## What Traditional Languages Cannot Do

### The Coverage Problem

In C#, Java, or Python, you can add contracts manually:

```csharp
public int Divide(int a, int b)
{
    Contract.Requires(b != 0);  // Easy to forget
    Contract.Ensures(Contract.Result<int>() * b == a);
    return a / b;
}
```

But who ensures every function has appropriate contracts? Code reviews catch some. Static analysis catches some. Most slip through.

**Calor's solution**: The agent generates contracts for every function. The compiler enforces effect declarations. There's no "forgot to add" because the agent never forgets.

### The Maintenance Problem

Even with contracts, they rot over time:

```csharp
// Original contract
Contract.Requires(amount > 0);

// 6 months later, someone adds a "free trial" feature
// But forgets to update the contract
if (isTrial) amount = 0;  // Now the contract is wrong
```

**Calor's solution**: When an agent modifies code, it updates all affected contracts. The contract and implementation evolve together, maintained by the same agent that understands both.

### The Escape Hatch Problem

Java's checked exceptions taught us what happens with mandatory annotations:

```java
// Developer circumvents the system
public void riskyMethod() throws Exception {  // "I give up"
    // ...
}

// Or worse
try {
    riskyMethod();
} catch (Exception e) {
    // swallow and pray
}
```

**Calor's solution**: Agents have no motivation to cheat. They don't feel annotation fatigue. They don't take shortcuts under deadline pressure. The verification system is used as designed.

---

## Real-World Impact

### Bug Categories Eliminated

| Bug Category | Traditional Detection | Calor Detection |
|:-------------|:---------------------|:----------------|
| Undeclared side effect | Code review (maybe) | Compile error |
| Missing null check | Runtime NPE | Contract violation |
| Invalid state transition | Integration test (if lucky) | Contract violation |
| Effect leaking through abstraction | Production incident | Compile error with call chain |
| Contract violation | Silent corruption | Immediate, traced exception |
| Unknown .NET library effect | Manual documentation lookup | Manifest resolution (compile error) |
| Contract violation provable at compile time | Unit test (if written) | Z3 proof at compile time |

### Development Velocity

Counter-intuitively, verification speeds up development:

1. **No debugging sessions** for contract violations - they're caught immediately
2. **No production incidents** from undeclared side effects
3. **No code archaeology** to understand what a function does - read the contracts
4. **No "works on my machine"** - effects are explicit and verifiable

---

## The Path Not Taken

For 50 years, language designers asked: *"How do we make verification palatable to humans?"*

The answers - optional annotations, gradual typing, configurable strictness - all failed to achieve widespread verified code.

Calor asks a different question: *"What if humans aren't the ones writing the code?"*

When agents write code:
- Annotation overhead disappears
- Verification becomes free
- Safety and productivity align

This is the verification opportunity that coding agent languages uniquely enable.

---

## Getting Started

To see effect and contract enforcement in action:

```bash
# Compile with enforcement (enabled by default)
calor compile myprogram.calr

# See effect violations with call chains
# error Calor0410: Function 'f001' uses effect 'console_write' but does not declare it

# Disable for migration (not recommended)
calor compile myprogram.calr --enforce-effects=false
```

---

## Next

- [Design Principles](/calor/philosophy/design-principles/) - The five principles behind Calor
- [Effects Reference](/calor/syntax-reference/effects/) - Complete effect syntax
- [Contracts Reference](/calor/syntax-reference/contracts/) - Complete contract syntax
- [Enforcement Details](/calor/effects-and-contracts-enforcement/) - Technical specification
