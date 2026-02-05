using System.CommandLine;
using System.Text.RegularExpressions;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;

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

        var command = new Command("lint", "Check and fix Calor code for agent-optimal format")
        {
            inputArgument,
            fixOption,
            checkOption,
            verboseOption
        };

        command.SetHandler(ExecuteAsync, inputArgument, fixOption, checkOption, verboseOption);

        return command;
    }

    private static async Task ExecuteAsync(FileInfo[] files, bool fix, bool check, bool verbose)
    {
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
                Console.Error.WriteLine($"Error: File not found: {file.FullName}");
                errorFiles++;
                continue;
            }

            if (!file.Extension.Equals(".calr", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Warning: Skipping non-Calor file: {file.Name}");
                continue;
            }

            try
            {
                var result = await LintFileAsync(file.FullName, verbose);

                if (!result.ParseSuccess)
                {
                    Console.Error.WriteLine($"Error parsing {file.Name}:");
                    foreach (var error in result.ParseErrors)
                    {
                        Console.Error.WriteLine($"  {error}");
                    }
                    errorFiles++;
                    continue;
                }

                totalIssues += result.Issues.Count;

                if (result.Issues.Count > 0)
                {
                    filesWithIssues++;

                    if (verbose || (!fix && !check))
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
                        Console.WriteLine($"Fixed: {file.Name}");
                        fixedFiles++;
                    }
                    else if (check)
                    {
                        Console.WriteLine($"Would fix: {file.Name} ({result.Issues.Count} issues)");
                    }
                }
                else if (verbose)
                {
                    Console.WriteLine($"{file.Name}: OK");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing {file.Name}: {ex.Message}");
                errorFiles++;
            }
        }

        // Summary
        if (verbose || files.Length > 1)
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

        // Exit code
        if (errorFiles > 0)
        {
            Environment.ExitCode = 2;
        }
        else if ((check || !fix) && filesWithIssues > 0)
        {
            Environment.ExitCode = 1;
        }
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

            // Check for leading whitespace (indentation)
            if (line.Length > 0 && char.IsWhiteSpace(line[0]) && line.TrimStart().Length > 0)
            {
                issues.Add(new LintIssue(lineNum, "Line has leading whitespace (indentation not allowed)"));
            }

            // Check for trailing whitespace
            if (line.Length > 0 && line.TrimEnd('\r') != line.TrimEnd('\r').TrimEnd())
            {
                issues.Add(new LintIssue(lineNum, "Line has trailing whitespace"));
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
                issues.Add(new LintIssue(lineNum, $"ID should be abbreviated: use '{newId}' instead of '{oldId}'"));
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
                    issues.Add(new LintIssue(lineNum, $"ID should be abbreviated: use '{newId}' instead of '{oldId}'"));
                }
            }

            // Check for blank lines (empty or whitespace-only)
            if (string.IsNullOrWhiteSpace(line.TrimEnd('\r')))
            {
                issues.Add(new LintIssue(lineNum, "Blank lines not allowed in agent-optimized format"));
            }
        }

        // Parse the file to generate fixed content
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath);

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (diagnostics.HasErrors)
        {
            return new LintResult
            {
                ParseSuccess = false,
                ParseErrors = diagnostics.Errors.Select(e => e.Message).ToList(),
                Issues = issues,
                OriginalContent = source,
                FixedContent = source
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
                FixedContent = source
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
            FixedContent = fixedContent
        };
    }

    private sealed class LintResult
    {
        public bool ParseSuccess { get; init; }
        public required List<string> ParseErrors { get; init; }
        public required List<LintIssue> Issues { get; init; }
        public required string OriginalContent { get; init; }
        public required string FixedContent { get; init; }
    }

    private sealed record LintIssue(int Line, string Message);
}
