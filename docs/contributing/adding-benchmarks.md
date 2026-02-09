---
layout: default
title: Adding Benchmarks
parent: Contributing
nav_order: 2
---

# Adding Benchmarks

The evaluation framework relies on benchmark programs to measure Calor vs C#. This guide explains how to add new benchmarks.

---

## Benchmark Requirements

Each benchmark needs:

1. **Calor source file** - The Calor implementation
2. **C# source file** - Semantically equivalent C# implementation
3. **Metadata** - Description and expected behavior
4. **Both must compile** - 100% compilation success required

---

## Directory Structure

```
tests/Calor.Evaluation/Benchmarks/
├── HelloWorld/
│   ├── hello.calr
│   ├── hello.cs
│   └── metadata.json
├── FizzBuzz/
│   ├── fizzbuzz.calr
│   ├── fizzbuzz.cs
│   └── metadata.json
└── YourNewBenchmark/
    ├── program.calr
    ├── program.cs
    └── metadata.json
```

---

## Creating a Benchmark

### Step 1: Create Directory

```bash
mkdir tests/Calor.Evaluation/Benchmarks/YourBenchmark
cd tests/Calor.Evaluation/Benchmarks/YourBenchmark
```

### Step 2: Write Calor Code

Create `program.calr`:

```
§M{m001:YourModule}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  // Your implementation
§/F{f001}
§/M{m001}
```

**Guidelines:**
- Use Lisp-style expressions for operations
- Include all required IDs
- Declare effects explicitly
- Add contracts where meaningful
- Ensure it compiles

### Step 3: Write Equivalent C#

Create `program.cs`:

```csharp
namespace YourModule
{
    public static class Program
    {
        public static void Main()
        {
            // Equivalent implementation
        }
    }
}
```

**Guidelines:**
- Match the Calor logic exactly
- Include equivalent contracts if Calor has them (using Debug.Assert or exceptions)
- Keep structure as parallel as possible

### Step 4: Add Metadata

Create `metadata.json`:

```json
{
  "id": "your-benchmark",
  "name": "Your Benchmark Name",
  "description": "What this benchmark tests",
  "category": "category-name",
  "complexity": "simple|medium|complex",
  "features": ["loops", "contracts", "effects"],
  "expectedOutput": "Expected console output if any"
}
```

### Step 5: Verify

```bash
# Compile Calor
dotnet run --project src/Calor.Compiler -- \
  --input tests/Calor.Evaluation/Benchmarks/YourBenchmark/program.calr \
  --output /tmp/test.g.cs

# Verify C# compiles
dotnet build tests/Calor.Evaluation/Benchmarks/YourBenchmark/program.cs

# Run evaluation
dotnet run --project tests/Calor.Evaluation -- --output report.json
```

---

## Benchmark Categories

| Category | Description | Examples |
|:---------|:------------|:---------|
| `basic` | Simple programs | Hello World, Print Numbers |
| `loops` | Iteration patterns | FizzBuzz, Sum 1-N |
| `conditionals` | Branching logic | Min/Max, Absolute Value |
| `contracts` | Preconditions/postconditions | Safe Divide, Factorial |
| `effects` | Side effect patterns | File I/O, Network |
| `types` | Type system features | Option, Result |
| `complex` | Multi-feature programs | Full applications |

---

## Example: Factorial Benchmark

### Calor (`factorial.calr`)

```
§M{m001:Math}
§F{f001:Factorial:pub}
  §I{i32:n}
  §O{i32}
  §Q (>= n 0)
  §Q (<= n 12)
  §S (>= result 1)
  §IF{if1} (<= n 1) → §R 1
  §EL → §R (* n §C{Factorial} §A (- n 1) §/C)
  §/I{if1}
§/F{f001}

§F{f002:Main:pub}
  §O{void}
  §E{cw}
  §P §C{Factorial} §A 5 §/C
§/F{f002}
§/M{m001}
```

### C# (`factorial.cs`)

```csharp
using System.Diagnostics;

namespace Math
{
    public static class Program
    {
        public static int Factorial(int n)
        {
            Debug.Assert(n >= 0, "n must be non-negative");
            Debug.Assert(n <= 12, "n must be <= 12 to avoid overflow");

            if (n <= 1) return 1;
            var result = n * Factorial(n - 1);

            Debug.Assert(result >= 1, "result must be >= 1");
            return result;
        }

        public static void Main()
        {
            Console.WriteLine(Factorial(5));
        }
    }
}
```

### Metadata (`metadata.json`)

```json
{
  "id": "factorial",
  "name": "Factorial",
  "description": "Recursive factorial with contracts",
  "category": "contracts",
  "complexity": "medium",
  "features": ["recursion", "contracts", "conditionals"],
  "expectedOutput": "120"
}
```

---

## Semantic Equivalence

Both implementations must be semantically equivalent:

### Same Logic
- Same algorithm
- Same control flow structure
- Same edge case handling

### Same Contracts
- If Calor has `§Q`, C# should have equivalent check
- If Calor has `§S`, C# should have equivalent assertion

### Same Effects
- If Calor has `§E{cw}`, C# should write to console
- If Calor has `§E{fs:r}`, C# should read files

---

## Quality Checklist

Before submitting:

- [ ] Calor compiles without errors
- [ ] C# compiles without errors
- [ ] Both produce identical output (if applicable)
- [ ] Uses Lisp-style expressions
- [ ] IDs are unique and meaningful
- [ ] Effects are declared
- [ ] Contracts are equivalent
- [ ] Metadata is complete
- [ ] Code is clean and readable

---

## E2E Test Scenarios

For simpler test cases, you can add to the E2E test suite instead:

```
tests/E2E/scenarios/
├── 01_hello_world/
│   ├── input.calr
│   └── verify.sh
├── 02_fizzbuzz/
│   ├── input.calr
│   └── verify.sh
└── XX_your_test/
    ├── input.calr
    └── verify.sh
```

### E2E Verification Script

```bash
#!/usr/bin/env bash
SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Check generated C# contains expected patterns
grep -q "Console.WriteLine" "$OUTPUT_FILE" || exit 1
grep -q "namespace" "$OUTPUT_FILE" || exit 1
```

---

## Getting Help

- Check existing benchmarks for examples
- Open an issue if you have questions
- Ask for review on your PR

---

## Next

- [Development Setup](/calor/contributing/development-setup/) - Set up your environment
- [Contributing Overview](/calor/contributing/) - Other ways to contribute
