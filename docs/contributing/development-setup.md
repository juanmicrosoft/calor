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

<!-- Keep this section broadly in sync with the terser version in CLAUDE.md
     (and AGENTS.md, which is regenerated from CLAUDE.md). If you edit
     substance here, mirror the change into CLAUDE.md's "Corpus Submodules"
     section. -->

`.gitmodules` pins three real-world C# corpora under `bench/corpus/`:

- `bench/corpus/MediatR`
- `bench/corpus/serilog`
- `bench/corpus/FluentValidation`

`git clone` does **not** auto-initialize these — CI opts them in per job.
Find the current call sites with
`grep -n submodules: .github/workflows/test.yml`; today the `test`,
`roundtrip-verification`, and the `compiler` shard of `remaining-tests`
jobs are the ones that need them. If you skip the init step and later run
the round-trip harness or the corpus binder-ratchet leg locally, you will
see confusing "corpus submodules not initialized" errors or an
apparently-empty `bench/corpus/` tree. The three corpora add ~500 MB to
a fresh clone.

**On a fresh clone**, initialize the submodules:

```bash
git submodule update --init
```

`--recursive` is not needed — the three pinned submodules are flat (no
nested submodules of their own). CI passes `--recursive` as a safe
superset default; scripts can drop it.

**After upstream changes** to the pinned SHAs, refresh them:

```bash
git submodule update
```

Note: this fast-forwards each submodule to the pinned SHA on your
current outer-repo commit; if you have local changes inside a submodule
they will be lost unless you `stash` or commit them first.

### When do I actually need this?

You need the submodules populated **only** if you plan to:

- Run the round-trip harness in `tools/Calor.RoundTrip.Harness/` locally.
- Reproduce the `roundtrip-verification` CI job locally (e.g. debugging a
  `MediatR: MinorRegressions` failure).
- Reproduce the `corpus-binder-ratchet` leg locally, or run the specific
  `BinderIncompleteRatchetTests` class in `Calor.Compiler.Tests`.

The rest of the test suite works fine on a bare clone:
`BinderIncompleteRatchetTests`
(`tests/Calor.Compiler.Tests/Binding/BinderIncompleteRatchetTests.cs`)
uses `Skip.IfNot(subjects.All(Directory.Exists), "corpus submodules not
initialized")` to skip cleanly if the corpus is empty, so
`dotnet test tests/Calor.Compiler.Tests/` is safe without submodules —
you will just silently skip that one class. `Calor.Conversion.Tests`,
`Calor.Semantics.Tests`, `Calor.Verification.Tests`,
`Calor.Enforcement.Tests`, and `Calor.Evaluation` do not touch
`bench/corpus/` at all.

Because CI initializes submodules per-job, dev/CI parity issues show
up as "corpus tests green in CI, failing locally". Running
`git submodule update --init` on the outer repo is almost always the fix.

See also the "Build & Test" section in the repo root
[`AGENTS.md`](https://github.com/juanmicrosoft/calor/blob/main/AGENTS.md)
and `CLAUDE.md` for the terser agent-oriented version of this note.

Origin: 2026-08-18 test-suite audit, finding F8 / recommendation R4
(`docs/plans/2026-08-18-test-suite-audit.md`).

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

Three lanes, ordered fastest to slowest. See the
[Corpus Submodules](#corpus-submodules-round-trip-harness) section above for
which projects touch `bench/corpus/` and why the fast lane is safe without
submodules.

### Fast Unit-Test Lane (no submodules)

Seconds to run, no `bench/corpus/` init required:

```bash
dotnet test tests/Calor.Compiler.Tests/     # lexer/parser/emitter/analysis
dotnet test tests/Calor.Conversion.Tests/   # C# → Calor snapshots
dotnet test tests/Calor.Semantics.Tests/    # binding/semantic analysis
dotnet test tests/Calor.Verification.Tests/ # Z3 contract verification
dotnet test tests/Calor.Enforcement.Tests/  # effects/taint
```

`BinderIncompleteRatchetTests` silently skips on a bare clone (it uses
`Skip.IfNot` when `bench/corpus/` is empty); everything else runs green
without submodules. To iterate on a single class:

```bash
dotnet test --filter "FullyQualifiedName~ClassName"
```

### Full-Corpus Lane (submodules required)

Adds ~500 MB and a few minutes; needed for the `BinderIncompleteRatchetTests`
corpus assertions:

```bash
git submodule update --init && dotnet test
```

### Round-Trip Harness Lane (~30 min)

Matches the `roundtrip-verification` CI job end-to-end:

```bash
git submodule update --init
dotnet run --project tools/Calor.RoundTrip.Harness -- run --all
```

`run --all` iterates every project in `ProjectConfigs.KnownProjects` against
the vendored `bench/corpus/` corpus. Pass a single project name
(`run MediatR`) to shrink the loop while debugging a specific regression.

### E2E Tests

```bash
# Mac/Linux
./tests/E2E/run-tests.sh

# Windows
.\tests\E2E\run-tests.ps1

# Clean generated files
./tests/E2E/run-tests.sh --clean
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
