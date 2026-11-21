using System.CommandLine;
using System.Text;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Formatting;

namespace Calor.Compiler.Commands;

/// <summary>
/// <c>calor fix --apply</c> applies the safe, deterministic patches that diagnostics
/// carry (<c>--dry-run</c> reports them without writing). The archived section-tag
/// migrations (<c>--elide-call-closers</c>, <c>--heal-closers</c>) were removed in 0.30
/// with the <c>§</c> syntax (#1598).
/// </summary>
public static class FixCommand
{
    public static Command Create()
    {
        var rootArgument = new Argument<string>(
            name: "root",
            getDefaultValue: () => ".",
            description: "Calor file or root directory (default: current directory)")
        {
            Arity = ArgumentArity.ZeroOrOne
        };

        var applyOption = new Option<bool>("--apply", "Apply deterministic safe diagnostic patches");
        var noImplicitSystemOption = new Option<bool>("--no-implicit-system", "Validate without the implicit System project import");
        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run", "-n"],
            description: "Report what would change without writing files");

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "text",
            description: "Output format: text or json (envelope v1.1)");

        var command = new Command("fix", "Apply safe diagnostic patches")
        {
            rootArgument,
            applyOption,
            noImplicitSystemOption,
            dryRunOption,
            formatOption,
        };

        command.SetHandler(async (System.CommandLine.Invocation.InvocationContext context) =>
        {
            var parse = context.ParseResult;
            var json = string.Equals(parse.GetValueForOption(formatOption), "json", StringComparison.OrdinalIgnoreCase);
            if (!parse.GetValueForOption(applyOption))
            {
                const string message = "specify --apply";
                EmitErrorEnvelope(json, DiagnosticCode.CliUsageError, message, filePath: null);
                Console.Error.WriteLine(message);
                context.ExitCode = 2;
                return;
            }
            context.ExitCode = await ApplyDiagnosticsAsync(
                parse.GetValueForArgument(rootArgument),
                parse.GetValueForOption(dryRunOption), json,
                !parse.GetValueForOption(noImplicitSystemOption));
        });

        return command;
    }

    /// <summary>
    /// Envelope mode (--format json): stdout carries exactly one document,
    /// always — including early-exit error paths, which get a CLI-band
    /// diagnostic (Calor1310 missing input, Calor1311 usage error).
    /// </summary>
    private static void EmitErrorEnvelope(bool json, string code, string message, string? filePath)
    {
        if (!json)
        {
            return;
        }
        Console.WriteLine(EnvelopeWriter.Serialize("fix", null,
            [new Diagnostic(code, DiagnosticSeverity.Error, message, filePath, line: 1, column: 1)]));
    }

    private static async Task<int> ApplyDiagnosticsAsync(string target, bool dryRun, bool json,
        bool includeImplicitSystemUsing)
    {
        var diagnostics = new DiagnosticBag();
        var changedFiles = 0;
        var editsApplied = 0;
        try
        {
            var fullPath = Path.GetFullPath(target);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                diagnostics.Add(new Diagnostic(DiagnosticCode.CliInputNotFound, DiagnosticSeverity.Error,
                    "Input path not found", fullPath, 1, 1));
            }
            else
            {
                var paths = File.Exists(fullPath) ? [fullPath]
                    : EnumerateCalrFiles(new DirectoryInfo(fullPath)).Order(StringComparer.Ordinal).ToArray();
                if (paths.Length == 0 || paths.Any(path => !path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Expected a .calr file or a directory containing .calr files.");

                foreach (var path in paths)
                {
                    var snapshot = await SourceFileSnapshot.ReadAsync(path);
                    var fixedSource = DiagnosticFixes.Repair(snapshot.Text, snapshot.Path,
                        new CompilationOptions { IncludeImplicitSystemUsing = includeImplicitSystemUsing },
                        out var result, out var applied);
                    if (fixedSource != snapshot.Text)
                    {
                        if (!dryRun)
                        {
                            await SafeSourceFile.WriteParsedAsync(snapshot, fixedSource, persisted =>
                            {
                                if (!string.Equals(persisted, fixedSource, StringComparison.Ordinal))
                                    throw new InvalidDataException("Persisted source does not match the validated patch.");
                            });
                        }
                        changedFiles++;
                        editsApplied += applied;
                    }
                    // A preview describes the unchanged disk, not a fictional successful repair.
                    if (dryRun)
                        result = Program.Compile(snapshot.Text, snapshot.Path);
                    diagnostics.AddRange(result.Diagnostics);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            diagnostics.Add(new Diagnostic(DiagnosticCode.CliInternalError, DiagnosticSeverity.Error,
                $"Fix failed: {ex.Message}", target, 1, 1));
        }

        if (json)
            Console.WriteLine(EnvelopeWriter.Serialize("fix",
                new { operation = "apply", dryRun, changedFiles, editsApplied }, diagnostics));
        else
        {
            foreach (var diagnostic in diagnostics)
                Console.Error.WriteLine(diagnostic);
            Console.WriteLine($"fix: {(dryRun ? "[dry-run] " : "")}files_changed={changedFiles} edits={editsApplied} errors={diagnostics.Errors.Count} warnings={diagnostics.Warnings.Count}");
        }
        return diagnostics.HasErrors ? 1 : 0;
    }

    private static IEnumerable<string> EnumerateCalrFiles(DirectoryInfo root)
    {
        return Directory.EnumerateFiles(root.FullName, "*.calr", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
