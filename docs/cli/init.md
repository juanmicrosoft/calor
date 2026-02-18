---
layout: default
title: init
parent: CLI Reference
nav_order: 2
permalink: /cli/init/
---

# calor init

Initialize Calor development environment with MSBuild integration and optional AI agent support.

```bash
calor init [options]
```

---

## Overview

The `init` command sets up your project for Calor development by:

1. **Adding MSBuild targets** - Integrates Calor compilation into your .NET build process
2. **Configuring AI agent integration** (optional) - Creates skills/prompts for your preferred AI coding assistant

After running `init`, you can write `.calr` files alongside your `.cs` files and they'll compile automatically during `dotnet build`.

---

## Solution vs Project Mode

The `init` command operates in one of two modes depending on your project structure:

### Solution Mode

When initializing a solution (`.sln` or `.slnx` file):

- **AI files** (`CLAUDE.md`, `.claude/skills/`, etc.) are created in the **solution root directory**
- **MSBuild targets** are added to **each project** in the solution
- All projects share the same AI configuration

This is ideal for multi-project solutions where you want consistent Calor tooling across all projects.

```
MySolution/
├── MySolution.sln
├── CLAUDE.md                    # AI config in solution root
├── .mcp.json                    # MCP server configuration
├── .claude/
│   ├── settings.json            # Hooks configuration
│   └── skills/calor/SKILL.md
├── src/
│   ├── Core/
│   │   └── Core.csproj          # Has Calor MSBuild targets
│   └── Web/
│       └── Web.csproj           # Has Calor MSBuild targets
└── tests/
    └── Tests.csproj             # Has Calor MSBuild targets
```

### Project Mode

When initializing a single project (`.csproj` file):

- **AI files** are created in the **project directory**
- **MSBuild targets** are added to that specific project

This is ideal for standalone projects or when you need different AI configurations per project.

```
MyProject/
├── MyProject.csproj             # Has Calor MSBuild targets
├── CLAUDE.md                    # AI config in project root
├── .mcp.json                    # MCP server configuration
└── .claude/
    ├── settings.json            # Hooks configuration
    └── skills/calor/SKILL.md
```

---

## Quick Start

```bash
# Basic initialization (MSBuild integration only)
calor init

# Initialize with Claude Code support
calor init --ai claude

# Initialize a solution (auto-detected or explicit)
calor init --ai claude

# Initialize with explicit solution file
calor init --solution MySolution.sln --ai claude

# Initialize with a specific .csproj
calor init --project MyApp.csproj

# Initialize with both Claude and specific project
calor init --ai claude --project MyApp.csproj
```

---

## Options

| Option | Short | Required | Description |
|:-------|:------|:---------|:------------|
| `--ai` | `-a` | No | AI agent to configure: `claude`, `codex`, `gemini`, `github` |
| `--project` | `-p` | No | Target .csproj file (auto-detects if single .csproj exists) |
| `--solution` | `-s` | No | Target .sln or .slnx file (initializes all projects in solution) |
| `--force` | `-f` | No | Overwrite existing files without prompting |

---

## AI Agent Support (Optional)

When you specify `--ai`, the command also sets up AI-specific configuration files.

### Claude (`--ai claude`)

Creates the following files:

| File | Purpose |
|:-----|:--------|
| `.claude/skills/calor/SKILL.md` | Calor code writing skill with YAML frontmatter |
| `.claude/skills/calor-convert/SKILL.md` | C# to Calor conversion skill |
| `.claude/skills/calor-semantics/SKILL.md` | Calor semantics and verification skill |
| `.claude/skills/calor-analyze/SKILL.md` | C# analysis for Calor migration skill |
| `.claude/settings.json` | **Hooks** - enforces Calor-first development with pre-tool-use hooks |
| `.mcp.json` | **MCP servers** - provides Calor compiler tools to Claude |
| `CLAUDE.md` | Project documentation (creates new or updates Calor section) |

#### MCP Server Integration

The `.mcp.json` file configures **two MCP servers**:

```json
{
  "mcpServers": {
    "calor-lsp": {
      "type": "stdio",
      "command": "calor",
      "args": ["lsp"]
    },
    "calor": {
      "type": "stdio",
      "command": "calor",
      "args": ["mcp", "--stdio"]
    }
  }
}
```

| Server | Purpose |
|:-------|:--------|
| `calor-lsp` | Language server for real-time diagnostics, hover info, go-to-definition |
| `calor` | MCP tools for compile, verify, analyze, convert, and syntax help |

The MCP tools allow Claude to:
- **Compile** Calor code and see generated C#
- **Verify** contracts with Z3 SMT solver
- **Analyze** code for security vulnerabilities
- **Convert** C# to Calor programmatically
- **Get syntax help** for specific Calor features

