# Calor

**Coding Agent Language for Optimized Reasoning**

A programming language designed specifically for AI coding agents, compiling to .NET via C# emission.

## Why Calor?

AI coding agents need to understand code semantically — what it does, what side effects it has, what contracts it upholds. Traditional languages hide this information behind syntax that requires deep analysis to parse.

Calor makes these things explicit:

| Feature | Calor Syntax | Benefit |
|---------|--------------|---------|
| Side effects | `§E{cw,fr,net}` | Know effects without reading implementation |
| Contracts | `§Q` (requires), `§S` (ensures) | Generate tests, verify correctness |
| Unique IDs | `§F{f001:Main}` | Precise references that survive refactoring |
| Clear structure | `§F{...}...§/F{...}` | Unambiguous scope boundaries |

## Quick Start

```bash
# Install the compiler
dotnet tool install -g calor

# Initialize for Claude Code (in a C# project folder)
calor init --ai claude

# Compile Calor to C#
calor --input program.calr --output program.g.cs
```

## Example

```
§M{m001:Hello}
§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §P "Hello from Calor!"
§/F{f001}
§/M{m001}
```

## Documentation

- [Getting Started](https://calor.dev/docs/getting-started/)
- [Syntax Reference](https://calor.dev/docs/syntax-reference/)
- [How It Works](https://calor.dev/docs/getting-started/how-it-works/)

## Learn More

- [GitHub Repository](https://github.com/juanmicrosoft/calor)
- [Website](https://calor.dev)
