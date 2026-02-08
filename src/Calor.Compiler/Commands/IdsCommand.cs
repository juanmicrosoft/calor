using System.CommandLine;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Commands;

/// <summary>
/// CLI command for managing Calor IDs.
/// </summary>
public static class IdsCommand
{
    public static Command Create()
    {
        var command = new Command("ids", "Manage Calor declaration IDs")
        {
            CreateCheckCommand(),
            CreateAssignCommand(),
            CreateIndexCommand()
        };

        return command;
    }

    private static Command CreateCheckCommand()
    {
        var pathsArgument = new Argument<string[]>(
            name: "paths",
            description: "Paths to files or directories to check")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var allowTestIdsOption = new Option<bool>(
            aliases: ["--allow-test-ids"],
            description: "Allow test IDs (f001, m001) in all locations");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show detailed output");

        var command = new Command("check", "Validate IDs (missing, duplicates, invalid format)")
        {
            pathsArgument,
            allowTestIdsOption,
            verboseOption
        };

        command.SetHandler(CheckAsync, pathsArgument, allowTestIdsOption, verboseOption);

        return command;
    }

    private static Command CreateAssignCommand()
    {
        var pathsArgument = new Argument<string[]>(
            name: "paths",
            description: "Paths to files or directories to process")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run", "-n"],
            description: "Show changes without modifying files");

        var fixDuplicatesOption = new Option<bool>(
            aliases: ["--fix-duplicates"],
            description: "Reassign duplicate IDs (keeps first occurrence)");

        var allowTestIdsOption = new Option<bool>(
            aliases: ["--allow-test-ids"],
            description: "Allow test IDs (f001, m001) - skip those without assigning");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show detailed output");

        var command = new Command("assign", "Add missing IDs to declarations")
        {
            pathsArgument,
            dryRunOption,
            fixDuplicatesOption,
            allowTestIdsOption,
            verboseOption
        };

        command.SetHandler(AssignAsync, pathsArgument, dryRunOption, fixDuplicatesOption, allowTestIdsOption, verboseOption);

