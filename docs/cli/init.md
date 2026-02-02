---
layout: default
title: init
parent: CLI Reference
nav_order: 2
permalink: /cli/init/
---

# opalc init

Initialize OPAL development environment with MSBuild integration and optional AI agent support.

```bash
opalc init [options]
```

---

## Overview

The `init` command sets up your project for OPAL development by:

1. **Adding MSBuild targets** - Integrates OPAL compilation into your .NET build process
2. **Configuring AI agent integration** (optional) - Creates skills/prompts for your preferred AI coding assistant

After running `init`, you can write `.opal` files alongside your `.cs` files and they'll compile automatically during `dotnet build`.

---

## Solution vs Project Mode

The `init` command operates in one of two modes depending on your project structure:

### Solution Mode

When initializing a solution (`.sln` or `.slnx` file):

- **AI files** (`CLAUDE.md`, `.claude/skills/`, etc.) are created in the **solution root directory**
- **MSBuild targets** are added to **each project** in the solution
- All projects share the same AI configuration

This is ideal for multi-project solutions where you want consistent OPAL tooling across all projects.

```
MySolution/
├── MySolution.sln
├── CLAUDE.md                    # AI config in solution root
├── .claude/
│   ├── settings.json
│   └── skills/opal/SKILL.md
├── src/
│   ├── Core/
│   │   └── Core.csproj          # Has OPAL MSBuild targets
│   └── Web/
│       └── Web.csproj           # Has OPAL MSBuild targets
└── tests/
    └── Tests.csproj             # Has OPAL MSBuild targets
```

### Project Mode

When initializing a single project (`.csproj` file):

- **AI files** are created in the **project directory**
- **MSBuild targets** are added to that specific project

This is ideal for standalone projects or when you need different AI configurations per project.

```
MyProject/
├── MyProject.csproj             # Has OPAL MSBuild targets
├── CLAUDE.md                    # AI config in project root
└── .claude/
    ├── settings.json
    └── skills/opal/SKILL.md
```

---

## Quick Start

```bash
# Basic initialization (MSBuild integration only)
opalc init

# Initialize with Claude Code support
opalc init --ai claude

# Initialize a solution (auto-detected or explicit)
opalc init --ai claude

# Initialize with explicit solution file
opalc init --solution MySolution.sln --ai claude

# Initialize with a specific .csproj
opalc init --project MyApp.csproj

# Initialize with both Claude and specific project
opalc init --ai claude --project MyApp.csproj
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
| `.claude/skills/opal/SKILL.md` | OPAL code writing skill with YAML frontmatter |
| `.claude/skills/opal-convert/SKILL.md` | C# to OPAL conversion skill |
| `.claude/settings.json` | **Hook configuration** - enforces OPAL-first development |
| `CLAUDE.md` | Project documentation (creates new or updates OPAL section) |

#### OPAL-First Enforcement

The `.claude/settings.json` file configures a `PreToolUse` hook that **blocks Claude from creating `.cs` files**. When Claude tries to write a C# file, it will see:

```
BLOCKED: Cannot create C# file 'MyClass.cs'

This is an OPAL-first project. Create an .opal file instead:
  MyClass.opal

