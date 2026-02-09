# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [0.2.1] - 2026-02-08

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.80x (C# leads overall)
- **Metrics**: Calor wins 4, C# wins 4
- **Highlights**:
  - ErrorDetection: 1.55x (Calor wins, large effect)
  - Comprehension: 1.49x (Calor wins, large effect)
  - RefactoringStability: 1.49x (Calor wins, large effect)
  - EditPrecision: 1.36x (Calor wins, large effect)
- **Programs Tested**: 28

### Added
- **Z3 static contract verification** - Prove contracts at compile time with `--verify` flag; proven contracts can have runtime checks elided
- **Manifest-based effect resolution for .NET interop** - Layered resolution from built-in BCL manifests, user manifests, and namespace defaults
- **Granular effect taxonomy** - `fs:r/fs:w`, `net:r/net:w`, `db:r/db:w`, `env:r/env:w` with subtyping (`rw` encompasses `r` and `w`)
- **New CLI commands**: `calor effects resolve`, `calor effects validate`, `calor effects list`
- New CatchBugs component on homepage showing interprocedural effect analysis with compiler error demo

### Changed
- Homepage restructured from 9 to 7 sections for better focus
- Hero updated with value-oriented messaging ("When AI writes your code, the language should catch the bugs")
- CodeComparison updated with ULID-based stable identifiers
- FeatureGrid updated with impact statements and "Learn more" links for all cards
- BenchmarkChart reframed as "Where Explicit Semantics Pay Off"
- QuickStart now includes descriptions under each command
- ProjectStatus now compact with chip-based milestones

### Removed
- Story section from homepage
- CompetitivePositioning section from homepage
- VSCodeExtension section from homepage

## [0.2.0] - 2026-02-07

### Benchmark Results (Statistical: 30 runs)
- **Overall Advantage**: 0.80x (C# leads overall)
- **Metrics**: Calor wins 4, C# wins 4
- **Highlights**:
  - ErrorDetection: 1.55x (Calor wins, large effect)
  - Comprehension: 1.49x (Calor wins, large effect)
  - RefactoringStability: 1.49x (Calor wins, large effect)
  - EditPrecision: 1.36x (Calor wins, large effect)
- **Programs Tested**: 28

### Fixed
- Benchmark calculators now use correct curly brace syntax `{` for Calor patterns instead of square brackets `[`
- This fix enables proper detection of Calor language constructs in RefactoringStability, Comprehension, ErrorDetection, and EditPrecision metrics

## [0.1.9] - 2026-02-06

### Changed
- Documentation updated to remove v1/v2 version references
- Fixed invalid tokens in documentation to match current lexer (§SM, §NN, §CL, §MT, §IV, §TH)

## [0.1.8] - 2026-02-05

### Added
- New documentation page: "The Verification Opportunity" explaining why effects and contracts enforcement is a key value proposition
- "Learn more" links on landing page feature cards for Contracts and Effects

## [0.1.7] - 2026-02-05

### Added
- `calor lint` command for formatting and linting Calor files
- Comprehensive linter regression test suite

### Changed
- **Project renamed from OPAL to Calor**
  - Language name: Calor (was OPAL)
  - CLI tool: `calor` (was `opalc`)
  - File extension: `.calr` (was `.opal`)
  - NuGet packages: `calor`, `Calor.Tasks`, `Calor.Sdk`
- New tagline: "Coding Agent Language for Optimized Reasoning"
- Added project logo
- Enhanced warning messages for non-Claude AI agents (Codex, GitHub Copilot) to clearly indicate they cannot enforce Calor-first development

### Fixed
- Claude skills directory structure now uses correct `SKILL.md` format

## [0.1.4] - 2025-02-03

### Added
- **Multi-AI support**: Added support for GitHub Copilot, OpenAI Codex, and Google Gemini CLI
  - `calor init --ai github` for GitHub Copilot
  - `calor init --ai codex` for OpenAI Codex
  - `calor init --ai gemini` for Google Gemini
- **Solution-level initialization**: `calor init` now works on solution folders, initializing all projects
- Enum support for C# to Calor conversion
- Support for explicit enum values and underlying types
- Calor syntax: `§ENUM{id:Name}` and `§ENUM{id:Name:underlyingType}`
- Type mappings for DateTime, Guid, and read-only collections (ReadList, ReadDict)
- Comprehensive NuGet package metadata (authors, tags, repository URL, license)
- CHANGELOG.md for tracking version history

### Changed
- Renamed to "Coding Agent Language for Optimized Reasoning"
- Documentation links now point to https://juanrivera.github.io/calor
- Updated documentation to reflect current feature support status
- Fixed Claude skills directory structure to match Codex/Gemini pattern

### Fixed
- Clarified that `calor init` should be run in a folder with a C# project or solution

## [0.1.3] - Previous Release
- Claude Code hooks for Calor-first enforcement
- Initial AI integration with Claude

## [0.1.0] - Initial Release
- Initial public release of Calor compiler
