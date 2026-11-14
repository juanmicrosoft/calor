namespace Calor.Compiler.LegacySyntax;

public static class Program
{
    private const string Usage = """
        usage:
          calor-legacy-rewriter calr [--write] [--include-registered] [--log out.tsv] <file-or-dir>...
          calor-legacy-rewriter cs   [--write] [--log out.tsv] [--skip-list skip.txt] <file-or-dir>...
          calor-legacy-rewriter literal <file.cs> <line>  print the §-bearing literal on that line
          calor-legacy-rewriter probe <file.calr>        print the native rewrite (or the oracle failure)
          calor-legacy-rewriter legacy-cs <file.calr>    C# from the legacy parse (today's emitter)
          calor-legacy-rewriter native-cs <file.calr>    C# from today's parse
        Without --write nothing is modified (dry run).
        """;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        var command = args[0];
        var write = args.Contains("--write");
        var includeRegistered = args.Contains("--include-registered");
        string? logPath = null;
        string? skipListPath = null;
        var paths = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--log") logPath = args[++i];
            else if (args[i] == "--skip-list") skipListPath = args[++i];
            else if (!args[i].StartsWith("--", StringComparison.Ordinal)) paths.Add(args[i]);
        }

        switch (command)
        {
            case "probe": return Probe(paths[0]);
            case "literal": return Literal(paths[0], int.Parse(paths[1], System.Globalization.CultureInfo.InvariantCulture));
            case "legacy-cs": return LegacyCSharp(paths[0]);
            case "native-cs": return NativeCSharp(paths[0]);
            case "calr":
            case "cs":
                break;
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }

        var repoRoot = FindRepoRoot(paths.Count > 0 ? paths[0] : Directory.GetCurrentDirectory());
        var exclusions = Exclusions.Load(repoRoot);
        using var log = new Log(logPath, exclusions);
        var cache = new Dictionary<string, RewriteResult>(StringComparer.Ordinal);
        var extension = command == "calr" ? ".calr" : ".cs";
        var files = paths.SelectMany(path => Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*" + extension, SearchOption.AllDirectories)
                : [path])
            .Select(Path.GetFullPath).Distinct().Order(StringComparer.Ordinal).ToList();
        // Skip list: "relative/path.cs:LINE  reason" per line ('#' comments); LINE is the
        // literal's line in the original file.
        var skipList = skipListPath == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : File.ReadAllLines(skipListPath).Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0)
                .Select(l => l.Split(' ', 2)[0]).ToHashSet(StringComparer.Ordinal);
        var literalRewriter = new CSharpLiteralRewriter(log, cache, skipList);
        foreach (var file in files)
        {
            var excluded = exclusions.Reason(file, includeRegistered);
            if (excluded != null)
            {
                if (File.ReadAllText(file).Contains('§'))
                    log.Skip(file, 0, excluded, null);
                continue;
            }
            if (command == "calr")
                CalrFile(file, write, log);
            else
                literalRewriter.ProcessFile(file, write);
        }
        log.Summary(Console.Out);
        if (!write)
            Console.WriteLine("(dry run: nothing written)");
        return 0;
    }

    private static void CalrFile(string path, bool write, Log log)
    {
        var file = TextFile.Read(path);
        if (!file.Text.Contains('§'))
            return;
        var newline = TextFile.UniformNewline(file.Text);
        if (newline == null)
        {
            log.Skip(path, 0, "mixed-line-endings", null);
            return;
        }
        var result = Rewriter.Rewrite(file.Text);
        if (!result.Ok)
        {
            log.Skip(path, 0, result.Reason!, result.Detail);
            return;
        }
        log.Rewritten(path, 0, "calr");
        if (write)
            file.Write(TextFile.Shape(result.Native!, file.Text, newline));
    }

    private static int Probe(string path)
    {
        var text = File.ReadAllText(path);
        var r = Rewriter.Rewrite(text);
        if (r.Ok)
        {
            Console.Write(r.Native);
            return 0;
        }
        Console.Error.WriteLine($"FAIL {r.Reason}: {r.Detail}");
        var dump = Environment.GetEnvironmentVariable("REWRITER_DUMP");
        if (dump != null)
        {
            Directory.CreateDirectory(dump);
            if (r.Candidate != null) File.WriteAllText(Path.Combine(dump, "native.calr"), r.Candidate);
            if (r.CsLegacy != null) File.WriteAllText(Path.Combine(dump, "legacy.cs"), r.CsLegacy);
            if (r.CsNative != null) File.WriteAllText(Path.Combine(dump, "native.cs"), r.CsNative);
        }
        return 1;
    }

    /// <summary>Prints the value of the first §-bearing string literal on a line of a C# file.</summary>
    private static int Literal(string path, int line)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(File.ReadAllText(path));
        foreach (var token in tree.GetRoot().DescendantTokens())
        {
            if (token.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == line && token.ValueText.Contains('§'))
            {
                Console.Write(token.ValueText);
                return 0;
            }
        }
        return 1;
    }

    private static int LegacyCSharp(string path)
    {
        if (!Rewriter.TryLegacyParse(File.ReadAllText(path), out var ast, out var err))
        {
            Console.Error.WriteLine(err);
            return 1;
        }
        Console.Write(new CodeGen.CSharpEmitter().Emit(ast!));
        return 0;
    }

    private static int NativeCSharp(string path)
    {
        var bag = new Diagnostics.DiagnosticBag();
        var ast = new Parsing.Parser(new Parsing.Lexer(File.ReadAllText(path), bag).TokenizeAllForParser(), bag).Parse();
        foreach (var d in bag) Console.Error.WriteLine(d);
        Console.Write(new CodeGen.CSharpEmitter().Emit(ast));
        return 0;
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        if (!dir.Exists) dir = dir.Parent!;
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
