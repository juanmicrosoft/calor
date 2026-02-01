---
layout: default
title: Adding OPAL to Existing Projects
nav_order: 7
permalink: /guides/adding-opal-to-existing-projects/
---

# Adding OPAL to an Existing C# Project

This guide walks you through adding OPAL to an existing C# project, from identifying migration candidates to building with mixed OPAL/C# code.

---

## Overview

Adding OPAL to an existing project is a gradual process:

1. **Analyze** - Find files that benefit most from OPAL's features
2. **Initialize** - Set up tooling and MSBuild integration
3. **Convert** - Migrate files one at a time
4. **Build** - OPAL compiles automatically with your project

You don't need to convert everything at once. OPAL and C# coexist seamlessly.

---

## Prerequisites

- .NET 8.0 SDK or later
- `opalc` global tool installed: `dotnet tool install -g opalc`
- An existing C# project

---

## Step 1: Find Migration Candidates

Use `opalc analyze` to score your C# files for OPAL migration potential:

```bash
# Analyze your source directory
opalc analyze ./src

# Show top 10 candidates with detailed scores
opalc analyze ./src --top 10 --verbose
```

### Understanding the Output

```
=== OPAL Migration Analysis ===

Analyzed: 42 files
Average Score: 34.2/100

Top 10 Files for Migration:
--------------------------------------------------------------------------------
 82/100 [Critical]  src/Services/PaymentProcessor.cs
 78/100 [Critical]  src/Services/OrderService.cs
 65/100 [High]      src/Repositories/UserRepository.cs
 58/100 [High]      src/Validators/InputValidator.cs
...
```

Files are scored based on patterns that map to OPAL features:

| Pattern | OPAL Feature |
|:--------|:-------------|
| Argument validation, `ArgumentException` | `§Q` precondition contracts |
| Null checks, `?.`, `??` | `Option<T>` types |
| Try/catch blocks | `Result<T,E>` error handling |
| File I/O, network, database calls | `§E` effect declarations |

**Start with high-scoring files** - they'll benefit most from OPAL's features.

---

## Step 2: Initialize Your Project

Set up OPAL tooling and MSBuild integration:

```bash
# Initialize with Claude Code support
opalc init --ai claude

# Or specify a specific project file
opalc init --ai claude --project MyApp.csproj
```

### What Gets Created

| File | Purpose |
|:-----|:--------|
| `.claude/skills/opal.md` | Claude skill for writing OPAL code |
| `.claude/skills/opal-convert.md` | Claude skill for C# to OPAL conversion |
| `CLAUDE.md` | Project documentation with OPAL reference |
| `MyApp.csproj` (updated) | MSBuild targets for automatic OPAL compilation |

### MSBuild Integration

Your `.csproj` now includes targets that:

- Compile `.opal` files before C# compilation
- Include generated `.g.cs` files in the build
- Clean generated files on `dotnet clean`

Generated files go to `obj/<Configuration>/<TargetFramework>/opal/`, keeping your source tree clean.

---

## Step 3: Add Your First OPAL File

Create a new `.opal` file alongside your existing code:

```bash
# Create a simple OPAL module
cat > src/Services/Calculator.opal << 'EOF'
§M[m001:Calculator]

§F[f001:Add:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §R (+ a b)
§/F[f001]

§F[f002:Divide:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §Q (!= b 0)
  §R (/ a b)
§/F[f002]

§/M[m001]
EOF
```

---

## Step 4: Build and Verify

```bash
# Build your project - OPAL compiles automatically
dotnet build
```

The build process:

1. Finds all `.opal` files in your project
2. Compiles each to `.g.cs` in the `obj/opal/` directory
3. Includes generated C# in the normal compilation
4. Produces your final assembly

### Verify the Generated Code

Check the generated C# to understand what OPAL produces:

```bash
cat obj/Debug/net8.0/opal/Calculator.g.cs
```

---

## Step 5: Gradual Migration

Now you can migrate existing C# files one at a time.

### Option A: Use Claude Code (Recommended)

If you initialized with `--ai claude`, use the conversion skill:

```
/opal-convert src/Services/PaymentProcessor.cs
```

