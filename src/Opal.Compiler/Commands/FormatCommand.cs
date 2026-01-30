using System.CommandLine;
using Opal.Compiler.Diagnostics;
using Opal.Compiler.Formatting;
using Opal.Compiler.Parsing;

namespace Opal.Compiler.Commands;

/// <summary>
/// CLI command for formatting OPAL source files.
/// Provides canonical formatting to ensure consistent style across the codebase.
/// </summary>
public static class FormatCommand
{
    public static Command Create()
    {
        var inputArgument = new Argument<FileInfo[]>(
            name: "files",
            description: "The OPAL source file(s) to format")
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

        var command = new Command("format", "Format OPAL source files to canonical style")
        {
            inputArgument,
            checkOption,
            writeOption,
            diffOption,
            verboseOption
        };

        command.SetHandler(ExecuteAsync, inputArgument, checkOption, writeOption, diffOption, verboseOption);

        return command;
    }

    private static async Task ExecuteAsync(FileInfo[] files, bool check, bool write, bool diff, bool verbose)
    {
        var hasUnformatted = false;
        var totalFiles = 0;
        var formattedFiles = 0;
        var errorFiles = 0;

        foreach (var file in files)
        {
            totalFiles++;

            if (!file.Exists)
            {
                Console.Error.WriteLine($"Error: File not found: {file.FullName}");
                errorFiles++;
                continue;
            }

            if (!file.Extension.Equals(".opal", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Warning: Skipping non-OPAL file: {file.Name}");
                continue;
            }

            try
            {
                var result = await FormatFileAsync(file.FullName, verbose);

                if (!result.Success)
                {
                    Console.Error.WriteLine($"Error formatting {file.Name}:");
                    foreach (var error in result.Errors)
                    {
                        Console.Error.WriteLine($"  {error}");
                    }
                    errorFiles++;
                    continue;
                }

                var isFormatted = result.Original == result.Formatted;

                if (!isFormatted)
                {
                    hasUnformatted = true;

                    if (check)
                    {
                        Console.WriteLine($"Would reformat: {file.Name}");
                    }
                    else if (write)
                    {
                        await File.WriteAllTextAsync(file.FullName, result.Formatted);
                        Console.WriteLine($"Formatted: {file.Name}");
                        formattedFiles++;
                    }
                    else
                    {
                        // Default: write to stdout
                        Console.WriteLine(result.Formatted);
                    }

                    if (diff)
                    {
                        ShowDiff(result.Original, result.Formatted, file.Name);
                    }
                }
                else if (verbose)
                {
                    Console.WriteLine($"Already formatted: {file.Name}");
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
            Console.WriteLine($"Processed {totalFiles} file(s)");
            if (write)
            {
                Console.WriteLine($"  Formatted: {formattedFiles}");
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
        else if (check && hasUnformatted)
        {
            Environment.ExitCode = 1;
        }
    }

    private static async Task<FormatResult> FormatFileAsync(string filePath, bool verbose)
    {
        var source = await File.ReadAllTextAsync(filePath);

        // Parse the file
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath(filePath);

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        if (diagnostics.HasErrors)
        {
            return new FormatResult
            {
                Success = false,
                Original = source,
                Formatted = source,
                Errors = diagnostics.Errors.Select(e => e.Message).ToList()
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
                Errors = diagnostics.Errors.Select(e => e.Message).ToList()
            };
        }

        // Format the AST
        var formatter = new OpalFormatter();
        var formatted = formatter.Format(ast);

        return new FormatResult
        {
            Success = true,
            Original = source,
            Formatted = formatted,
            Errors = new List<string>()
        };
    }

    private static void ShowDiff(string original, string formatted, string fileName)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {fileName} (original)");
        Console.WriteLine($"+++ {fileName} (formatted)");
        Console.WriteLine();

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
                        Console.WriteLine("...");
                    }
                    for (var j = startContext; j < i; j++)
                    {
                        if (j < originalLines.Length)
                        {
                            Console.WriteLine($" {originalLines[j].TrimEnd()}");
                        }
                    }
                }

                // Print the diff
                if (origLine != null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"-{origLine}");
                    Console.ResetColor();
                }
                if (fmtLine != null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"+{fmtLine}");
                    Console.ResetColor();
                }

                lastPrintedLine = i;
            }
        }

        Console.WriteLine();
    }

    private sealed class FormatResult
    {
        public bool Success { get; init; }
        public required string Original { get; init; }
        public required string Formatted { get; init; }
        public required List<string> Errors { get; init; }
    }
}