Use /opal skill for OPAL syntax help.
```

Claude will then automatically retry with an `.opal` file. This enforcement ensures all new code is written in OPAL.

**Allowed file types:**
- `.opal` files (always allowed)
- `.g.cs` generated files (build output)
- Files in `obj/` directory (build artifacts)

After initialization, use these Claude Code commands:

| Command | Description |
|:--------|:------------|
| `/opal` | Write new OPAL code with Claude's assistance |
| `/opal-convert` | Convert existing C# code to OPAL syntax |

### Codex (`--ai codex`)

Creates the following files:

| File | Purpose |
|:-----|:--------|
| `.codex/skills/opal/SKILL.md` | OPAL code writing skill with YAML frontmatter |
| `.codex/skills/opal-convert/SKILL.md` | C# to OPAL conversion skill |
| `AGENTS.md` | Project documentation with OPAL-first guidelines |

#### Guidance-Based Enforcement

**Important:** Codex CLI does not support hooks like Claude Code. OPAL-first development is **guidance-based only**, relying on instructions in `AGENTS.md` and the skill files.

This means:
- Codex *should* create `.opal` files based on the instructions
- However, enforcement is not automatic - Codex may occasionally create `.cs` files
- Review file extensions after code generation
- Use `opalc analyze` to find any unconverted `.cs` files

After initialization, use these Codex commands:

| Command | Description |
|:--------|:------------|
| `$opal` | Write new OPAL code with Codex's assistance |
| `$opal-convert` | Convert existing C# code to OPAL syntax |

#### Output Structure

```
project/
├── .codex/
│   └── skills/
│       ├── opal/
│       │   └── SKILL.md
│       └── opal-convert/
│           └── SKILL.md
├── AGENTS.md
└── MyProject.csproj
```

### Gemini (`--ai gemini`)

Creates the following files:

| File | Purpose |
|:-----|:--------|
| `.gemini/skills/opal/SKILL.md` | OPAL code writing skill with YAML frontmatter |
| `.gemini/skills/opal-convert/SKILL.md` | C# to OPAL conversion skill |
| `.gemini/settings.json` | **Hook configuration** - enforces OPAL-first development |
| `GEMINI.md` | Project documentation (creates new or updates OPAL section) |

#### OPAL-First Enforcement

Like Claude Code, **Gemini CLI supports hooks** (as of v0.26.0+). The `.gemini/settings.json` file configures a `BeforeTool` hook that **blocks Gemini from creating `.cs` files**.

When Gemini tries to write a C# file, the hook returns a JSON response that blocks the operation and provides guidance:

```json
{
  "decision": "deny",
  "reason": "BLOCKED: Cannot create C# file 'MyClass.cs'",
  "systemMessage": "This is an OPAL-first project. Create an .opal file instead: MyClass.opal\n\nUse @opal skill for OPAL syntax help."
}
```

Gemini will then automatically retry with an `.opal` file. This enforcement ensures all new code is written in OPAL.

**Allowed file types:**
- `.opal` files (always allowed)
- `.g.cs` generated files (build output)
- Files in `obj/` directory (build artifacts)

After initialization, use these Gemini CLI commands:

| Command | Description |
|:--------|:------------|
| `@opal` | Write new OPAL code with Gemini's assistance |
| `@opal-convert` | Convert existing C# code to OPAL syntax |

#### Output Structure

```
project/
├── .gemini/
│   ├── skills/
│   │   ├── opal/
│   │   │   └── SKILL.md
│   │   └── opal-convert/
│   │       └── SKILL.md
│   └── settings.json        # Hook configuration
├── GEMINI.md
└── MyProject.csproj
```

### GitHub Copilot (`--ai github`)

Creates configuration for GitHub Copilot integration.

---

## MSBuild Integration

The `init` command adds three MSBuild targets to your `.csproj` file:

### CompileOpalFiles

Compiles all `.opal` files before C# compilation:

```xml
<Target Name="CompileOpalFiles" BeforeTargets="BeforeBuild">
  <Exec Command="opalc --input %(OpalFiles.Identity) --output $(OpalOutputPath)%(OpalFiles.Filename).g.cs" />
</Target>
```

### IncludeOpalGeneratedFiles

Includes generated `.g.cs` files in the compilation:

```xml
<Target Name="IncludeOpalGeneratedFiles" BeforeTargets="BeforeBuild" DependsOnTargets="CompileOpalFiles">
  <ItemGroup>
    <Compile Include="$(OpalOutputPath)*.g.cs" />
  </ItemGroup>
</Target>
```

### CleanOpalFiles

Cleans generated files when running `dotnet clean`:

```xml
<Target Name="CleanOpalFiles" AfterTargets="Clean">
  <Delete Files="$(OpalOutputPath)*.g.cs" />
