---
layout: default
title: init
parent: CLI Reference
nav_order: 2
permalink: /cli/init/
---

# opalc init

Initialize OPAL development environment with AI agent support and .csproj integration.

```bash
opalc init --ai <agent> [options]
```

---

## Overview

The `init` command sets up your project for OPAL development by:

1. **Configuring AI agent integration** - Creates skills/prompts for your preferred AI coding assistant
2. **Adding MSBuild targets** - Integrates OPAL compilation into your .NET build process
3. **Setting up project documentation** - Creates or updates documentation files

After running `init`, you can write `.opal` files alongside your `.cs` files and they'll compile automatically during `dotnet build`.

---

## Quick Start

```bash
# Initialize with Claude Code support
opalc init --ai claude

# Initialize with a specific .csproj
opalc init --ai claude --project MyApp.csproj

# Overwrite existing files
opalc init --ai claude --force
```

---

## Options

| Option | Short | Required | Description |
|:-------|:------|:---------|:------------|
| `--ai` | `-a` | Yes | AI agent to configure: `claude`, `codex`, `gemini`, `github` |
| `--project` | `-p` | No | Target .csproj file (auto-detects if single .csproj exists) |
| `--force` | `-f` | No | Overwrite existing files without prompting |

---

## AI Agent Support

### Claude (`--ai claude`)

Creates the following files:

| File | Purpose |
|:-----|:--------|
| `.claude/skills/opal.md` | OPAL code writing skill - teaches Claude OPAL v2+ syntax |
| `.claude/skills/opal-convert.md` | C# to OPAL conversion skill |
| `CLAUDE.md` | Project documentation (creates new or updates OPAL section) |

After initialization, use these Claude Code commands:

| Command | Description |
|:--------|:------------|
| `/opal` | Write new OPAL code with Claude's assistance |
| `/opal-convert` | Convert existing C# code to OPAL syntax |

### Codex (`--ai codex`)

Creates configuration for OpenAI Codex integration.

### Gemini (`--ai gemini`)

Creates configuration for Google Gemini integration.

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

## Project Detection

If you don't specify `--project`, the command auto-detects your project:

| Scenario | Behavior |
|:---------|:---------|
| Single `.csproj` in current directory | Uses that project |
| Multiple `.csproj` files | Prompts you to specify one |
| No `.csproj` found | Creates MSBuild targets file only |

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

### Initialize New Project

```bash
# Create a new console app and initialize OPAL
dotnet new console -o MyOpalApp
cd MyOpalApp
opalc init --ai claude
```

### Initialize Existing Project

```bash
# Navigate to your existing project
cd ~/projects/MyExistingApp

# Initialize with Claude support
opalc init --ai claude

# Or specify the exact project file
opalc init --ai claude --project src/MyApp/MyApp.csproj
```

### Initialize Multiple Projects

```bash
# Initialize each project in a solution
opalc init --ai claude --project src/Core/Core.csproj
opalc init --ai claude --project src/Web/Web.csproj
opalc init --ai claude --project src/Tests/Tests.csproj
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

### Build Fails After Init

1. Verify `opalc` is in PATH: `which opalc`
2. Check the `.csproj` has the OPAL targets
3. Try running `opalc` manually on a test file

---

## See Also

- [Adding OPAL to Existing Projects](/opal/guides/adding-opal-to-existing-projects/) - Complete migration guide
- [opalc convert](/opal/cli/convert/) - Convert individual files
- [opalc analyze](/opal/cli/analyze/) - Find migration candidates
- [Claude Integration](/opal/getting-started/claude-integration/) - Using OPAL with Claude
