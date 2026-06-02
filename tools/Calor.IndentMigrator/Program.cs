using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;

namespace Calor.IndentMigrator;

/// <summary>
/// Phase 4 bulk migration tool.
///
/// For each `.calr` file under the given root, parses the file with the
/// indent-aware lexer and re-emits it through the migration <see cref="CalorEmitter"/>,
/// which produces the indent-only block form. The result is written back in place.
///
/// Files that fail to parse cleanly are skipped and reported. Files whose
/// emitter output is identical to the original (byte-for-byte after trim) are
/// counted as already-migrated.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var dryRun = args.Contains("--dry-run");
        var verbose = args.Contains("--verbose") || args.Contains("-v");

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"error: directory not found: {root}");
            return 2;
        }

        var files = Directory.EnumerateFiles(root, "*.calr", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Console.WriteLine($"Scanning {files.Length} .calr file(s) under {root}");
        if (dryRun) Console.WriteLine("[dry-run] no files will be written");

        var rewritten = 0;
        var unchanged = 0;
        var skipped = new List<(string Path, string Reason)>();

        foreach (var file in files)
        {
            string original;
            try { original = File.ReadAllText(file); }
            catch (Exception ex)
            {
                skipped.Add((file, $"read failed: {ex.Message}"));
                continue;
            }

            var diagnostics = new DiagnosticBag();
            diagnostics.SetFilePath(file);

            Calor.Compiler.Ast.ModuleNode? module;
            try
            {
                var lexer = new Lexer(original, diagnostics);
                var tokens = lexer.TokenizeAllForParser();
                if (diagnostics.HasErrors)
                {
                    skipped.Add((file, $"lex errors: {diagnostics.Errors.First().Message}"));
                    continue;
                }
                var parser = new Parser(tokens, diagnostics);
                module = parser.Parse();
                if (diagnostics.HasErrors || module is null)
                {
                    skipped.Add((file, $"parse errors: {(diagnostics.Errors.FirstOrDefault()?.Message ?? "null AST")}"));
                    continue;
                }
            }
            catch (Exception ex)
            {
                skipped.Add((file, $"parser threw: {ex.Message}"));
                continue;
            }

            string emitted;
            try
            {
                var emitter = new CalorEmitter();
                emitted = emitter.Emit(module);
            }
            catch (Exception ex)
            {
                skipped.Add((file, $"emit threw: {ex.Message}"));
                continue;
            }

            var originalNorm = NormalizeForCompare(original);
            var emittedNorm = NormalizeForCompare(emitted);

            if (originalNorm == emittedNorm)
            {
                unchanged++;
                if (verbose) Console.WriteLine($"  unchanged: {Relative(root, file)}");
                continue;
            }

            if (!dryRun)
            {
                try { File.WriteAllText(file, emitted); }
                catch (Exception ex)
                {
                    skipped.Add((file, $"write failed: {ex.Message}"));
                    continue;
                }
            }

            rewritten++;
            if (verbose) Console.WriteLine($"  rewrote: {Relative(root, file)}");
        }

        Console.WriteLine();
        Console.WriteLine($"Summary: {rewritten} rewritten, {unchanged} unchanged, {skipped.Count} skipped");
        if (skipped.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Skipped files:");
            foreach (var (path, reason) in skipped.Take(50))
            {
                Console.WriteLine($"  {Relative(root, path)}: {reason}");
            }
            if (skipped.Count > 50) Console.WriteLine($"  ... and {skipped.Count - 50} more");
        }
        return skipped.Count > 0 ? 1 : 0;
    }

    private static string Relative(string root, string path)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }

    private static string NormalizeForCompare(string text)
        => text.Replace("\r\n", "\n").TrimEnd('\n', ' ', '\t');
}
