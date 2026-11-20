using Calor.Compiler.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1613: diagnostics are read by agents on every iteration, so the median rendered
/// diagnostic has a token budget, and every rendered diagnostic is one line.
///
/// <para>The corpus is a fixed set of small erroneous programs, each targeting one
/// diagnostic across the lexer (Calor00xx), parser (Calor01xx), semantic (Calor02xx),
/// effects (Calor04xx) and generated-C# (Calor1002) bands. Most are copied from the
/// negative inputs of existing tests (LexerTests, PrimerMistakesRejectedTests,
/// SilentAcceptanceLanguageTests, NativeEffectHeaderTests, NativeEffectSuggestionTests,
/// AgentDiagnosticTests, ExemplarCompilesTests); the rest are minimal programs written for
/// this corpus. They are inlined so edits to other fixtures cannot move the median.
/// Each entry must still produce its code, so the corpus cannot shrink silently.</para>
///
/// <para>Each program is compiled in process with <see cref="Program.Compile(string, string?, bool)"/>
/// and rendered by <see cref="TextDiagnosticFormatter"/>, the CLI's default
/// <c>--format text</c> output (<c>file:line:col code message</c>). Tokens are o200k proxy
/// counts; see <see cref="ProxyTokenizer"/>.</para>
///
/// <para>Budget set 2026-11-20: today's median of the 44 targeted diagnostics is 30 tokens,
/// so the budget is the next multiple of 10 above it, 40. Change it only by a deliberate edit with
/// the reason next to the constant.</para>
/// </summary>
public sealed class DiagnosticTokenBudgetTests
{
    /// <summary>2026-11-20: measured median 30 o200k tokens over the 44-entry corpus below.</summary>
    private const int MedianDiagnosticBudget = 40;

    /// <summary>The file name every corpus program is compiled under, so paths are deterministic.</summary>
    private const string FileName = "src/Program.calr";

    private sealed record Case(string Name, string Code, string Source);

