using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using System.Text.RegularExpressions;

namespace Calor.Phase3Tier0Verify;

internal static class Program
{
    private static readonly Regex CloserLineRe = new(
        @"^\s*§/[A-Z][A-Z0-9_]*(?:\{[^}]*\})?\s*(//.*)?$",
        RegexOptions.Compiled);

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: dotnet run -- <dir1> [<dir2> ...]");
            return 2;
        }

        var allFiles = new List<string>();
        foreach (var dir in args)
        {
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"skip: {dir} not found");
                continue;
            }
            allFiles.AddRange(Directory.GetFiles(dir, "*.calr", SearchOption.AllDirectories));
        }

        int total = 0, lexClean = 0, indentClean = 0, indentCleanAfterStrip = 0;
        int errLex = 0, errIndent = 0, errStrip = 0;
        long totIndentTokensOrig = 0, totIndentTokensStripped = 0;

        foreach (var fp in allFiles.OrderBy(x => x))
        {
            total++;
            string src;
            try { src = File.ReadAllText(fp); }
            catch { continue; }

            var diagPlain = new DiagnosticBag();
            try
            {
                var lx = new Lexer(src, diagPlain);
                _ = lx.TokenizeAll();
                if (!diagPlain.HasErrors) lexClean++;
                else errLex++;
            }
            catch { errLex++; }

            var diagIndent = new DiagnosticBag();
            try
            {
                var lx = new Lexer(src, diagIndent);
                var toks = lx.TokenizeWithIndentAll();
                totIndentTokensOrig += toks.Count;
                if (!diagIndent.HasErrors) indentClean++;
                else errIndent++;
            }
            catch { errIndent++; }

            var stripped = StripCloserLines(src);
            var diagStripped = new DiagnosticBag();
            try
            {
                var lx = new Lexer(stripped, diagStripped);
                var toks = lx.TokenizeWithIndentAll();
                totIndentTokensStripped += toks.Count;
                if (!diagStripped.HasErrors) indentCleanAfterStrip++;
                else errStrip++;
            }
            catch { errStrip++; }
        }

        Console.WriteLine($"Total .calr files: {total}");
        Console.WriteLine($"  Plain lexer clean (Phase 1 baseline):       {lexClean}/{total} ({lexClean * 100.0 / total:F1}%)");
        Console.WriteLine($"  Indent-pass on closer-mode source (clean):  {indentClean}/{total} ({indentClean * 100.0 / total:F1}%) errors={errIndent}");
        Console.WriteLine($"  Indent-pass on stripped source (clean):     {indentCleanAfterStrip}/{total} ({indentCleanAfterStrip * 100.0 / total:F1}%) errors={errStrip}");
        Console.WriteLine();
        Console.WriteLine($"Total indent-pass tokens (orig):     {totIndentTokensOrig:N0}");
        Console.WriteLine($"Total indent-pass tokens (stripped): {totIndentTokensStripped:N0}");
        if (totIndentTokensOrig > 0)
        {
            var reduction = (totIndentTokensOrig - totIndentTokensStripped) * 100.0 / totIndentTokensOrig;
            Console.WriteLine($"Token reduction from stripping closers: {reduction:F2}%");
        }
        return 0;
    }

    private static string StripCloserLines(string src)
    {
        var kept = new List<string>();
        foreach (var line in src.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (CloserLineRe.IsMatch(trimmed)) continue;
            kept.Add(line);
        }
        return string.Join("\n", kept);
    }
}