        return command;
    }

    private static Command CreateIndexCommand()
    {
        var pathsArgument = new Argument<string[]>(
            name: "paths",
            description: "Paths to files or directories to index")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var outputOption = new Option<FileInfo?>(
            aliases: ["--output", "-o"],
            description: "Output file path (default: calor.ids.json)");

        var command = new Command("index", "Generate an index of all IDs in the project")
        {
            pathsArgument,
            outputOption
        };

        command.SetHandler(IndexAsync, pathsArgument, outputOption);

        return command;
    }

    private static async Task CheckAsync(string[] paths, bool allowTestIds, bool verbose)
    {
        var files = CollectFiles(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("No .calr files found");
            Environment.ExitCode = 1;
            return;
        }

        if (verbose)
        {
            Console.WriteLine($"Checking {files.Count} file(s)...");
        }

        var allEntries = new List<IdEntry>();

        foreach (var file in files)
        {
            var entries = await ScanFileAsync(file, verbose);
            if (entries != null)
            {
                allEntries.AddRange(entries);
            }
        }

        var result = IdChecker.Check(allEntries, allowTestIds);

        if (result.IsValid)
        {
            Console.WriteLine($"All {allEntries.Count} IDs are valid.");
            Environment.ExitCode = 0;
            return;
        }

        // Report issues
        var diagnostics = IdChecker.GenerateDiagnostics(result).ToList();

        foreach (var diag in diagnostics.OrderBy(d => d.FilePath).ThenBy(d => d.Span.Line))
        {
            Console.Error.WriteLine(diag.ToString());
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"Found {result.TotalIssues} issue(s):");
        if (result.MissingIds.Count > 0)
            Console.Error.WriteLine($"  Missing IDs: {result.MissingIds.Count}");
        if (result.InvalidFormatIds.Count > 0)
            Console.Error.WriteLine($"  Invalid format: {result.InvalidFormatIds.Count}");
        if (result.WrongPrefixIds.Count > 0)
            Console.Error.WriteLine($"  Wrong prefix: {result.WrongPrefixIds.Count}");
        if (result.TestIdsInProduction.Count > 0)
            Console.Error.WriteLine($"  Test IDs in production: {result.TestIdsInProduction.Count}");
        if (result.DuplicateGroups.Count > 0)
            Console.Error.WriteLine($"  Duplicate groups: {result.DuplicateGroups.Count}");

        Environment.ExitCode = 1;
    }

    private static async Task AssignAsync(string[] paths, bool dryRun, bool fixDuplicates, bool allowTestIds, bool verbose)
    {
        var files = CollectFiles(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("No .calr files found");
            Environment.ExitCode = 1;
            return;
        }

        if (verbose || dryRun)
        {
            Console.WriteLine($"Processing {files.Count} file(s)...");
        }

        // First pass: collect existing IDs to avoid duplicates across files
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!fixDuplicates)
        {
            foreach (var file in files)
            {
                var entries = await ScanFileAsync(file, verbose: false);
                if (entries != null)
                {
                    foreach (var entry in entries.Where(e => !string.IsNullOrEmpty(e.Id)))
                    {
                        // Skip test IDs if allowed
                        if (allowTestIds && entry.IsTestId)
                            continue;

                        existingIds.Add(entry.Id);
                    }
                }
            }
        }

        var totalAssigned = 0;
        var totalDuplicatesFixed = 0;
        var modifiedFiles = new List<string>();
        var allAssignments = new List<IdAssignment>();

        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file);
            var (newContent, assignments) = IdAssigner.AssignIds(content, file, fixDuplicates, existingIds);

            // Update closing tags
            if (assignments.Count > 0)
            {
                newContent = IdAssigner.UpdateClosingTags(newContent, assignments);
            }

            if (assignments.Count > 0)
            {
                allAssignments.AddRange(assignments);
                totalAssigned += assignments.Count(a => string.IsNullOrEmpty(a.OldId));
                totalDuplicatesFixed += assignments.Count(a => !string.IsNullOrEmpty(a.OldId));

                if (dryRun)
                {
                    Console.WriteLine($"{Path.GetFileName(file)}:");
                    foreach (var assignment in assignments)
                    {
                        if (string.IsNullOrEmpty(assignment.OldId))
                        {
                            Console.WriteLine($"  Line {assignment.Line}: {assignment.Kind} '{assignment.Name}' -> {assignment.NewId}");
                        }
                        else
                        {
                            Console.WriteLine($"  Line {assignment.Line}: {assignment.Kind} '{assignment.Name}': {assignment.OldId} -> {assignment.NewId} (duplicate)");
                        }
                    }
                }
                else
                {
                    await File.WriteAllTextAsync(file, newContent);
                    modifiedFiles.Add(file);

                    if (verbose)
                    {
                        Console.WriteLine($"Modified: {Path.GetFileName(file)} ({assignments.Count} ID(s))");
                    }
                }
            }
        }

        // Summary
        if (dryRun)
        {
            Console.WriteLine();
            Console.WriteLine($"Dry run complete. Would assign {totalAssigned} new ID(s), fix {totalDuplicatesFixed} duplicate(s).");
        }
        else
        {
            Console.WriteLine($"Assigned {totalAssigned} ID(s), fixed {totalDuplicatesFixed} duplicate(s) across {modifiedFiles.Count} file(s).");
        }

        Environment.ExitCode = 0;
    }

    private static async Task IndexAsync(string[] paths, FileInfo? output)
    {
        var files = CollectFiles(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("No .calr files found");
            Environment.ExitCode = 1;
            return;
        }

        var allEntries = new List<IdEntry>();

        foreach (var file in files)
        {
            var entries = await ScanFileAsync(file, verbose: false);
            if (entries != null)
            {
                allEntries.AddRange(entries);
            }
        }

        var outputPath = output?.FullName ?? "calor.ids.json";

        // Generate JSON index
        var json = System.Text.Json.JsonSerializer.Serialize(
            allEntries.Select(e => new
            {
                id = e.Id,
                kind = e.Kind.ToString(),
                name = e.Name,
                file = Path.GetRelativePath(Directory.GetCurrentDirectory(), e.FilePath),
                line = e.Span.Line
            }),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(outputPath, json);

        Console.WriteLine($"Indexed {allEntries.Count} ID(s) to {outputPath}");
        Environment.ExitCode = 0;
    }

    private static List<string> CollectFiles(string[] paths)
    {
        var files = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                files.AddRange(Directory.GetFiles(path, "*.calr", SearchOption.AllDirectories));
            }
            else if (File.Exists(path) && path.EndsWith(".calr", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(path);
            }
            else if (path == ".")
            {
                files.AddRange(Directory.GetFiles(Directory.GetCurrentDirectory(), "*.calr", SearchOption.AllDirectories));
            }
        }

        return files.Distinct().ToList();
    }

    private static async Task<IReadOnlyList<IdEntry>?> ScanFileAsync(string filePath, bool verbose)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var diagnostics = new DiagnosticBag();
            diagnostics.SetFilePath(filePath);

            var lexer = new Lexer(content, diagnostics);
            var tokens = lexer.TokenizeAll();

            if (diagnostics.HasErrors)
            {
                if (verbose)
                {
                    Console.Error.WriteLine($"Error lexing {Path.GetFileName(filePath)}");
                }
                return null;
            }

            var parser = new Parser(tokens, diagnostics);
            var module = parser.Parse();

            if (diagnostics.HasErrors)
            {
                if (verbose)
                {
                    Console.Error.WriteLine($"Error parsing {Path.GetFileName(filePath)}");
                }
                return null;
            }

            var scanner = new IdScanner();
            return scanner.Scan(module, filePath);
        }
        catch (Exception ex)
        {
            if (verbose)
            {
                Console.Error.WriteLine($"Error processing {Path.GetFileName(filePath)}: {ex.Message}");
            }
            return null;
        }
    }
}