    private static readonly Case[] Corpus =
    [
        new("lex_section_marker", "Calor0006",
            "module Bad\n  func Main () → void\n    §INVALID_TAG\n"),
        new("parse_four_field", "Calor0006",
            "module Probe\n  §F{f1:Add:int:pub}\n    return 0\n"),
        new("parse_legacy_closer", "Calor0006",
            "module Probe\n  func Probe (int:x) → int\n    !\n    return x\n  §/F\n"),
        new("lex_unterminated_string", "Calor0002",
            "module Bad\n  func Main () → void\n    ! cw\n    System.Console.WriteLine(\"unfinished\n"),
        new("parse_typed_literal", "Calor0003",
            "module Bad\n  func F () → int\n    !\n    return INT:42\n"),
        new("lex_tab_indent", "Calor0008",
            "module Bad\n\tfunc Main () → int\n\t\t!\n\t\treturn 1\n"),
        new("lex_unterminated_char", "Calor0112",
            "module Bad\n  func Main () → char\n    !\n    return 'a\n"),
        new("parse_empty_arg", "Calor0100",
            "module Bad\n  func Main () → void\n    return f(,)\n"),
        new("parse_double_operator", "Calor0100",
            "module Bad\n  func F (int:x) → int\n    !\n    return x + * 2\n"),
        new("parse_for_missing_to", "Calor0100",
            "module Bad\n  func F () → void\n    ! cw\n    for i from 1 5\n      Console.WriteLine(i)\n"),
        new("parse_missing_paren", "Calor0100",
            "module Bad\n  func F (int:x → int\n    !\n    return x\n"),
        new("parse_param_no_type", "Calor0100",
            "module Bad\n  func F (x) → int\n    !\n    return x\n"),
        new("parse_unclosed_call", "Calor0100",
            "module Bad\n  func F (int:x) → int\n    !\n    return Math.Abs(x\n"),
        new("parse_if_braces", "Calor0100",
            "module Probe\n  func Probe (int:x) → int\n    !\n    if {x > 0}\n      return 1\n    return x\n"),
        new("parse_if_missing_cond", "Calor0100",
            "module Probe\n  func Probe (int:x) → int\n    !\n    if\n      return 1\n    return x\n"),
        new("parse_bad_visibility", "Calor0107",
            "module V V block\n  class K\n    func A publik () → int\n      !\n      return 1\n"),
        new("parse_else_without_if", "Calor0117",
            "module Bad\n  func F (int:x) → int\n    !\n    else\n      return 1\n    return x\n"),
        new("parse_effect_after_body", "Calor0405",
            "module Bad\n  func F (int:x) → int\n    let int:y = x\n    !\n    return y\n"),
        new("parse_uppercase_result", "Calor0200",
            "module Probe\n  func Probe (int:x) → int\n    !\n    => RESULT >= 0\n    return x\n"),
        new("sem_undefined_var", "Calor0200",
            "module Sem\n  func F () → int\n    !\n    return missing + 1\n"),
        new("sem_unknown_type", "Calor0200",
            "module Sem\n  func F (Frob:x) → int\n    !\n    return 1\n"),
        new("sem_bool_condition", "Calor0202",
            "module Sem\n  func F (int:x) → int\n    !\n    if x\n      return 1\n    return 0\n"),
        new("sem_null_to_int", "Calor0202",
            "module Sem\n  func F () → int\n    !\n    let int:x = null\n    return x\n"),
        new("sem_precondition_type", "Calor0202",
            "module Sem\n  func F (int:x | (# + 1)) → int\n    !\n    return x\n"),
        new("sem_postcondition_bool", "Calor0202",
            "module Sem\n  func F (int:x) → int\n    !\n    => result + 1\n    return x\n"),
        new("sem_string_plus_bool", "Calor0202",
            "module Sem\n  func F () → bool\n    !\n    return 1 + true\n"),
        new("sem_readalllines_list", "Calor0202",
            "module Sem\n  func F (string:p) → int\n    ! fs:r\n    let List<string>:lines = File.ReadAllLines(p)\n    return lines.Count\n"),
        new("sem_return_void_value", "Calor0205",
            "module Sem\n  func F () → void\n    !\n    return 1\n"),
        new("sem_duplicate_function", "Calor0206",
            "module Sem\n  func F () → int\n    !\n    return 1\n  func F () → int\n    !\n    return 2\n"),
        new("sem_wrong_arg_count", "Calor0208",
            "module Sem\n  func G (int:a) → int\n    !\n    return a\n  func F () → int\n    !\n    return G(1, 2)\n"),
        new("sem_immutable_set", "Calor0213",
            "module Sem\n  func F () → int\n    !\n    let int:y = 1\n    set y 2\n    return y\n"),
        new("sem_loop_var_assign", "Calor0213",
            "module Sem\n  func F () → void\n    ! cw\n    for i from 1 to 3 step 1\n      set i 5\n      Console.WriteLine(i)\n"),
        new("sem_duplicate_binding", "Calor0258",
            "module Sem\n  func F () → int\n    !\n    let int:y = 1\n    let int:y = 2\n    return y\n"),
        new("sem_nullable_return", "Calor0273",
            "module Eff\n  func F (object:o) → string\n    !\n    return o.ToString()\n"),
        new("sem_type_mismatch", "Calor1002",
            "module Sem\n  func F () → int\n    !\n    let string:s = \"x\"\n    return s\n"),
        new("sem_missing_return", "Calor1002",
            "module Sem\n  func F (int:x) → int\n    !\n    let int:y = x\n"),
        new("eff_missing_cw", "Calor0410",
            "module Eff\n  func F () → void\n    !\n    Console.WriteLine(\"hi\")\n"),
        new("eff_missing_fs", "Calor0410",
            "module Eff\n  func F (string:p) → bool\n    !\n    return File.Exists(p)\n"),
        new("eff_mut_missing", "Calor0410",
            "module Eff\n  func F () → int\n    ! alloc\n    let List<int>:xs = new List<int>()\n    xs.Add(1)\n    return xs.Count\n"),
        new("sem_unknown_function", "Calor0411",
            "module Sem\n  func F () → int\n    !\n    return Nope(1)\n"),
        new("eff_unknown_code", "Calor0403",
            "module Eff\n  func F () → void\n    ! frobnicate\n    return\n"),
        new("parse_effect_bad_code", "Calor0403",
            "module Bad\n  func F () → void\n    ! fs:throw\n"),
        new("eff_missing_row_public", "Calor0124",
            "module Eff\n  func F public () → int\n    return 1\n"),
        new("eff_private_row", "Calor0125",
            "module Eff\n  func F private () → int\n    !\n    return 1\n"),
    ];

