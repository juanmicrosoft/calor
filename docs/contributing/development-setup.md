---
layout: default
title: Development Setup
parent: Contributing
nav_order: 1
---

# Development Setup

This guide helps you set up a development environment for contributing to Calor.

---

## Prerequisites

| Requirement | Version | Purpose |
|:------------|:--------|:--------|
| .NET SDK | 10.0+ | Build and run |
| Python | 3.11+ | Hermetic asset and release metadata verification |
| Git | Any recent | Version control |
| Editor | VS Code, Rider, VS | Code editing |

---

## Clone and Build

```bash
# Clone your fork (or the main repo)
git clone https://github.com/YOUR_USERNAME/calor.git
cd calor

# Explicitly bootstrap checksum-verified native dependencies once
bash src/Calor.Compiler/scripts/download-z3.sh
# Windows: pwsh src/Calor.Compiler/scripts/download-z3.ps1

# Restore the committed dependency graph, then build without network access
dotnet restore --locked-mode
dotnet build --no-restore

# Run tests
dotnet test --no-restore
```

The documented `--no-restore` build/test/pack path never downloads dependencies
or rewrites tracked source resources. Restores are always locked; update a lock
file intentionally with
`dotnet restore --force-evaluate -p:RestoreLockedMode=false`. To refresh
embedded self-test fixtures explicitly, run
`python3 scripts/sync-self-test-resources.py`.

---

## Corpus Submodules (Round-Trip Harness)

`.gitmodules` pins three real-world C# corpora under `bench/corpus/`:

- `bench/corpus/MediatR`
- `bench/corpus/serilog`
- `bench/corpus/FluentValidation`

`git clone` does **not** auto-initialize these — CI opts in per job (see
`.github/workflows/test.yml` at lines 59, 229, and 719 for the
`submodules: recursive` checkouts). If you skip this step and later run the
round-trip harness or the corpus binder-ratchet leg locally, you will see
confusing "corpus submodules not initialized" errors or an
apparently-empty `bench/corpus/` tree.

**On a fresh clone**, initialize the submodules:

```bash
git submodule update --init --recursive
```

**After upstream changes** to the pinned SHAs, refresh them:

```bash
git submodule update --recursive
```

### When do I actually need this?

You need the submodules populated **only** if you plan to:

- Run the round-trip harness in `tools/Calor.RoundTrip.Harness/` locally.
- Reproduce the `roundtrip-verification` CI job locally (e.g. debugging a
  `MediatR: MinorRegressions` failure).
- Reproduce the corpus binder-ratchet leg locally.

The fast unit-test lane below (`Calor.Compiler.Tests`,
`Calor.Conversion.Tests`, `Calor.Semantics.Tests`,
`Calor.Verification.Tests`, `Calor.Enforcement.Tests`, and
`Calor.Evaluation`) does **not** touch `bench/corpus/` and works fine on a
fresh clone without submodules.

Because CI initializes submodules per-job, it is possible for the corpus
tests to be green in CI while failing locally on a fresh clone — running
`git submodule update --init --recursive` is almost always the fix.

