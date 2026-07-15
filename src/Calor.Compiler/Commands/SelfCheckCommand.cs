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
            description: "Output format: text (human-readable, stderr) or json (structured document on stdout)");
        formatOption.FromAmong("text", "json");

        var docsCommand = new Command("docs",
            "Check agent-facing docs (CLAUDE.md, syntax reference, CLI docs) against the compiler implementation: " +
            "documented §-keywords must exist in the lexer, cited diagnostic codes in DiagnosticCode, " +
            "effect codes in the effect registry (both directions), and no doc may hardcode the current version")
        {
            rootOption,
            formatOption
        };

        docsCommand.SetHandler((InvocationContext ctx) =>
        {
            var root = ctx.ParseResult.GetValueForOption(rootOption);
            var format = ctx.ParseResult.GetValueForOption(formatOption) ?? "text";
            ctx.ExitCode = Execute(root, format);
        });
        return docsCommand;
    }

    private static int Execute(string? root, string format)
    {
        var resolvedRoot = root ?? FindRepositoryRoot(Directory.GetCurrentDirectory());
        if (resolvedRoot == null)
        {
            Console.Error.WriteLine(
                "error: could not locate a repository root (a directory containing CLAUDE.md and Directory.Build.props); pass --root");
            return 2;
        }

        var diagnostics = new List<Diagnostic>();
        var inputs = DocDriftChecker.LoadFromRepository(resolvedRoot, diagnostics);
        diagnostics.AddRange(DocDriftChecker.Check(inputs));

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            // Structured-output contract: exactly one parseable document on stdout.
            Console.WriteLine(new JsonDiagnosticFormatter().Format(diagnostics));
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
