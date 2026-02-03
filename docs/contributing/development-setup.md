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
| .NET SDK | 8.0+ | Build and run |
| Git | Any recent | Version control |
| Editor | VS Code, Rider, VS | Code editing |

---

## Clone and Build

```bash
# Clone your fork (or the main repo)
git clone https://github.com/YOUR_USERNAME/calor.git
cd calor

# Build everything
dotnet build

# Run tests
dotnet test
```

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
  --input path/to/file.calor \
  --output path/to/output.g.cs

# With verbose output
dotnet run --project src/Calor.Compiler -- \
  --input file.calor \
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
      "args": ["--input", "test.calor", "--output", "test.g.cs"],
      "cwd": "${workspaceFolder}",
      "console": "internalConsole"
    }
  ]
}
```

### Rider/Visual Studio

Set `Calor.Compiler` as startup project with command line arguments:
```
--input samples/HelloWorld/hello.calor --output output.g.cs
```

---

## Code Style

### C# Conventions

- Use file-scoped namespaces
- Use expression-bodied members where appropriate
- Use `var` for obvious types
- Use meaningful names

### Calor Conventions

- Use V2 syntax (Lisp-style expressions)
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
