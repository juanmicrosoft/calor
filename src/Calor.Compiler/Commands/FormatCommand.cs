using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Init;
using Calor.Compiler.Parsing;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for formatting Calor source files.
/// Provides canonical formatting to ensure consistent style across the codebase.
/// </summary>
public static class FormatCommand
{
    public static Command Create()
    {
        var inputArgument = new Argument<FileInfo[]>(
            name: "files",
            description: "The Calor source file(s) to format")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var checkOption = new Option<bool>(
            aliases: ["--check", "-c"],
            description: "Check if files are formatted without modifying them (exit 1 if not formatted)");

        var writeOption = new Option<bool>(
            aliases: ["--write", "-w"],
            description: "Write formatted output back to the file(s)");

        var diffOption = new Option<bool>(
            aliases: ["--diff", "-d"],
            description: "Show diff of formatting changes");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output");

        var healOption = new Option<bool>(
            aliases: ["--heal"],
            description: "Best-effort source-level repair: strip forbidden structural closers, " +
                         "normalize indentation to 2-space levels from structural nesting, and fix " +
                         "common whitespace issues. Works on files too broken for the AST formatter. Idempotent. " +
                         "WARNING: healing is NOT semantics-preserving — re-anchoring ambiguous statements " +
                         "into a chain-clause body guesses the intended control flow (each guess is reported " +
                         "as a warning). Always review the healed output.");

        // No -f short alias (consistent with lint, where -f means --fix).
        var formatOption = new Option<string>(
            aliases: ["--format"],
            getDefaultValue: () => "text",
            description: "Output format: text (human-readable) or json (envelope document on stdout). No short alias.");
        formatOption.FromAmong("text", "json");

        var command = new Command("format", "Format Calor source files to canonical style")
        {
            inputArgument,
            checkOption,
            writeOption,
            diffOption,
            verboseOption,
            healOption,
            formatOption
        };

        // Return the exit code through the handler (ctx.ExitCode) instead of
        // stomping Environment.ExitCode: the root command's InvokeAsync
        // result is what Program.Main returns, so a code parked only on
        // Environment.ExitCode is overwritten by Main's return value.
        command.SetHandler(async (InvocationContext ctx) =>
        {
            ctx.ExitCode = await ExecuteAsync(
                ctx.ParseResult.GetValueForArgument(inputArgument),
                ctx.ParseResult.GetValueForOption(checkOption),
                ctx.ParseResult.GetValueForOption(writeOption),
                ctx.ParseResult.GetValueForOption(diffOption),
                ctx.ParseResult.GetValueForOption(verboseOption),
                ctx.ParseResult.GetValueForOption(healOption),
                ctx.ParseResult.GetValueForOption(formatOption) ?? "text");
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(FileInfo[] files, bool check, bool write, bool diff, bool verbose, bool heal, string format)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("format");
        if (telemetry != null && files.Length > 0)
        {
            var discovered = CalorConfigManager.Discover(files[0].FullName);
            telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
        }
        var sw = Stopwatch.StartNew();

        // Envelope output (--format json, schema v1.1, loop plan D1.3): real
        // Diagnostic objects are aggregated across files into one bag and
        // serialized once to stdout; ALL human-oriented output goes to stderr
        // so stdout carries exactly one machine-parseable document.
        var json = format.Equals("json", StringComparison.OrdinalIgnoreCase);
        var statusOut = json ? Console.Error : Console.Out;
        var diagnosticSink = json ? new DiagnosticBag() : null;
        var fileEntries = json ? new List<FormatFileData>() : null;
        // The formatted source is embedded in the document only in preview
        // mode (neither --write nor --check), mirroring what text mode would
        // have printed to stdout.
        var embedFormatted = json && !write && !check;

        var hasUnformatted = false;
        var totalFiles = 0;
        var formattedFiles = 0;
        var errorFiles = 0;
        var unrepairedFiles = 0; // heal ran but the output still fails to parse

        foreach (var file in files)
        {
            totalFiles++;

            if (!file.Exists)
            {
                diagnosticSink?.Add(new Diagnostic(
                    DiagnosticCode.FormatFileNotFound,
                    $"File not found: {file.FullName}",
                    new TextSpan(0, 0, 1, 1),
                    DiagnosticSeverity.Error,
                    file.FullName));
                fileEntries?.Add(new FormatFileData
                {
                    Path = file.FullName,
                    Changed = false,
                    Status = "not-found"
                });
                Console.Error.WriteLine($"Error: File not found: {file.FullName}");
                errorFiles++;
                continue;
            }

            if (!file.Extension.Equals(".calr", StringComparison.OrdinalIgnoreCase))
            {
                diagnosticSink?.Add(new Diagnostic(
                    DiagnosticCode.FormatUnsupportedFileType,
                    $"Skipping non-Calor file: {file.Name}",
                    new TextSpan(0, 0, 1, 1),
                    DiagnosticSeverity.Warning,
                    file.FullName));
                fileEntries?.Add(new FormatFileData
                {
                    Path = file.FullName,
                    Changed = false,
                    Status = "skipped"
                });
                Console.Error.WriteLine($"Warning: Skipping non-Calor file: {file.Name}");
                continue;
            }

            try
            {
                var result = heal
                    ? await HealFileAsync(file.FullName)
                    : await FormatFileAsync(file.FullName, verbose);

                diagnosticSink?.AddRange(result.Diagnostics);

                if (!result.Success)
                {
                    fileEntries?.Add(new FormatFileData
                    {
                        Path = file.FullName,
                        Changed = false,
                        Status = "error"
                    });
                    Console.Error.WriteLine($"Error formatting {file.Name}:");
                    foreach (var error in result.Errors)
                    {
                        Console.Error.WriteLine($"  {error}");
                    }
                    errorFiles++;
                    continue;
                }

                var isFormatted = result.Original == result.Formatted;

                if (result.ResidualParseErrors)
                {
                    unrepairedFiles++;
                }

                // Healing is NOT semantics-preserving: surface every
                // control-flow guess with a file:line so the author can
                // review the healed output.
                if (heal && result.Ambiguities.Count > 0)
                {
                    foreach (var ambiguity in result.Ambiguities)
                    {
                        Console.Error.WriteLine(
                            $"Warning: {file.FullName}:{ambiguity.Line}: {ambiguity.Message}");
                    }
                    Console.Error.WriteLine(
                        "Warning: heal guesses control flow when re-anchoring statements and is " +
                        "NOT semantics-preserving — review the healed output.");
                }

                if (!isFormatted)
                {
                    hasUnformatted = true;

                    if (check)
                    {
                        statusOut.WriteLine(heal
                            ? $"Would heal: {file.Name} (ambiguousDecisions: {result.Ambiguities.Count})"
                            : $"Would reformat: {file.Name}");
                    }
                    else if (write)
                    {
                        await File.WriteAllTextAsync(file.FullName, result.Formatted);
                        statusOut.WriteLine($"{(heal ? "Healed" : "Formatted")}: {file.Name}");
                        formattedFiles++;
                    }
                    else if (!json)
                    {
                        // Default: write to stdout (in JSON mode the formatted
                        // source is embedded in the document instead).
                        Console.WriteLine(result.Formatted);
                    }

                    if (diff)
                    {
                        ShowDiff(result.Original, result.Formatted, file.Name, statusOut);
                    }
                }
                else if (verbose)
                {
                    statusOut.WriteLine($"Already formatted: {file.Name}");
                }

                fileEntries?.Add(new FormatFileData
                {
                    Path = file.FullName,
                    Changed = !isFormatted,
                    Status = isFormatted
                        ? "already-formatted"
                        : check ? "would-reformat" : heal ? "healed" : "formatted",
                    Formatted = embedFormatted && !isFormatted ? result.Formatted : null,
                    Ambiguities = heal
                        ? result.Ambiguities
                            .Select(a => new FormatAmbiguityData { Line = a.Line, Message = a.Message })
                            .ToList()
                        : null,
                    ResidualParseErrors = result.ResidualParseErrors
                });
            }
            catch (Exception ex)
            {
                diagnosticSink?.Add(new Diagnostic(
                    DiagnosticCode.FormatProcessingError,
                    $"Error processing {file.Name}: {ex.Message}",
                    new TextSpan(0, 0, 1, 1),
                    DiagnosticSeverity.Error,
                    file.FullName));
                fileEntries?.Add(new FormatFileData
                {
                    Path = file.FullName,
                    Changed = false,
                    Status = "error"
                });
                Console.Error.WriteLine($"Error processing {file.Name}: {ex.Message}");
                errorFiles++;
            }
        }

        // Summary
        if (verbose || files.Length > 1)
        {
            statusOut.WriteLine();
            statusOut.WriteLine($"Processed {totalFiles} file(s)");
            if (write)
            {
                statusOut.WriteLine($"  Formatted: {formattedFiles}");
            }
            if (errorFiles > 0)
            {
                statusOut.WriteLine($"  Errors: {errorFiles}");
            }
            if (unrepairedFiles > 0)
            {
                statusOut.WriteLine($"  Still failing to parse after heal: {unrepairedFiles}");
            }
        }

        // Envelope mode: stdout carries exactly one document, always.
        if (json)
        {
            var data = new FormatData
            {
                Files = fileEntries!,
                Totals = new FormatTotals
                {
                    Processed = totalFiles,
                    Formatted = formattedFiles,
                    Errors = errorFiles,
                    StillFailingAfterHeal = unrepairedFiles
                }
            };
            Console.WriteLine(CommandEnvelope.Serialize("format", diagnosticSink!, data));
        }

        // Exit code. A heal that leaves (or found and could not touch) parse
        // errors must not exit 0 — silence would let an agent loop believe
        // the file was repaired.
        int exitCode = 0;
        if (errorFiles > 0)
        {
            exitCode = 2;
        }
        else if ((check && hasUnformatted) || unrepairedFiles > 0)
        {
            exitCode = 1;
        }

        sw.Stop();
        telemetry?.TrackCommand("format", exitCode, new Dictionary<string, string>
        {
            ["durationMs"] = sw.ElapsedMilliseconds.ToString(),
            ["fileCount"] = totalFiles.ToString(),
            ["errorCount"] = errorFiles.ToString()
        });
        if (exitCode != 0)
        {
            IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "format", "Format check failed");
        }
        return exitCode;
    }

    /// <summary>
    /// <c>--heal</c> path: source-level best-effort repair via
    /// <see cref="SourceHealer"/>. Unlike <see cref="FormatFileAsync"/> this
    /// never requires the input to parse — that is the point: it repairs
    /// files the AST formatter must reject. The healed output is ALWAYS
    /// re-parsed — including when it is identical to the input, so a heal
    /// no-op on a broken file never exits silently — and residual errors are
    /// reported on stderr and drive a nonzero exit code.
    /// </summary>
    private static async Task<FormatResult> HealFileAsync(string filePath)
    {
        var source = await File.ReadAllTextAsync(filePath);

        var healer = new SourceHealer();
        var healed = healer.Heal(source);

        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath);
        var lexer = new Lexer(healed, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        if (!diagnostics.HasErrors)
        {
            var parser = new Parser(tokens, diagnostics);
            parser.Parse();
        }
        if (diagnostics.HasErrors)
        {
            Console.Error.WriteLine(healed == source
                ? $"Note: {Path.GetFileName(filePath)} has parse errors that heal could not repair (no changes made):"
                : $"Note: {Path.GetFileName(filePath)} still has parse errors after healing; heal could not fully repair it:");
            foreach (var error in diagnostics.Errors.Take(5))
            {
                Console.Error.WriteLine($"  {error}");
            }
        }

        return new FormatResult
        {
            Success = true,
            Original = source,
            Formatted = healed,
            Errors = new List<string>(),
            Diagnostics = diagnostics.ToList(),
            Ambiguities = healer.Ambiguities.ToList(),
            ResidualParseErrors = diagnostics.HasErrors
        };
    }

    private static async Task<FormatResult> FormatFileAsync(string filePath, bool verbose)
    {
        var source = await File.ReadAllTextAsync(filePath);

        // Parse the file
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath);

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = diagnostics.Errors.Select(e => e.Message).ToList(),
                Diagnostics = diagnostics.ToList()
            };
        }

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = diagnostics.Errors.Select(e => e.Message).ToList(),
                Diagnostics = diagnostics.ToList()
            };
        }

        // Format the AST
        var formatter = new CalorFormatter();
        var formatted = formatter.Format(ast);

        return new FormatResult
        {
            Success = true,
            Original = source,
            Formatted = formatted,
            Errors = new List<string>(),
            Diagnostics = diagnostics.ToList()
        };
    }

    private static void ShowDiff(string original, string formatted, string fileName, TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine($"--- {fileName} (original)");
        writer.WriteLine($"+++ {fileName} (formatted)");
        writer.WriteLine();

        var originalLines = original.Split('\n');
        var formattedLines = formatted.Split('\n');

        // Simple line-by-line diff (not a proper unified diff, but useful for quick comparison)
        var maxLines = Math.Max(originalLines.Length, formattedLines.Length);
        var contextLines = 3;
        var lastPrintedLine = -1;

        for (var i = 0; i < maxLines; i++)
        {
            var origLine = i < originalLines.Length ? originalLines[i].TrimEnd() : null;
            var fmtLine = i < formattedLines.Length ? formattedLines[i].TrimEnd() : null;

            if (origLine != fmtLine)
            {
                // Print context before
                var startContext = Math.Max(lastPrintedLine + 1, i - contextLines);
                if (startContext < i)
                {
                    if (lastPrintedLine >= 0 && startContext > lastPrintedLine + 1)
                    {
                        writer.WriteLine("...");
                    }
                    for (var j = startContext; j < i; j++)
                    {
                        if (j < originalLines.Length)
                        {
                            writer.WriteLine($" {originalLines[j].TrimEnd()}");
                        }
                    }
                }

                // Print the diff
                if (origLine != null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    writer.WriteLine($"-{origLine}");
                    Console.ResetColor();
                }
                if (fmtLine != null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    writer.WriteLine($"+{fmtLine}");
                    Console.ResetColor();
                }

                lastPrintedLine = i;
            }
        }

        writer.WriteLine();
    }

    private sealed class FormatResult
    {
        public bool Success { get; init; }
        public required string Original { get; init; }
        public required string Formatted { get; init; }
        public required List<string> Errors { get; init; }

        /// <summary>
        /// The real parse/format diagnostics behind <see cref="Errors"/> (plus
        /// heal-mode residual parse errors), carried so <c>--format json</c> can
        /// serialize them through the shared envelope surface.
        /// </summary>
        public List<Diagnostic> Diagnostics { get; init; } = new();

        /// <summary>Control-flow guesses made by the healer (heal path only).</summary>
        public List<HealAmbiguity> Ambiguities { get; init; } = new();

        /// <summary>True when the healed output still fails to parse (heal path only).</summary>
        public bool ResidualParseErrors { get; init; }
    }

    // ------------------------------------------------------------------
    // Envelope `data` payload (--format json). Serialized camelCase with
    // null fields omitted; shape documented in docs/cli/format.md.
    // ------------------------------------------------------------------

    private sealed class FormatData
    {
        public required List<FormatFileData> Files { get; init; }
        public required FormatTotals Totals { get; init; }
    }

    private sealed class FormatFileData
    {
        public required string Path { get; init; }
        public bool Changed { get; init; }

        /// <summary>
        /// formatted | already-formatted | would-reformat | healed | error |
        /// skipped | not-found.
        /// </summary>
        public required string Status { get; init; }

        /// <summary>
        /// The formatted source; present only in preview mode (neither
        /// <c>--write</c> nor <c>--check</c>) for files that changed.
        /// </summary>
        public string? Formatted { get; init; }

        /// <summary>Heal mode only: control-flow guesses made by the healer.</summary>
        public List<FormatAmbiguityData>? Ambiguities { get; init; }

        public bool ResidualParseErrors { get; init; }
    }

    private sealed class FormatAmbiguityData
    {
        public int Line { get; init; }
        public required string Message { get; init; }
    }

    private sealed class FormatTotals
    {
        public int Processed { get; init; }
        public int Formatted { get; init; }
        public int Errors { get; init; }
        public int StillFailingAfterHeal { get; init; }
    }
}