See [`calor mcp`](/calor/cli/mcp/) for full tool documentation.

#### Calor-First Enforcement

The `.claude/settings.json` file configures a `PreToolUse` hook that **blocks Claude from creating `.cs` files** (MCP servers are in the separate `.mcp.json` file). When Claude tries to write a C# file, it will see:

```
BLOCKED: Cannot create C# file 'MyClass.cs'

This is an Calor-first project. Create an .calr file instead:
  MyClass.calr

Use /calor skill for Calor syntax help.
```

Claude will then automatically retry with an `.calr` file. This enforcement ensures all new code is written in Calor.

**Allowed file types:**
- `.calr` files (always allowed)
- `.g.cs` generated files (build output)
- Files in `obj/` directory (build artifacts)

After initialization, use these Claude Code commands:

| Command | Description |
|:--------|:------------|
| `/calor` | Write new Calor code with Claude's assistance |
| `/calor-convert` | Convert existing C# code to Calor syntax |

### Codex (`--ai codex`)

Creates the following files:

| File | Purpose |
|:-----|:--------|
| `.codex/skills/calor/SKILL.md` | Calor code writing skill with YAML frontmatter |
| `.codex/skills/calor-convert/SKILL.md` | C# to Calor conversion skill |
| `.codex/config.toml` | MCP server configuration for Calor tools |
| `AGENTS.md` | Project documentation with Calor-first guidelines |

#### MCP Server Integration

The `.codex/config.toml` file configures an MCP server that gives Codex direct access to Calor compiler tools (`calor_typecheck`, `calor_verify_contracts`, `calor_analyze`, `calor_convert`, and more):

```toml
# BEGIN CalorC MCP SECTION - DO NOT EDIT
[mcp_servers.calor]
command = "calor"
args = ["mcp", "--stdio"]
# END CalorC MCP SECTION
```

See [`calor mcp`](/calor/cli/mcp/) for the complete list of available tools.

#### Enforcement

Codex CLI does not support hooks like Claude Code. MCP tools provide direct access to Calor compiler features, and Calor-first development is **guidance-based**, relying on instructions in `AGENTS.md` and the skill files.

This means:
- Codex *should* create `.calr` files based on the instructions
- MCP tools give Codex native access to compile, verify, and convert
- Hooks are not supported, so enforcement is not automatic
- Review file extensions after code generation
- Use `calor assess` to find any unconverted `.cs` files

After initialization, use these Codex commands:

| Command | Description |
|:--------|:------------|
| `$calor` | Write new Calor code with Codex's assistance |
| `$calor-convert` | Convert existing C# code to Calor syntax |

#### Output Structure

```
project/
├── .codex/
│   ├── config.toml
│   └── skills/
│       ├── calor/
│       │   └── SKILL.md
│       └── calor-convert/
│           └── SKILL.md
├── AGENTS.md
└── MyProject.csproj
```

### Gemini (`--ai gemini`)

Creates the following files:

| File | Purpose |
|:-----|:--------|
| `.gemini/skills/calor/SKILL.md` | Calor code writing skill with YAML frontmatter |
| `.gemini/skills/calor-convert/SKILL.md` | C# to Calor conversion skill |
| `.gemini/settings.json` | **Hook configuration** - enforces Calor-first development |
| `GEMINI.md` | Project documentation (creates new or updates Calor section) |

#### Calor-First Enforcement

Like Claude Code, **Gemini CLI supports hooks** (as of v0.26.0+). The `.gemini/settings.json` file configures a `BeforeTool` hook that **blocks Gemini from creating `.cs` files**.

When Gemini tries to write a C# file, the hook returns a JSON response that blocks the operation and provides guidance:

```json
{
  "decision": "deny",
  "reason": "BLOCKED: Cannot create C# file 'MyClass.cs'",
  "systemMessage": "This is an Calor-first project. Create an .calr file instead: MyClass.calr\n\nUse @calor skill for Calor syntax help."
}
```

Gemini will then automatically retry with an `.calr` file. This enforcement ensures all new code is written in Calor.

**Allowed file types:**
- `.calr` files (always allowed)
- `.g.cs` generated files (build output)
- Files in `obj/` directory (build artifacts)

After initialization, use these Gemini CLI commands:

| Command | Description |
|:--------|:------------|
| `@calor` | Write new Calor code with Gemini's assistance |
| `@calor-convert` | Convert existing C# code to Calor syntax |

#### Output Structure

```
project/
├── .gemini/
│   ├── skills/
│   │   ├── calor/
│   │   │   └── SKILL.md
│   │   └── calor-convert/
│   │       └── SKILL.md
│   └── settings.json        # Hook configuration
├── GEMINI.md
└── MyProject.csproj
```

