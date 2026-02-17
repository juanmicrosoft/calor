# Calor Language Skills - Single Source of Truth

This directory contains the **canonical source** for Calor syntax documentation used by both:
- **MCP Server** (`calor_syntax_help` tool)
- **Benchmarks** (LlmTaskRunner, correctness benchmarks)

## Architecture

```
calor-language-skills.md (CANONICAL - edit this one)
         │
         ├──[build time]──→ src/Calor.Compiler/Resources/Skills/calor.md
         │                  (embedded in NuGet package)
         │
         ├──[runtime]──→ SyntaxHelpTool reads this file directly
         │               when running from source
         │
         └──[runtime]──→ LlmTaskRunner loads this for benchmarks
```

## Which File to Edit

**Always edit `calor-language-skills.md` in this directory.**

The file `src/Calor.Compiler/Resources/Skills/calor.md` is automatically synced:
- During build (MSBuild target `SyncSkillFiles`)
- Manually via `./scripts/sync-skill-files.sh`

## How Sync Works

### Automatic (Build Time)
The `Calor.Compiler.csproj` has a target that copies the canonical file to the embedded resource location before every build:

```xml
<Target Name="SyncSkillFiles" BeforeTargets="BeforeBuild">
  <Copy SourceFiles="$(CanonicalSkillFile)" DestinationFiles="$(EmbeddedSkillFile)" />
</Target>
```

### Manual
```bash
./scripts/sync-skill-files.sh        # Sync files
./scripts/sync-skill-files.sh --check # Check if in sync (for CI)
```

### CI Enforcement
The `.github/workflows/test.yml` workflow includes a `skill-file-sync` job that fails if files are out of sync.

## Environment Variable Override

For testing or development, you can override the skill file path:

```bash
export CALOR_SKILL_FILE=/path/to/custom/skills.md
calor mcp --stdio
```

The `SyntaxHelpTool` checks this environment variable first before falling back to the default locations.

## File Structure

The skill file contains:
1. **Core Philosophy** - Design goals
2. **Semantic Guarantees** - Formal semantics
3. **Contract-First Methodology** - Teaching approach for AI agents
4. **Syntax Quick Reference** - All Calor syntax
5. **Complete Examples** - Working code samples
6. **Common Mistakes to Avoid** - Error patterns and fixes
7. **Extended Features** - Advanced tokens (metadata, patterns, etc.)

## Validation

The file is validated by `SkillFileValidationTests`:
- All code blocks must parse without syntax errors
- Token documentation coverage must be ≥75%

## Running Benchmarks

To compare skill file versions:

```bash
./compare-skill-versions.sh --provider claude --sample 5
```

This will:
1. Run benchmarks with the current skill file
2. Swap to a different version (if available)
3. Run benchmarks again
4. Generate a comparison report
