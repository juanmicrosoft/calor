using System.Text.RegularExpressions;
using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #741: no diagnostic may echo an INTERNAL type spelling. Agents pattern-match message
/// text, and the expanded forms (<c>INT</c>, <c>STRING</c>, <c>BOOL</c>, <c>List&lt;STRING&gt;</c>,
/// <c>ARRAY[element=STRING]</c>, <c>Option&lt;INT&gt;</c>) are NOT valid annotation syntax —
/// so a leaked spelling teaches a mistake that produces a new error. Every diagnostic that
/// echoes a type must route it through <c>AttributeHelper.ToSurfaceSpelling</c> or
/// <c>CalorType.SurfaceName</c> (the compact <c>i32</c>/<c>str</c>/<c>bool</c> forms).
///
/// <para>This is the durable backstop the #739 review asked for. Two layers, both with
/// type-checking enabled (the mode the MCP <c>calor_check</c>/<c>calor_refine</c> tools use,
/// where the bulk of the type-echoing surface lives): (1) a curated set of programs that
/// trigger the type-echoing diagnostics — including the sized numeric types
/// (<c>i64</c>/<c>f32</c>/<c>i16</c>) whose expanded form (<c>INT[bits=64]…</c>) is the token
/// family most prone to leak (#748 review); and (2) a scan of the whole in-repo corpus. Every
/// diagnostic message produced must contain no internal spelling token, so a new leak fails
/// here instead of in review.</para>
/// </summary>
public class DiagnosticSurfaceSpellingTests
{
    // Internal spellings that must never appear in message text. The primitive tokens are
    // excluded when they are the legitimate typed-literal keyword the user writes — `INT:42`,
    // `BOOL:true` (a trailing ':'), or the phrase "INT literal" (the lexer echoing the keyword
    // for `INT:`). Everything else uppercase-bare, and the composite forms, is a leak.
    private static readonly (string Name, Regex Pattern)[] ForbiddenSpellings =
    {
        ("STRING", new Regex(@"\bSTRING\b")),
        ("INT",    new Regex(@"\bINT\b(?!:)(?! literal)")),
        ("BOOL",   new Regex(@"\bBOOL\b(?!:)(?! literal)")),
        ("FLOAT",  new Regex(@"\bFLOAT\b(?!:)(?! literal)")),
        ("VOID",   new Regex(@"\bVOID\b")),
        ("UNIT",   new Regex(@"\bUNIT\b")),
        ("NEVER",  new Regex(@"\bNEVER\b")),
        ("ARRAY[element=", new Regex(@"ARRAY\[element=")),
        ("OPTION[inner=",  new Regex(@"OPTION\[inner=")),
        ("RESULT[ok=",     new Regex(@"RESULT\[ok=")),
        ("[bits=",         new Regex(@"\[bits=")),
    };

    private static string? FirstLeak(string message)
    {
        foreach (var (name, pattern) in ForbiddenSpellings)
        {
            if (pattern.IsMatch(message)) return name;
        }
        return null;
    }

