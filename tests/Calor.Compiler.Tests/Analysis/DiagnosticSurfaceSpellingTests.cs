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
/// <para>This is the durable backstop the #739 review asked for: a curated set of programs
/// that trigger every type-echoing diagnostic, plus the whole in-repo corpus, run through
/// the compiler; the message text must contain no internal spelling token. A new diagnostic
/// that leaks fails here instead of in review.</para>
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
    public void SanityCheck_DetectorCatchesAKnownInternalSpelling()
    {
        // Guards the guard: the detector must actually fire on an internal spelling, and
        // must NOT fire on the legitimate surface / typed-literal forms.
        Assert.Equal("STRING", FirstLeak("Cannot assign STRING to variable of type INT"));
        Assert.Equal("INT", FirstLeak("condition must be bool, got INT"));
        Assert.Equal("ARRAY[element=", FirstLeak("value is ARRAY[element=i32]"));
        Assert.Null(FirstLeak("Cannot assign str to variable of type i32"));
        Assert.Null(FirstLeak("Invalid INT literal"));          // lexer keyword form
        Assert.Null(FirstLeak("write §Q (>= n INT:0)"));        // typed-literal syntax
        Assert.Null(FirstLeak("expected Option<str>, got i32"));
    }
}