Claude will:
1. Read the C# file
2. Convert it to OPAL syntax
3. Preserve all functionality
4. Add contracts where appropriate

### Option B: Use the CLI

Convert a single file:

```bash
# Convert C# to OPAL
opalc convert src/Services/PaymentProcessor.cs

# Output: src/Services/PaymentProcessor.opal

# With benchmark comparison
opalc convert src/Services/PaymentProcessor.cs --benchmark
```

### Option C: Convert Entire Project

For bulk conversion:

```bash
# Preview what would be converted
opalc migrate ./src --dry-run

# Actually convert all files
opalc migrate ./src --direction cs-to-opal

# Generate a migration report
opalc migrate ./src --report migration-report.md
```

---

## Step 6: Remove Original C# Files (Optional)

Once you've verified the OPAL version works correctly:

```bash
# Remove the original C# file
rm src/Services/PaymentProcessor.cs

# The OPAL file now produces the generated C# during build
```

Or keep both if you want to compare them during a transition period.

---

## Using Claude Code

After initialization, Claude Code provides powerful assistance:

### Writing New OPAL Code

```
/opal

Write a function that validates email addresses with:
- Precondition: input is not null or empty
- Postcondition: returns true only for valid emails
- Effect: pure (no side effects)
```

### Converting Existing Code

```
/opal-convert

Convert this C# class to OPAL:
[paste your C# code or reference a file]
```

### Refactoring OPAL

```
Extract the validation logic from function f001 into a separate private function
```

Claude understands OPAL's ID-based structure, so you can reference specific functions, loops, and conditionals by their IDs.

---

## Project Structure After Migration

A typical mixed project structure:

```
MyProject/
├── MyProject.csproj           # MSBuild with OPAL targets
├── CLAUDE.md                  # Project docs for Claude
├── .claude/
│   └── skills/
│       ├── opal.md           # OPAL writing skill
│       └── opal-convert.md   # Conversion skill
├── src/
│   ├── Program.cs            # C# entry point
│   ├── Models/
│   │   └── User.cs           # Keep simple DTOs in C#
│   └── Services/
│       ├── AuthService.cs    # Legacy C# (not yet converted)
│       ├── PaymentService.opal    # Converted to OPAL
│       └── OrderService.opal      # Converted to OPAL
└── obj/
    └── Debug/net8.0/opal/
        ├── PaymentService.g.cs    # Generated C#
        └── OrderService.g.cs      # Generated C#
```

---

## Best Practices

### What to Convert First

1. **High-scoring files** from `opalc analyze`
2. **Files with complex validation** - benefit from contracts
3. **Files with error handling** - benefit from `Result<T,E>`
4. **Files with side effects** - benefit from effect declarations

### What to Keep in C#

- Simple DTOs and record types
- Auto-generated code (EF migrations, gRPC stubs)
- Files with heavy framework integration
- Code using features OPAL doesn't support yet

### Conversion Tips

- Convert one file at a time and verify it works
- Run your tests after each conversion
- Use `--benchmark` to see token savings
- Keep the original C# until you're confident in the OPAL version

---

## Troubleshooting

### Build Fails: "opalc not found"

Add the .NET tools directory to your PATH:

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

### Generated Files Not Updating

Clean and rebuild:

```bash
dotnet clean
dotnet build
```

### Conversion Produces Warnings

The converter warns about patterns it can't perfectly translate:

```
Warning: Complex LINQ expression at line 42 - manual review recommended
```

Review these manually and adjust the OPAL code as needed.

### Test Failures After Conversion

1. Compare the generated C# with the original
2. Check that all method signatures match
3. Verify contracts aren't rejecting valid inputs
4. Look for subtle behavioral differences

---

## Next Steps

- [Syntax Reference](/opal/syntax-reference/) - Complete OPAL language reference
- [Contracts](/opal/syntax-reference/contracts/) - Writing preconditions and postconditions
- [Effects](/opal/syntax-reference/effects/) - Declaring side effects
- [opalc analyze](/opal/cli/analyze/) - Understanding migration scores
- [opalc benchmark](/opal/cli/benchmark/) - Comparing OPAL vs C# metrics