### GitHub Copilot (`--ai github`)

Creates the following files:

| File | Purpose |
|:-----|:--------|
| `.github/copilot/skills/calor/SKILL.md` | Calor code writing skill with YAML frontmatter |
| `.github/copilot/skills/calor-convert/SKILL.md` | C# to Calor conversion skill |
| `.github/copilot-instructions.md` | Project documentation with Calor-first guidelines |

#### Guidance-Based Enforcement

**Important:** GitHub Copilot does not support hooks like Claude Code. Calor-first development is **guidance-based only**, relying on instructions in `copilot-instructions.md` and the skill files.

This means:
- Copilot *should* create `.calr` files based on the instructions
- However, enforcement is not automatic - Copilot may occasionally create `.cs` files
- Review file extensions after code generation
- Use `calor assess` to find any unconverted `.cs` files

After initialization, reference the Calor skills when asking Copilot about Calor syntax or converting C# code.

#### Output Structure

```
project/
├── .github/
│   ├── copilot-instructions.md     # Calor guidelines
│   └── copilot/
│       └── skills/
│           ├── calor/
│           │   └── SKILL.md
│           └── calor-convert/
│               └── SKILL.md
└── MyProject.csproj
```

---

## MSBuild Integration

The `init` command adds three MSBuild targets to your `.csproj` file:

### CompileCalorFiles

Compiles all `.calr` files before C# compilation:

```xml
<Target Name="CompileCalorFiles" BeforeTargets="BeforeBuild">
  <Exec Command="calor --input %(CalorFiles.Identity) --output $(CalorOutputPath)%(CalorFiles.Filename).g.cs" />
</Target>
```

### IncludeCalorGeneratedFiles

Includes generated `.g.cs` files in the compilation:

```xml
<Target Name="IncludeCalorGeneratedFiles" BeforeTargets="BeforeBuild" DependsOnTargets="CompileCalorFiles">
  <ItemGroup>
    <Compile Include="$(CalorOutputPath)*.g.cs" />
  </ItemGroup>
</Target>
```

### CleanCalorFiles

Cleans generated files when running `dotnet clean`:

```xml
<Target Name="CleanCalorFiles" AfterTargets="Clean">
  <Delete Files="$(CalorOutputPath)*.g.cs" />
</Target>
```

### Output Location

Generated C# files are placed in the intermediate output directory:

```
obj/<Configuration>/<TargetFramework>/calor/
```

For example: `obj/Debug/net8.0/calor/MyModule.g.cs`

This keeps generated files out of your source tree while still making them part of the build.

### MSBuild Properties

The following properties control Calor build behavior. Set them in your `.csproj` or pass via `-p:` on the command line.

| Property | Default | Description |
|:---------|:--------|:------------|
| `CalorCompilerPath` | `calor` | Path to the Calor CLI executable |
| `CalorOutputDirectory` | `obj/<Config>/<TFM>/calor/` | Directory for generated `.g.cs` files |
| `CalorCompilerOverride` | *(empty)* | Override path for a locally-built compiler (see below) |

#### CalorCompilerOverride

When set, `CalorCompilerOverride` derives `CalorCompilerPath` automatically. This is intended for developers working on the Calor compiler itself who want to test local changes without modifying project files:

```bash
dotnet build -p:CalorCompilerOverride=path/to/Calor.Compiler/bin/Debug/net8.0/calor
```

**Precedence** (highest to lowest):
1. Explicit `CalorCompilerPath` set in the project file
2. `CalorCompilerOverride` (derives `CalorCompilerPath`)
3. Default (`calor`)

When `CalorCompilerOverride` is set, the build emits a warning confirming which compiler is being used. If the path does not exist, the build fails with an error.

> **Note:** For projects using `Calor.Sdk` (MSBuild task integration) rather than `calor init`, `CalorCompilerOverride` derives `CalorTasksAssembly` instead. Point it at your local `Calor.Tasks.dll`:
> ```bash
> dotnet build -p:CalorCompilerOverride=path/to/Calor.Tasks/bin/Debug/net8.0/Calor.Tasks.dll
> ```

---

## Calor/C# Coexistence

After initialization, `.calr` and `.cs` files coexist seamlessly in your project:

```
MyProject/
├── MyProject.csproj        # Updated with Calor targets
├── Program.cs              # Existing C# code
├── Services/
│   ├── UserService.cs      # Existing C# service
│   └── PaymentService.calr # New Calor service
└── obj/
    └── Debug/net8.0/calor/
        └── PaymentService.g.cs  # Generated C#
```

