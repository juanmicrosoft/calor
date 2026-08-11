using System.CommandLine;
using System.CommandLine.Invocation;
using Calor.Compiler.Refactoring;

namespace Calor.Compiler.Commands;

/// <summary>
/// SymbolId-addressed rename across a project (roadmap §2.5 gate 4).
///
/// The symbol is named by a position, then resolved to an identity; every edit
/// that follows is derived from that identity rather than from the text, and the
/// command refuses rather than guessing whenever the identity is not exact.
/// </summary>
public static class RenameCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "Project directory (or a single .calr file) to rename within");
        var fileOption = new Option<string>(
            aliases: ["--file"],
            description: "File containing the symbol occurrence to rename")
        { IsRequired = true };
        var lineOption = new Option<int>(
            aliases: ["--line"],
            description: "1-based line of the identifier to rename")
        { IsRequired = true };
        var columnOption = new Option<int>(
            aliases: ["--column"],
            description: "1-based column of the identifier to rename")
        { IsRequired = true };
        var toOption = new Option<string>(
            aliases: ["--to"],
            description: "New identifier")
        { IsRequired = true };
        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run"],
            description: "Report the edits without writing them");

        var command = new Command("rename", "Rename a symbol and every reference to it")
        {
            pathArgument, fileOption, lineOption, columnOption, toOption, dryRunOption,
        };

        command.SetHandler((InvocationContext context) =>
        {
            context.ExitCode = Execute(
                context.ParseResult.GetValueForArgument(pathArgument),
                context.ParseResult.GetValueForOption(fileOption)!,
                context.ParseResult.GetValueForOption(lineOption),
                context.ParseResult.GetValueForOption(columnOption),
                context.ParseResult.GetValueForOption(toOption)!,
                context.ParseResult.GetValueForOption(dryRunOption));
        });

        return command;
    }

    private static int Execute(
        string path,
        string file,
        int line,
        int column,
        string newName,
        bool dryRun)
    {
        var files = ResolveSources(path);
        if (files.Count == 0)
        {
            Console.Error.WriteLine($"Error: no .calr files found under '{path}'.");
            return 1;
        }

        var targetPath = Path.GetFullPath(file);
        if (!File.Exists(targetPath))
        {
            Console.Error.WriteLine($"Error: file not found: {file}");
            return 1;
        }

        var index = ProjectSymbolIndex.Build(files, out var skipped);
        foreach (var failure in skipped)
        {
            // Named, not summarised: a file the index could not read is a file
            // whose references cannot be renamed, and the user needs to know
            // which one before trusting the result.
            Console.Error.WriteLine(
                $"Warning: skipped (does not parse or bind): {failure}");
        }

        var source = File.ReadAllText(targetPath);
        var offset = ToOffset(source, line, column);
        if (offset < 0)
        {
            Console.Error.WriteLine($"Error: position {line}:{column} is outside {file}.");
            return 1;
        }

        var occurrence = index.Resolve(targetPath, offset);
        if (occurrence == null)
        {
            Console.Error.WriteLine(
                $"Error: no renameable symbol at {file}:{line}:{column}.");
            return 1;
        }

        var result = RenameEngine.Rename(index, occurrence.SymbolId, newName);
        if (result.Refusal != RenameRefusal.None)
        {
            Console.Error.WriteLine($"Rename refused: {Explain(result.Refusal)}");
            return 1;
        }

        var sources = index.Documents.ToDictionary(
            document => document.FilePath,
            document => document.Source,
            StringComparer.Ordinal);
        var updated = RenameEngine.Apply(sources, result.Edits);

        foreach (var group in result.Edits.GroupBy(edit => edit.FilePath, StringComparer.Ordinal))
            Console.WriteLine($"{group.Key}: {group.Count()} edit(s)");
        Console.WriteLine(
            $"{result.OldName} -> {newName}: {result.Edits.Count} edit(s) in "
                + $"{result.Edits.Select(edit => edit.FilePath).Distinct(StringComparer.Ordinal).Count()} file(s)"
                + (dryRun ? " (dry run, nothing written)" : ""));

        if (dryRun)
            return 0;

        foreach (var group in result.Edits.GroupBy(edit => edit.FilePath, StringComparer.Ordinal))
            File.WriteAllText(group.Key, updated[group.Key]);

        return 0;
    }

    private static string Explain(RenameRefusal refusal) => refusal switch
    {
        RenameRefusal.SymbolNotFound => "the symbol has no indexed occurrences.",
        RenameRefusal.NotAnIdentifier => "the new name is not a valid identifier.",
        RenameRefusal.NameUnchanged => "the new name matches the current name.",
        RenameRefusal.SplitDeclaration =>
            "the declaration spans several files under one name (a module, or a type "
                + "declared across files). Renaming one part would split it silently; "
                + "see issue #922.",
        RenameRefusal.InexactOccurrence =>
            "an occurrence no longer reads as the symbol's name — the sources changed "
                + "under the index.",
        RenameRefusal.NameCollision =>
            "the new name already denotes something in a file this rename would touch.",
        RenameRefusal.TypeReferencesNotIndexed =>
            "type references are not indexed yet, so renaming a type declaration would "
                + "leave its uses pointing at a name that no longer exists.",
        _ => refusal.ToString(),
    };

    private static List<string> ResolveSources(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full))
            return [full];

        return Directory.Exists(full)
            ? Directory.GetFiles(full, "*.calr", SearchOption.AllDirectories)
                .OrderBy(candidate => candidate, StringComparer.Ordinal)
                .ToList()
            : [];
    }

    private static int ToOffset(string source, int line, int column)
    {
        if (line < 1 || column < 1)
            return -1;

        var currentLine = 1;
        var offset = 0;
        while (currentLine < line && offset < source.Length)
        {
            if (source[offset] == '\n')
                currentLine++;
            offset++;
        }

        if (currentLine != line)
            return -1;

        var target = offset + column - 1;
        return target < source.Length ? target : -1;
    }
}