    private readonly ITestOutputHelper _output;

    public DiagnosticTokenBudgetTests(ITestOutputHelper output) => _output = output;

    private static IReadOnlyList<Diagnostic> Diagnose(Case entry) =>
        Program.Compile(entry.Source, FileName).Diagnostics.ToList();

    private static string Render(Diagnostic diagnostic) =>
        new TextDiagnosticFormatter().Format([diagnostic]);

    [Fact]
    public void Corpus_CoversAtLeastThirtyDistinctDiagnosticsAcrossBands()
    {
        var rendered = new List<string>();
        var missing = new List<string>();
        foreach (var entry in Corpus)
        {
            var target = Diagnose(entry).FirstOrDefault(d => d.Code == entry.Code);
            if (target == null)
                missing.Add($"{entry.Name} (expected {entry.Code})");
            else
                rendered.Add(Render(target));
        }

        Assert.True(missing.Count == 0, "Corpus programs no longer produce their diagnostic: " + string.Join(", ", missing));
        Assert.True(rendered.Distinct(StringComparer.Ordinal).Count() >= 30,
            $"The corpus renders only {rendered.Distinct(StringComparer.Ordinal).Count()} distinct diagnostics; #1613 needs at least 30.");
        foreach (var band in new[] { "Calor00", "Calor01", "Calor02", "Calor04" })
            Assert.Contains(Corpus, entry => entry.Code.StartsWith(band, StringComparison.Ordinal));
    }

    [Fact]
    public void MedianDiagnostic_FitsBudget()
    {
        var counts = new List<int>();
        foreach (var entry in Corpus)
        {
            var target = Diagnose(entry).FirstOrDefault(d => d.Code == entry.Code);
            Assert.True(target != null, $"{entry.Name} no longer produces {entry.Code}.");
            var line = Render(target);
            var tokens = ProxyTokenizer.Count(line);
            counts.Add(tokens);
            _output.WriteLine($"{tokens,4} {entry.Name}: {line}");
        }

        counts.Sort();
        var median = counts.Count % 2 == 1
            ? counts[counts.Count / 2]
            : (counts[counts.Count / 2 - 1] + counts[counts.Count / 2]) / 2.0;
        _output.WriteLine($"median: {median} over {counts.Count} diagnostics");

        Assert.True(median <= MedianDiagnosticBudget,
            $"The median rendered diagnostic measures {median} o200k tokens over {counts.Count} corpus " +
            $"diagnostics; the #1613 budget is {MedianDiagnosticBudget}. Shorten the message and keep the " +
            "long explanation in calor_help action=error.");
    }

    [Fact]
    public void EveryRenderedDiagnostic_IsOneLine()
    {
        var multiLine = new List<string>();
        foreach (var entry in Corpus)
        {
            foreach (var diagnostic in Diagnose(entry))
            {
                var line = Render(diagnostic);
                if (line.Contains('\n') || line.Contains('\r'))
                    multiLine.Add($"{entry.Name}: {line}");
            }
        }

        Assert.True(multiLine.Count == 0,
            "#1613 requires one line per diagnostic in the default text format:\n" + string.Join("\n", multiLine));
    }
}