### Build Workflow

1. Run `dotnet build`
2. MSBuild triggers `CompileCalorFiles` target
3. Each `.calr` file is compiled to `.g.cs` in `obj/calor/`
4. Generated files are included in C# compilation
5. Normal .NET build continues

### No Manual Steps

- No need to run `calor` manually
- No need to manage generated file paths
- `dotnet clean` removes generated files automatically

---

## Auto-Detection

If you don't specify `--project` or `--solution`, the command auto-detects what to initialize:

| Priority | Detection | Behavior |
|:---------|:----------|:---------|
| 1 | `.slnx` files | Uses solution mode (newer XML format) |
| 2 | `.sln` files | Uses solution mode |
| 3 | Single `.csproj` | Uses project mode |
| 4 | Multiple `.csproj` | Error - specify with `--project` |

### Multiple Solutions

If multiple solution files are found in the current directory, you must specify which one to use:

```bash
# Error: Multiple solutions found
calor init --ai claude

# Specify the solution explicitly
calor init --solution MyApp.sln --ai claude
```

### Solution Parsing

The `init` command parses solution files to find all referenced C# projects:

- **`.sln` files**: Traditional text format, parsed using regex
- **`.slnx` files**: Newer XML format, parsed as XML

Only C# projects (`.csproj`) are initialized. Solution folders and non-C# projects are skipped.

---

## File Backup

When updating an existing `.csproj`, the command creates a backup:

```
MyProject.csproj.bak
```

This allows you to restore the original if needed.

---

## Re-running Init

You can safely run `calor init` multiple times:

- **CLAUDE.md**: Updates only the Calor section, preserving your custom content
- **Skills files**: Overwrites with latest version (use `--force` or confirm)
- **.csproj targets**: Skips if already present (idempotent)

---

## Examples

### Basic Initialization

```bash
# Navigate to your project
cd ~/projects/MyApp

# Initialize Calor (MSBuild integration only)
calor init

# Analyze codebase for migration candidates
calor assess ./src --top 10
```

### Initialize with Claude Code

```bash
# Initialize with Claude Code support
calor init --ai claude
```

This adds `/calor` and `/calor-convert` skills plus CLAUDE.md with guidelines that instruct Claude to:
- Write new files in Calor instead of C#
- Analyze C# files before modifying to check if they should be converted first

### Initialize New Project

```bash
# Create a new console app and initialize Calor
dotnet new console -o MyCalorApp
cd MyCalorApp
calor init
calor init --ai claude  # Optional: add Claude support
```

### Initialize a Solution

```bash
# Initialize all projects in a solution (auto-detected)
calor init --ai claude

# Or specify the solution explicitly
calor init --solution MySolution.sln --ai claude
```

This creates AI files in the solution root and adds MSBuild targets to all projects:

```
Initialized Calor solution for Claude Code (calor v0.1.6)

Solution: MySolution.sln (3 projects)

Created files:
  CLAUDE.md
  .mcp.json
  .claude/skills/calor/SKILL.md
  .claude/skills/calor-convert/SKILL.md
  .claude/settings.json

Updated projects:
  src/Core/Core.csproj
  src/Web/Web.csproj
  tests/Tests.csproj

MSBuild configuration:
  - Added Calor compilation targets to 3 projects
```

### Initialize Individual Projects

If you need to initialize projects independently (different AI configs per project):

```bash
calor init --ai claude --project src/Core/Core.csproj
calor init --ai claude --project src/Web/Web.csproj
```

---

## Troubleshooting

### "calor not found"

Ensure the Calor compiler is installed globally:

```bash
dotnet tool install -g calor
```

And that your PATH includes the .NET tools directory:

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

### "Multiple .csproj files found"

Specify the exact project:

```bash
calor init --ai claude --project ./src/MyApp/MyApp.csproj
```

### "Multiple solution files found"

Specify which solution to use:

```bash
calor init --solution MyApp.sln --ai claude
```

### Build Fails After Init

1. Verify `calor` is in PATH: `which calor`
2. Check the `.csproj` has the Calor targets
3. Try running `calor` manually on a test file

---

## See Also

- [Adding Calor to Existing Projects](/calor/guides/adding-calor-to-existing-projects/) - Complete migration guide
- [calor convert](/calor/cli/convert/) - Convert individual files
- [calor assess](/calor/cli/assess/) - Find migration candidates
- [Claude Integration](/calor/getting-started/claude-integration/) - Using Calor with Claude Code
- [Codex Integration](/calor/getting-started/codex-integration/) - Using Calor with OpenAI Codex CLI
- [Gemini Integration](/calor/getting-started/gemini-integration/) - Using Calor with Google Gemini CLI
