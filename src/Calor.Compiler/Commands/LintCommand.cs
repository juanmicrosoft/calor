using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Init;
using Calor.Compiler.Parsing;
using Calor.Compiler.Telemetry;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for linting Calor source files.
/// Checks and optionally fixes code for agent-optimal format compliance.
/// </summary>
public static class LintCommand
{
    public static Command Create()
    {
        var inputArgument = new Argument<FileInfo[]>(
            name: "files",
            description: "The Calor source file(s) to lint")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var fixOption = new Option<bool>(
            aliases: ["--fix", "-f"],
            description: "Auto-fix lint issues by reformatting");

        var checkOption = new Option<bool>(
            aliases: ["--check", "-c"],
            description: "Check only, exit 1 if issues found");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show detailed lint issues");

        // Unlike the root command, --format has NO -f short alias here: -f is
        // taken by --fix on lint. Documented in docs/cli/structured-output.md.
        var formatOption = new Option<string>(
            aliases: ["--format"],
            getDefaultValue: () => "text",
            description: "Output format: text (human-readable), json, or sarif (machine-readable diagnostics on stdout). Note: no -f alias on lint (-f means --fix).");
        formatOption.FromAmong("text", "json", "sarif");

        var command = new Command("lint", "Check and fix Calor code for agent-optimal format")
        {
            inputArgument,
            fixOption,
            checkOption,
            verboseOption,
            formatOption
        };

        // Set the exit code through InvocationContext: Main returns InvokeAsync's
        // result as the process exit code, which would stomp a bare
        // Environment.ExitCode assignment (lint --check must exit nonzero on issues).
        command.SetHandler(async (InvocationContext ctx) =>
        {
            ctx.ExitCode = await ExecuteAsync(
                ctx.ParseResult.GetValueForArgument(inputArgument),
                ctx.ParseResult.GetValueForOption(fixOption),
                ctx.ParseResult.GetValueForOption(checkOption),
                ctx.ParseResult.GetValueForOption(verboseOption),
                ctx.ParseResult.GetValueForOption(formatOption) ?? "text");
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(FileInfo[] files, bool fix, bool check, bool verbose, string format)
    {
        var telemetry = CalorTelemetry.IsInitialized ? CalorTelemetry.Instance : null;
        telemetry?.SetCommand("lint");
        if (telemetry != null && files.Length > 0)
        {
            var discovered = CalorConfigManager.Discover(files[0].FullName);
            telemetry.SetAgents(CalorConfigManager.GetAgentString(discovered?.Config));
        }
        var sw = Stopwatch.StartNew();

        // Structured output (--format json|sarif): all diagnostics (parse errors
        // and lint style issues) are aggregated and serialized through the shared
        // DiagnosticFormatter surface to stdout; human-oriented status messages
        // move to stderr so stdout stays machine-parseable.
        var structuredOutput = !format.Equals("text", StringComparison.OrdinalIgnoreCase);
        var diagnosticSink = structuredOutput ? new DiagnosticBag() : null;
        var statusOut = structuredOutput ? Console.Error : Console.Out;

        var totalFiles = 0;
        var filesWithIssues = 0;
        var fixedFiles = 0;
        var errorFiles = 0;
        var totalIssues = 0;

        foreach (var file in files)
        {
            totalFiles++;

            if (!file.Exists)
            {
                diagnosticSink?.Add(new Diagnostic(
                    DiagnosticCode.LintFileNotFound,
                    $"File not found: {file.FullName}",
                    new TextSpan(0, 0, 1, 1),
                    DiagnosticSeverity.Error,
                    file.FullName));
                Console.Error.WriteLine($"Error: File not found: {file.FullName}");
                errorFiles++;
                continue;
            }

            if (!file.Extension.Equals(".calr", StringComparison.OrdinalIgnoreCase))
            {
                // Not lintable — an error in all output modes so `calor lint`
                // never silently exits 0 after skipping its input.
                diagnosticSink?.Add(new Diagnostic(
                    DiagnosticCode.LintUnsupportedFileType,
                    $"Cannot lint non-Calor file: {file.Name}",
                    new TextSpan(0, 0, 1, 1),
                    DiagnosticSeverity.Error,
                    file.FullName));
                Console.Error.WriteLine($"Error: Cannot lint non-Calor file: {file.Name}");
                errorFiles++;
                continue;
            }

            try
            {
                var result = await LintFileAsync(file.FullName, verbose);
                diagnosticSink?.AddRange(result.Diagnostics);

                if (!result.ParseSuccess)
                {
                    if (!structuredOutput)
                    {
                        Console.Error.WriteLine($"Error parsing {file.Name}:");
                        foreach (var error in result.ParseErrors)
                        {
                            Console.Error.WriteLine($"  {error}");
                        }
                    }
                    errorFiles++;
                    continue;
                }

                totalIssues += result.Issues.Count;

                if (result.Issues.Count > 0)
                {
                    filesWithIssues++;

                    if (!structuredOutput && (verbose || (!fix && !check)))
                    {
                        Console.WriteLine($"{file.Name}: {result.Issues.Count} issue(s)");
                        foreach (var issue in result.Issues)
                        {
                            Console.WriteLine($"  Line {issue.Line}: {issue.Message}");
                        }
                    }

                    if (fix)
                    {
                        await File.WriteAllTextAsync(file.FullName, result.FixedContent);
                        statusOut.WriteLine($"Fixed: {file.Name}");
                        fixedFiles++;
                    }
                    else if (check && !structuredOutput)
                    {
                        Console.WriteLine($"Would fix: {file.Name} ({result.Issues.Count} issues)");
                    }
                }
                else if (verbose && !structuredOutput)
                {
                    Console.WriteLine($"{file.Name}: OK");
                }
            }
            catch (Exception ex)
            {
                diagnosticSink?.Add(new Diagnostic(
                    DiagnosticCode.LintProcessingError,
                    $"Error processing {file.Name}: {ex.Message}",
                    new TextSpan(0, 0, 1, 1),
                    DiagnosticSeverity.Error,
                    file.FullName));
                Console.Error.WriteLine($"Error processing {file.Name}: {ex.Message}");
                errorFiles++;
            }
        }

        if (diagnosticSink != null)
        {
            var formatter = DiagnosticFormatterFactory.Create(format);
            Console.WriteLine(formatter.Format(diagnosticSink));
        }

        // Summary
        if (!structuredOutput && (verbose || files.Length > 1))
        {
            Console.WriteLine();
            Console.WriteLine($"Linted {totalFiles} file(s), {totalIssues} issue(s) found");
            if (fix)
            {
                Console.WriteLine($"  Fixed: {fixedFiles}");
            }
            if (errorFiles > 0)
            {
                Console.WriteLine($"  Errors: {errorFiles}");
            }
        }

        // Exit code: 2 = file/processing errors, 1 = lint issues found
        // (without --fix), 0 = clean. Returned (not set via Environment.ExitCode,
        // which Main's InvokeAsync return value would stomp).
        var exitCode = 0;
        if (errorFiles > 0)
        {
            exitCode = 2;
        }
        else if ((check || !fix) && filesWithIssues > 0)
        {
            exitCode = 1;
        }

        sw.Stop();
        telemetry?.TrackCommand("lint", exitCode, new Dictionary<string, string>
        {
            ["durationMs"] = sw.ElapsedMilliseconds.ToString(),
            ["fileCount"] = totalFiles.ToString(),
            ["issueCount"] = totalIssues.ToString()
        });
        if (exitCode != 0)
        {
            IssueReporter.PromptForIssue(telemetry?.OperationId ?? "unknown", "lint", "Lint check failed");
        }

        return exitCode;
    }

    private static async Task<LintResult> LintFileAsync(string filePath, bool verbose)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var issues = new List<LintIssue>();

        // Check source-level issues before parsing
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNum = i + 1;

            // Indentation is now semantically meaningful (Phase 1+ indent form);
            // do not flag leading whitespace.

            // Check for trailing whitespace
            if (line.Length > 0 && line.TrimEnd('\r') != line.TrimEnd('\r').TrimEnd())
            {
                issues.Add(new LintIssue(lineNum, DiagnosticCode.LintTrailingWhitespace, "Line has trailing whitespace"));
            }

            // Check for non-abbreviated IDs: m001, f001, etc.
            var paddedIdMatch = Regex.Match(line, @"§[A-Z/]+\{([a-zA-Z]+)(0+)(\d+)");
            if (paddedIdMatch.Success)
            {
                var prefix = paddedIdMatch.Groups[1].Value;
                var zeros = paddedIdMatch.Groups[2].Value;
                var number = paddedIdMatch.Groups[3].Value;
                var oldId = prefix + zeros + number;
                var newId = prefix + number;
                issues.Add(new LintIssue(lineNum, DiagnosticCode.LintNonAbbreviatedId, $"ID should be abbreviated: use '{newId}' instead of '{oldId}'"));
            }

            // Check for verbose loop/condition IDs: for1, if1, while1, do1
            var verboseIdPatterns = new[]
            {
                (@"§L\{(for)(\d+)", "l"),
                (@"§/L\{(for)(\d+)", "l"),
                (@"§IF\{(if)(\d+)", "i"),
                (@"§/I\{(if)(\d+)", "i"),
                (@"§WHILE\{(while)(\d+)", "w"),
                (@"§/WHILE\{(while)(\d+)", "w"),
                (@"§DO\{(do)(\d+)", "d"),
                (@"§/DO\{(do)(\d+)", "d")
            };

            foreach (var (pattern, replacement) in verboseIdPatterns)
            {
                var match = Regex.Match(line, pattern);
                if (match.Success)
                {
                    var oldId = match.Groups[1].Value + match.Groups[2].Value;
                    var newId = replacement + match.Groups[2].Value;
                    issues.Add(new LintIssue(lineNum, DiagnosticCode.LintNonAbbreviatedId, $"ID should be abbreviated: use '{newId}' instead of '{oldId}'"));
                }
            }

            // Blank lines are now allowed as readability separators (Phase 4 indent form).
        }

        // Parse the file to generate fixed content
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath);

        // Report style issues as warnings so they flow through the shared
        // structured diagnostic output (--format json|sarif).
        foreach (var issue in issues)
        {
            diagnostics.ReportWarning(
                new TextSpan(0, 0, issue.Line, 1),
                issue.Code,
                issue.Message);
        }

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();

        if (diagnostics.HasErrors)
        {
            return new LintResult
            {
                ParseSuccess = false,
                ParseErrors = diagnostics.Errors.Select(e => e.Message).ToList(),
                Issues = issues,
                OriginalContent = source,
                FixedContent = source,
                Diagnostics = diagnostics
            };
        }

        var parser = new Parser(tokens, diagnostics);
        var ast = parser.Parse();

        if (diagnostics.HasErrors)
        {
            return new LintResult
            {
                ParseSuccess = false,
                ParseErrors = diagnostics.Errors.Select(e => e.Message).ToList(),
                Issues = issues,
                OriginalContent = source,
                FixedContent = source,
                Diagnostics = diagnostics
            };
        }

        // Format the AST to get the canonical (fixed) version
        var formatter = new CalorFormatter();
        var fixedContent = formatter.Format(ast);

        return new LintResult
        {
            ParseSuccess = true,
            ParseErrors = new List<string>(),
            Issues = issues,
            OriginalContent = source,
            FixedContent = fixedContent,
            Diagnostics = diagnostics
        };
    }

    private sealed class LintResult
    {
        public bool ParseSuccess { get; init; }
        public required List<string> ParseErrors { get; init; }
        public required List<LintIssue> Issues { get; init; }
        public required string OriginalContent { get; init; }
        public required string FixedContent { get; init; }
        public required DiagnosticBag Diagnostics { get; init; }
    }

    private sealed record LintIssue(int Line, string Code, string Message);
}