    /// <summary>Programs that each trigger a type-echoing diagnostic, so the message text
    /// is actually produced and can be inspected. Type checking is enabled so the
    /// <c>TypeChecker</c> diagnostics (the bulk of the type-echoing surface) fire.</summary>
    public static IEnumerable<object[]> TypeEchoingTriggers() => new[]
    {
        // Calor0254 — array bound to a concrete collection (echoes the collection + element type).
        new object[] { "array-to-list",
            "§M{m:S}\n  §F{f:Do:pub} (str:path) -> i32\n    §E{fs:r}\n" +
            "    §B{lines:List<str>} §C{File.ReadAllLines} §A path §/C\n    §R (len lines)\n" },
        // Calor0256 — type-changing mutable rebind (echoes both types).
        new object[] { "rebind-mismatch",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~x:i32} 0\n    §B{~x:str} \"hi\"\n    §R 0\n" },
        // TypeChecker — IF condition not bool (echoes the got-type and formerly hardcoded
        // BOOL). The condition must PARSE as a valid expression but type as non-bool, so an
        // arithmetic expression (i32) is used rather than a comparison (which would be bool).
        new object[] { "if-cond-not-bool",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §IF{i1} (+ 1 2)\n      §R 1\n    §R 0\n" },
        // TypeChecker — WHILE condition not bool.
        new object[] { "while-cond-not-bool",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §WH{w1} (+ 1 2)\n      §R 1\n    §R 0\n" },
        // TypeChecker — assignment type mismatch (echoes both primitives: str -> i32).
        new object[] { "assign-mismatch",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{x:i32} \"hi\"\n    §R 0\n" },
        // TypeChecker — arithmetic on non-numeric (echoes operand types).
        new object[] { "arith-non-numeric",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{s:str} \"a\"\n    §B{r:i32} (+ s s)\n    §R r\n" },
        // Sized numeric types (#748 review finding 2): these reach the checker as their
        // EXPANDED internal form (INT[bits=64][signed=true], FLOAT[bits=32]) and are the
        // token family most likely to leak `INT`/`[bits=`. Each must surface as i64/f32/i16.
        new object[] { "sized-int-i64",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{x:i64} 0\n    §R 0\n" },
        new object[] { "sized-float-f32",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{y:f32} 0.0\n    §R 0\n" },
        new object[] { "sized-int-i16",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{z:i16} 0\n    §R 0\n" },
        new object[] { "sized-uint-u8",
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{b:u8} 0\n    §R 0\n" },
    };

    [Theory]
    [MemberData(nameof(TypeEchoingTriggers))]
    public void TriggeredDiagnostics_UseSurfaceSpellings(string name, string source)
    {
        var options = new CompilationOptions { EnableTypeChecking = true };
        var result = Program.Compile(source, "surface.calr", options);

        // The point of each trigger is to PRODUCE a diagnostic; if none fired the trigger
        // has rotted and no longer guards anything.
        Assert.True(result.Diagnostics.Any(),
            $"[{name}] expected the program to produce a diagnostic to inspect");

        foreach (var d in result.Diagnostics)
        {
            var leak = FirstLeak(d.Message);
            Assert.True(leak == null,
                $"[{name}] {d.Code} leaks internal spelling '{leak}':\n  {d.Message}");
        }
    }

    [Fact]
    public void SanityCheck_DetectorCatchesEveryForbiddenToken()
    {
        // Guards the guard: EVERY entry in ForbiddenSpellings must be detectable (#748
        // review finding 4 — the earlier sanity check exercised only 3 of 11). A message
        // carrying just that token in isolation must be flagged with that token's name.
        var isolated = new Dictionary<string, string>
        {
            ["STRING"] = "got STRING here",
            ["INT"] = "got INT here",
            ["BOOL"] = "must be BOOL",
            ["FLOAT"] = "got FLOAT here",
            ["VOID"] = "returns VOID",
            ["UNIT"] = "is UNIT",
            ["NEVER"] = "is NEVER",
            ["ARRAY[element="] = "ARRAY[element=i32]",
            ["OPTION[inner="] = "OPTION[inner=i32]",
            ["RESULT[ok="] = "RESULT[ok=i32][err=str]",
            ["[bits="] = "INT[bits=64][signed=true]",
        };
        foreach (var (token, message) in isolated)
        {
            Assert.True(FirstLeak(message) != null,
                $"detector failed to flag forbidden token '{token}' in: {message}");
        }

        // And it must NOT fire on the legitimate surface / typed-literal forms.
        Assert.Null(FirstLeak("Cannot assign str to variable of type i32"));
        Assert.Null(FirstLeak("Invalid INT literal"));          // lexer keyword form
        Assert.Null(FirstLeak("write §Q (>= n INT:0)"));        // typed-literal syntax
        Assert.Null(FirstLeak("expected Option<str>, got i32"));
        Assert.Null(FirstLeak("got i64 and f32"));              // surface sized types
    }

    [Fact]
    public void Corpus_ProducesNoLeakingDiagnostics()
    {
        // Second layer (#748 review finding 4): run every in-repo .calr through the full
        // compiler with type-checking on, and assert whatever diagnostics fire are
        // surface-clean. Corpus files exercise real code — including sized numeric types —
        // that the hand-written triggers might not cover.
        var roots = ResolveCorpusRoots();
        Assert.NotEmpty(roots);

        var files = roots
            .SelectMany(r => Directory.EnumerateFiles(r, "*.calr", SearchOption.AllDirectories))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);

        var leaks = new List<string>();
        foreach (var file in files)
        {
            string source;
            try { source = File.ReadAllText(file); }
            catch { continue; }

            CompilationResult result;
            try
            {
                result = Program.Compile(source, file, new CompilationOptions { EnableTypeChecking = true });
            }
            catch
            {
                continue; // a crashing corpus file is out of scope for this spelling audit
            }

            foreach (var d in result.Diagnostics)
            {
                var leak = FirstLeak(d.Message);
                if (leak != null)
                    leaks.Add($"{Path.GetFileName(file)}: {d.Code} leaks '{leak}' — {d.Message}");
            }
        }

        Assert.True(leaks.Count == 0,
            $"Diagnostics leaked internal type spellings across {files.Count} corpus files:\n  " +
            string.Join("\n  ", leaks));
    }

    private static IReadOnlyList<string> ResolveCorpusRoots()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(DiagnosticSurfaceSpellingTests).Assembly.Location)!);
        while (dir != null)
        {
            var samples = Path.Combine(dir.FullName, "samples");
            var benchmarks = Path.Combine(dir.FullName, "tests", "TestData", "Benchmarks");
            if (Directory.Exists(samples) && Directory.Exists(benchmarks))
            {
                var roots = new List<string>();
                if (Directory.Exists(samples)) roots.Add(samples);
                if (Directory.Exists(benchmarks)) roots.Add(benchmarks);
                return roots;
            }
            dir = dir.Parent;
        }
        return Array.Empty<string>();
    }
}
