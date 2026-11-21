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
/// <c>CalorType.SurfaceName</c> (the native <c>int</c>/<c>string</c>/<c>bool</c> forms).
///
/// <para>0.30 retired the compact aliases (<c>i32</c>, <c>str</c>, <c>f64</c>, …) and the
/// prefix nullable form (<c>?string</c>): the parser rejects them with Calor0100, so a message
/// that echoes one teaches a spelling that no longer parses. They are forbidden here too,
/// except where the message quotes the alias the user wrote in order to retire it.</para>
///
/// <para>This is the durable backstop the #739 review asked for. Two layers, both with
/// type-checking enabled (the mode the MCP <c>calor_check</c>/<c>calor_refine</c> tools use,
/// where the bulk of the type-echoing surface lives): (1) a curated set of programs that
/// trigger the type-echoing diagnostics — including the sized numeric types
/// (<c>long</c>/<c>float</c>/<c>short</c>) whose expanded form (<c>INT[bits=64]…</c>) is the token
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
        // The binder's canonical names for the remaining primitives.
        ("LONG",    new Regex(@"\bLONG\b(?!:)(?! literal)")),
        ("UINT",    new Regex(@"\bUINT\b(?!:)(?! literal)")),
        ("ULONG",   new Regex(@"\bULONG\b(?!:)(?! literal)")),
        ("DECIMAL", new Regex(@"\bDECIMAL\b(?!:)(?! literal)")),
        ("OBJECT",  new Regex(@"\bOBJECT\b(?!:)(?! literal)")),
        ("ARRAY[element=", new Regex(@"ARRAY\[element=")),
        ("NULLABLE[inner=", new Regex(@"NULLABLE\[inner=")),
        ("[bits=",         new Regex(@"\[bits=")),
        // Retired 0.30 aliases. A message may still quote one the user wrote when it says the
        // alias is retired ("Type alias 'i32' is retired").
        ("retired alias", new Regex(@"(?<!alias ')\b(i8|i16|i32|i64|u16|u32|u64|f32|f64|str)\b(?!' is retired)")),
        // Retired prefix nullable (`?string`); the native form is the suffix `string?`.
        ("prefix nullable", new Regex(@"(?<![\w\]>)?])\?[A-Za-z_]")),
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
            "module S S block\n" +
            "  func Do (string:path) → int\n" +
            "    ! fs:r\n" +
            "    let List<string>:lines = File.ReadAllLines(path)\n" +
            "    return lines.Length\n" },
        // Calor0256 — type-changing mutable rebind (echoes both types).
        new object[] { "rebind-mismatch",
            "module S S block\n  func Do () → int\n    !\n    var int:x = 0\n    var string:x = \"hi\"\n    return 0\n" },
        // TypeChecker — IF condition not bool (echoes the got-type and formerly hardcoded
        // BOOL). The condition must PARSE as a valid expression but type as non-bool, so an
        // arithmetic expression (int) is used rather than a comparison (which would be bool).
        new object[] { "if-cond-not-bool",
            "module S S block\n  func Do () → int\n    !\n    if (1 + 2)\n      return 1\n    return 0\n" },
        // TypeChecker — WHILE condition not bool.
        new object[] { "while-cond-not-bool",
            "module S S block\n  func Do () → int\n    !\n    while (1 + 2)\n      return 1\n    return 0\n" },
        // TypeChecker — assignment type mismatch (echoes both primitives: string -> int).
        new object[] { "assign-mismatch",
            "module S S block\n  func Do () → int\n    !\n    let int:x = \"hi\"\n    return 0\n" },
        // TypeChecker — arithmetic on non-numeric (echoes operand types).
        new object[] { "arith-non-numeric",
            "module S S block\n  func Do () → int\n    !\n    let string:s = \"a\"\n    let int:r = (s + s)\n    return r\n" },
        // Sized numeric types (#748 review finding 2): the token family most likely to leak
        // `INT`/`[bits=`. Each must surface as long/float/short/byte.
        //
        // These four used to be CLEAN programs (`let long:x = 0`) that produced a diagnostic only
        // because the opt-in checker could not resolve a sized spelling at all and reported a
        // spurious "Unknown type". That defect is fixed, so those programs now compile —
        // and a trigger that fires on nothing guards nothing, by this test's own rule above.
        // Each is now a genuine type error on a sized binding, which still forces the checker to
        // echo the width the user wrote.
        new object[] { "sized-int-i64",
            "module S S block\n  func Do () → int\n    !\n    let long:x = \"a\"\n    return 0\n" },
        new object[] { "sized-float-f32",
            "module S S block\n  func Do () → int\n    !\n    let float:y = \"a\"\n    return 0\n" },
        new object[] { "sized-int-i16",
            "module S S block\n  func Do () → int\n    !\n    let short:z = \"a\"\n    return 0\n" },
        new object[] { "sized-uint-u8",
            "module S S block\n  func Do () → int\n    !\n    let byte:b = \"a\"\n    return 0\n" },
        // 0.30: a nullable echoes as the suffix `string?`, never the retired prefix `?str`.
        new object[] { "nullable-coalesce",
            "module S S block\n  func Do (string?:input) → void\n    !\n    let string?:fallback = null\n" +
            "    let Option<string>:value = input ?? fallback\n" },
        // Calor0256 on a sized binding: the binder's canonical `LONG` must echo as `long`.
        new object[] { "rebind-sized",
            "module S S block\n  func Do () → int\n    !\n    var long:x = 0\n    var string:x = \"hi\"\n    return 0\n" },
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
            ["LONG"] = "has type 'LONG'",
            ["UINT"] = "got UINT here",
            ["ULONG"] = "got ULONG here",
            ["DECIMAL"] = "got DECIMAL here",
            ["OBJECT"] = "got OBJECT here",
            ["ARRAY[element="] = "ARRAY[element=int]",
            ["NULLABLE[inner="] = "NULLABLE[inner=int]",
            ["[bits="] = "INT[bits=64][signed=true]",
            ["retired alias"] = "Cannot assign str to variable of type i32",
            ["prefix nullable"] = "Change the target to '?string'",
        };
        foreach (var (token, message) in isolated)
        {
            Assert.True(FirstLeak(message) != null,
                $"detector failed to flag forbidden token '{token}' in: {message}");
        }

        // And it must NOT fire on the legitimate surface / typed-literal forms.
        Assert.Null(FirstLeak("Cannot assign string to variable of type int"));
        Assert.Null(FirstLeak("Invalid INT literal"));          // lexer keyword form
        Assert.Null(FirstLeak("write §Q (>= n INT:0)"));        // typed-literal syntax
        Assert.Null(FirstLeak("expected Option<string>, got int"));
        Assert.Null(FirstLeak("got long and float"));           // surface sized types
        Assert.Null(FirstLeak("Change the target to 'string?' or 'List<string?>'"));
        Assert.Null(FirstLeak("Use '?.' or '??' here"));       // operators, not a prefix type
        Assert.Null(FirstLeak("Type alias 'i32' is retired. Use the C# type 'int'."));
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