See also the "Build & Test" section in the repo root
[`AGENTS.md`](https://github.com/juanmicrosoft/calor/blob/main/AGENTS.md)
and `CLAUDE.md` for the terser agent-oriented version of this note.

---

## Project Structure

```
calor/
├── src/
│   └── Calor.Compiler/           # The compiler
│       ├── Lexer/               # Tokenization
│       ├── Parser/              # Parsing
│       ├── AST/                 # Abstract syntax tree
│       └── CodeGen/             # C# generation
│
├── samples/
│   └── HelloWorld/              # Sample program
│
├── tests/
│   ├── E2E/                     # End-to-end tests
│   │   ├── scenarios/           # Test programs
│   │   ├── run-tests.sh         # Mac/Linux runner
│   │   └── run-tests.ps1        # Windows runner
│   │
│   └── Calor.Evaluation/         # Evaluation framework
│       ├── Metrics/             # Metric calculators
│       ├── Core/                # Framework core
│       └── Benchmarks/          # Benchmark programs
│
└── docs/                        # This documentation
```

---

## Running the Compiler

```bash
# Compile an Calor file
dotnet run --project src/Calor.Compiler -- \
  --input path/to/file.calr \
  --output path/to/output.g.cs

# With verbose output
dotnet run --project src/Calor.Compiler -- \
  --input file.calr \
  --output file.g.cs \
  --verbose
```

---

## Running Tests

### E2E Tests

```bash
# Mac/Linux
./tests/E2E/run-tests.sh

# Windows
.\tests\E2E\run-tests.ps1

# Clean generated files
./tests/E2E/run-tests.sh --clean
```

### Unit Tests

```bash
# Run all unit tests
dotnet test

# Run specific test project
dotnet test tests/Calor.Compiler.Tests
```

### Evaluation Framework

```bash
# Run evaluation
dotnet run --project tests/Calor.Evaluation -- --output report.json

# Generate markdown report
dotnet run --project tests/Calor.Evaluation -- --output report.md --format markdown
```

---

## Making Changes

### 1. Create a Branch

```bash
git checkout -b feature/your-feature-name
```

### 2. Make Your Changes

Edit the relevant files in your editor.

### 3. Build and Test

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run E2E tests
./tests/E2E/run-tests.sh
```

### 4. Commit

```bash
git add -A
git commit -m "Description of your changes"
```

### 5. Push and PR

```bash
git push origin feature/your-feature-name
```

Then create a pull request on GitHub.

---

## Common Development Tasks

### Adding a New Syntax Element

1. Update `Lexer/` to recognize new tokens
2. Update `Parser/` to parse new syntax
3. Update `AST/` with new node types
4. Update `CodeGen/` to emit C#
5. Add E2E test in `tests/E2E/scenarios/`

### Adding a New Metric

1. Create calculator in `tests/Calor.Evaluation/Metrics/`
2. Implement `IMetricCalculator` interface
3. Register in evaluation runner
4. Add documentation

### Testing Local Compiler Changes

Set `CalorCompilerOverride` to use a locally-built compiler:

```bash
# For projects using Calor.Sdk (MSBuild task integration)
dotnet build -p:CalorCompilerOverride=path/to/Calor.Tasks/bin/Debug/net8.0/Calor.Tasks.dll

# For projects using calor init (CLI integration)
dotnet build -p:CalorCompilerOverride=path/to/Calor.Compiler/bin/Debug/net8.0/calor
```

This overrides the compiler path without modifying project files. If an explicit
`CalorTasksAssembly` (SDK path) or `CalorCompilerPath` (CLI path) is already set,
those take precedence over `CalorCompilerOverride`.

### Adding a Benchmark

See [Adding Benchmarks](/calor/contributing/adding-benchmarks/).

---

## Debugging

### VS Code

Add to `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Debug Compiler",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/src/Calor.Compiler/bin/Debug/net8.0/Calor.Compiler.dll",
      "args": ["--input", "test.calr", "--output", "test.g.cs"],
      "cwd": "${workspaceFolder}",
      "console": "internalConsole"
    }
  ]
}
```

### Rider/Visual Studio

Set `Calor.Compiler` as startup project with command line arguments:
```
--input samples/HelloWorld/hello.calr --output output.g.cs
```

---

## Code Style

### C# Conventions

- Use file-scoped namespaces
- Use expression-bodied members where appropriate
- Use `var` for obvious types
- Use meaningful names

### Calor Conventions

- Use Lisp-style expressions for operations
- Include IDs on all structures
- Declare effects explicitly
- Add contracts where meaningful

---

## Getting Help

- Check existing issues on GitHub
- Open a new issue for questions
- Review the documentation

---

## Next

- [Adding Benchmarks](/calor/contributing/adding-benchmarks/) - Add evaluation programs
