# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

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