</Target>
```

### Output Location

Generated C# files are placed in the intermediate output directory:

```
obj/<Configuration>/<TargetFramework>/opal/
```

For example: `obj/Debug/net8.0/opal/MyModule.g.cs`

This keeps generated files out of your source tree while still making them part of the build.

---

## OPAL/C# Coexistence

After initialization, `.opal` and `.cs` files coexist seamlessly in your project:

```
MyProject/
├── MyProject.csproj        # Updated with OPAL targets
├── Program.cs              # Existing C# code
├── Services/
│   ├── UserService.cs      # Existing C# service
│   └── PaymentService.opal # New OPAL service
└── obj/
    └── Debug/net8.0/opal/
        └── PaymentService.g.cs  # Generated C#
```

### Build Workflow

1. Run `dotnet build`
2. MSBuild triggers `CompileOpalFiles` target
3. Each `.opal` file is compiled to `.g.cs` in `obj/opal/`
4. Generated files are included in C# compilation
5. Normal .NET build continues

### No Manual Steps

- No need to run `opalc` manually
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
opalc init --ai claude

# Specify the solution explicitly
opalc init --solution MyApp.sln --ai claude
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

You can safely run `opalc init` multiple times:

- **CLAUDE.md**: Updates only the OPAL section, preserving your custom content
- **Skills files**: Overwrites with latest version (use `--force` or confirm)
- **.csproj targets**: Skips if already present (idempotent)

---

## Examples

### Basic Initialization

```bash
# Navigate to your project
cd ~/projects/MyApp

# Initialize OPAL (MSBuild integration only)
opalc init

# Analyze codebase for migration candidates
opalc analyze ./src --top 10
```

### Initialize with Claude Code

```bash
# Initialize with Claude Code support
opalc init --ai claude
```

This adds `/opal` and `/opal-convert` skills plus CLAUDE.md with guidelines that instruct Claude to:
- Write new files in OPAL instead of C#
- Analyze C# files before modifying to check if they should be converted first

### Initialize New Project

```bash
# Create a new console app and initialize OPAL
dotnet new console -o MyOpalApp
cd MyOpalApp
opalc init
opalc init --ai claude  # Optional: add Claude support
```

### Initialize a Solution

```bash
# Initialize all projects in a solution (auto-detected)
opalc init --ai claude

# Or specify the solution explicitly
opalc init --solution MySolution.sln --ai claude
```

This creates AI files in the solution root and adds MSBuild targets to all projects:

```
Initialized OPAL solution for Claude Code (opalc v0.1.5)

Solution: MySolution.sln (3 projects)

Created files:
  CLAUDE.md
  .claude/skills/opal/SKILL.md
  .claude/skills/opal-convert/SKILL.md
  .claude/settings.json

Updated projects:
  src/Core/Core.csproj
  src/Web/Web.csproj
  tests/Tests.csproj

MSBuild configuration:
  - Added OPAL compilation targets to 3 projects
```

### Initialize Individual Projects

If you need to initialize projects independently (different AI configs per project):

```bash
opalc init --ai claude --project src/Core/Core.csproj
opalc init --ai claude --project src/Web/Web.csproj
```

---

## Troubleshooting

### "opalc not found"

Ensure the OPAL compiler is installed globally:

```bash
dotnet tool install -g opalc
```

And that your PATH includes the .NET tools directory:

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

### "Multiple .csproj files found"

Specify the exact project:

```bash
opalc init --ai claude --project ./src/MyApp/MyApp.csproj
```

### "Multiple solution files found"

Specify which solution to use:

```bash
opalc init --solution MyApp.sln --ai claude
```

### Build Fails After Init

1. Verify `opalc` is in PATH: `which opalc`
2. Check the `.csproj` has the OPAL targets
3. Try running `opalc` manually on a test file

---

## See Also

- [Adding OPAL to Existing Projects](/opal/guides/adding-opal-to-existing-projects/) - Complete migration guide
- [opalc convert](/opal/cli/convert/) - Convert individual files
- [opalc analyze](/opal/cli/analyze/) - Find migration candidates
- [Claude Integration](/opal/getting-started/claude-integration/) - Using OPAL with Claude Code
- [Codex Integration](/opal/getting-started/codex-integration/) - Using OPAL with OpenAI Codex CLI
- [Gemini Integration](/opal/getting-started/gemini-integration/) - Using OPAL with Google Gemini CLI
