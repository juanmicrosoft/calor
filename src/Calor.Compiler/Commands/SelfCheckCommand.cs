using System.CommandLine;
using System.CommandLine.Invocation;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.SelfCheck;

namespace Calor.Compiler.Commands;

/// <summary>
/// <c>calor self-check docs</c> — machine-checks agent-facing documentation
/// (CLAUDE.md, docs/syntax-reference, docs/cli) against the implementation:
/// §-keywords vs the lexer, diagnostic codes vs DiagnosticCode, effect codes
/// vs the effect registry, and hardcoded version strings. Exits nonzero when
/// drift is found. Phase 1 item 6 of the agent-native strategy.
/// </summary>
public static class SelfCheckCommand
{
    public static Command Create()
    {
        var command = new Command("self-check",
            "Verify that agent-facing artifacts stay in sync with the implementation");
        command.AddCommand(CreateDocsCommand());
        return command;
    }

    private static Command CreateDocsCommand()
    {
        var rootOption = new Option<string?>(
            aliases: ["--root", "-r"],
            description: "Repository root (default: nearest ancestor of the current directory containing CLAUDE.md and Directory.Build.props)");

        var formatOption = new Option<string>(
            aliases: ["--format"],
            getDefaultValue: () => "text",
            description: "Output format: text (human-readable, stderr), json (unified schema on stdout), or sarif (SARIF 2.1.0 on stdout)");
        formatOption.FromAmong("text", "json", "sarif");

        var fixOption = new Option<bool>(
            aliases: ["--fix"],
            description: "Regenerate generated mirror docs (AGENTS.md from CLAUDE.md) instead of only reporting drift");

        var docsCommand = new Command("docs",
            "Check agent-facing docs against the compiler implementation. " +
            "Covered files: CLAUDE.md, docs/syntax-reference/*.md, and docs/cli/*.md " +
            "(plus docs/**/*.md for the version scan). Checks: (1) every documented §-keyword " +
            "exists in the lexer; (2) every cited CalorNNNN diagnostic code exists (and cited " +
            "bands are non-empty); (3) effect codes in docs/syntax-reference/effects.md match " +
            "the effect registry in both directions; (4) the Calor13xx table in " +
            "docs/cli/structured-output.md is complete; (5) no doc hardcodes the current version; " +
            "(6) every fenced ```calor example that declares a complete program (first non-blank " +
            "line starts with §M) parses with the current compiler; (7) AGENTS.md is in sync with its single source CLAUDE.md (--fix regenerates it). " +
            "Suppress an intentional-meta-notation finding by putting <!-- drift:ignore --> on the " +
            "preceding line (see docs/cli/self-check.md). Exits 1 when drift is found")
        {
            rootOption,
            formatOption,
            fixOption
        };

        docsCommand.SetHandler((InvocationContext ctx) =>
        {
            var root = ctx.ParseResult.GetValueForOption(rootOption);
            var format = ctx.ParseResult.GetValueForOption(formatOption) ?? "text";
            var fix = ctx.ParseResult.GetValueForOption(fixOption);
            ctx.ExitCode = Execute(root, format, fix);
        });
        return docsCommand;
    }

    private static int Execute(string? root, string format, bool fix = false)
    {
        var resolvedRoot = root ?? FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (resolvedRoot == null)
        {
            Console.Error.WriteLine(
                "error: could not locate a repository root (a directory containing CLAUDE.md and Directory.Build.props); pass --root");
            return 2;
        }

        if (fix)
        {
            // Regenerate mirror docs, then fall through to the full check so --fix
            // never silently skips the other drift checks (keywords, codes, effects,
            // versions, examples) and reports success. A source-missing error is fatal;
            // an anchor mismatch is surfaced as a finding by the check below.
            switch (DocDriftChecker.RegenerateAgentsMd(resolvedRoot))
            {
                case DocDriftChecker.MirrorRegenResult.SourceMissing:
                    Console.Error.WriteLine("error: CLAUDE.md not found; cannot regenerate AGENTS.md");
                    return 2;
                case DocDriftChecker.MirrorRegenResult.AnchorMismatch:
                    Console.Error.WriteLine(
                        $"warning: CLAUDE.md H1 does not match '{DocDriftChecker.ClaudeTitleAnchor}'; " +
                        "AGENTS.md not regenerated (reported below)");
                    break;
                case DocDriftChecker.MirrorRegenResult.Written:
                    Console.Error.WriteLine($"Regenerated {DocDriftChecker.MirrorAgentsRelativePath} from CLAUDE.md.");
                    break;
                case DocDriftChecker.MirrorRegenResult.AlreadyInSync:
                    Console.Error.WriteLine($"{DocDriftChecker.MirrorAgentsRelativePath} already in sync with CLAUDE.md.");
                    break;
            }
        }

        var diagnostics = new List<Diagnostic>();
        var inputs = DocDriftChecker.LoadFromRepository(resolvedRoot, diagnostics);
        diagnostics.AddRange(DocDriftChecker.Check(inputs));

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase) ||
            format.Equals("sarif", StringComparison.OrdinalIgnoreCase))
        {
            // Structured-output contract: exactly one parseable document on stdout.
            Console.WriteLine(DiagnosticFormatterFactory.Create(format).Format(diagnostics));
        }
        else
        {
            foreach (var diagnostic in diagnostics)
            {
                Console.Error.WriteLine(diagnostic.ToString());
            }

            Console.Error.WriteLine(diagnostics.Count == 0
                ? $"calor self-check docs — no drift found (root: {resolvedRoot})"
                : $"calor self-check docs — {diagnostics.Count} drift finding(s) (root: {resolvedRoot})");
        }

        return diagnostics.Count > 0 ? 1 : 0;
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")) &&
                File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
