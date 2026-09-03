using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// <para>
/// <b>PP-S1(rows) and release gate 14 — the row escape table.</b>
/// The instrument <c>docs/plans/roadmap-v0.17.md</c> §4.2 registered and v0.17 did
/// not build. See <c>docs/plans/roadmap-v0.18.md</c> §0.2 and §3.1 M1.
/// </para>
/// <para>
/// <b>Denominator: exactly the twelve argument shapes in issue #1136's measured
/// table</b>, named individually below — <b>five</b> that escaped on v0.15.0 and
/// <b>seven</b> that were already charged. The seven are <b>controls</b> and must
/// not change: a control that regressed is a MISS, not a footnote.
/// </para>
/// <para>
/// The two <i>disclosure</i> shapes the issue carries (inherited field unqualified;
/// module-qualified module function from inside a class body) are reported by
/// <see cref="Disclosure_ShapesAreReported_NeverScored"/> and are deliberately
/// <b>outside</b> the scored twelve. R17:§4.2 fixed the denominator at twelve
/// precisely because an ambiguous denominator is the failure these gates exist to
/// prevent — do not promote a disclosure into the table.
/// </para>
/// <para>
/// <b>What this file does NOT discriminate.</b> Gate 14 asks that reverting S1(a)
/// turn the <c>§PROP</c> row red while the field shapes stay green, and reverting
/// S1(b) turn the alias / other-instance / method-group shapes red. <b>Shape 9 here
/// is a ROWLESS <c>§PROP</c></b> — faithful to the committed fixture and to how
/// v0.15.0 measured it, since a row on a <c>§PROP</c> was a parse error then — so it
/// is closed by S1(b) fail-closed, and reverting S1(a) would leave it green. S1(a)
/// is discriminated by <c>StrictnessBatchTests.PropertyRow_IsParsedAndHonoured</c>
/// and <c>PropertyRow_IsResolved_NotMerelyParsed</c>, which write
/// <c>§PROP{…} §E{cw}</c>. Gate 14's two-halves pin is satisfied <b>jointly</b> by
/// this table and those two tests — not by this table alone.
/// </para>
/// <para>
/// <b>Limitation.</b> This file compiles through the in-process API
/// (<see cref="TestHarness.Compile"/>, which sets <c>UnsafeTranspileOnly</c>), while
/// #1136's table and the adjudication ledger were measured through the <b>CLI</b>.
/// They agree on all twelve, but this test would not catch a CLI-only regression —
/// the gap issue #1116 names (gate 3's CLI-process leg is unbuilt).
/// </para>
/// <para>
/// <b>Freeze.</b> #1136's table is a pre-registered confound under annex A-1.12 and
/// may not be edited by this release. It is committed in-repo in two places besides
/// the issue: <c>pairs/W-001-middleware-stage/pair.json</c> and
/// <c>pairs/W-006-map-doubler/pair.json</c>, both in the <c>note</c> field, and the
/// arm-B before-state is frozen in <c>pairs/ppw-seeded-compiles.json</c>.
/// <see cref="FrozenBeforeState_IsUnedited"/> pins that.
/// </para>
/// <para>
/// <b>Sources.</b> Each row is a single-fact fixture in the form #1136 specifies:
/// one shape per fixture and <b>no <c>§NEW</c> in the method under test</b> — an
/// allocation adds <c>alloc</c> and produces a <c>Calor0410</c> that is <i>not</i>
/// about the callback, which is the artefact that made two earlier measurements of
/// this table wrong. Seven of the twelve reduce a fixture committed under
/// <c>bench/phase0-agent-native/pairs/W-00*/seeded/</c>; the row's
/// <c>FrozenFixture</c> names it where one exists.
/// </para>
/// </summary>
public sealed class RowEscapeTableTests
{
    /// <summary>What #1136 measured on arm B (v0.15.0, no flags) for a shape.</summary>
    public enum Frozen
    {
        /// <summary><c>error Calor0410</c>, exit 1 — a control for this release.</summary>
        Charged,

        /// <summary><c>warning Calor0425</c>, exit 0, still emits — an escape S1 had to close.</summary>
        Escaped,
    }

    public sealed record Shape(
        int Row,
        string Name,
        Frozen FrozenOnV0150,
        string Source,
        string? FrozenFixture = null)
    {
        public override string ToString() => $"{Row:D2}. {Name}";
    }

    // ---------------------------------------------------------------------
    // The row-polymorphic callee every shape passes its callback to, plus a
    // console-writing target for it to receive. Shared verbatim by all twelve so
    // that the only thing varying between rows is the ARGUMENT SHAPE.
    // ---------------------------------------------------------------------
    private const string Preamble = """
        §M{m001:RowTable}
          §F{f001:RunTwice:pub}<eff e> (Func<i32>:g §E{e}) -> i32
            §E{e}
            §B{first:i32} §C{g}
            §B{second:i32} §C{g}
            §R (+ first second)

          §F{f002:Beat:pub} () -> i32
            §E{cw}
            §P "beat"
            §R INT:1
        """;

    private static string Src(string body) => Preamble + "\n\n" + body;

    /// <summary>
    /// The twelve shapes of #1136's table, in the issue's own order, with the issue's
    /// own wording for each name. Transcribed from the frozen table; committed before
    /// this test was first run (roadmap-v0.18 §3.1 M1, commit-order clause).
    /// </summary>
    public static TheoryData<Shape> TwelveShapes()
    {
        var data = new TheoryData<Shape>();

        // ---- CHARGED on v0.15.0 — the seven controls ----

        data.Add(new Shape(1, "module function by simple name (module scope)", Frozen.Charged, Src("""
          §F{f003:Twice:pub} () -> i32
            §E{}
            §R §C{RunTwice} §A Beat §/C
        """)));

        data.Add(new Shape(2, "module function module-qualified (module scope)", Frozen.Charged, Src("""
          §F{f003:Twice:pub} () -> i32
            §E{}
            §R §C{RunTwice} §A RowTableModule.Beat §/C
        """)));

        data.Add(new Shape(3, "own rowed §FLD by simple name", Frozen.Charged, Src("""
          §CL{c001:Holder:pub}
            §FLD{Func<i32>:stage:pri} §E{cw}
            §CTOR{ctor001:pub} ()
              §ASSIGN stage RowTableModule.Beat
            §MT{mt001:Twice:pub} () -> i32
              §E{}
              §R §C{RunTwice} §A stage §/C
        """)));

        data.Add(new Shape(4, "static rowed §FLD by simple name", Frozen.Charged, Src("""
          §CL{c001:Holder:pub}
            §FLD{Func<i32>:stage:pri:stat} §E{cw}
            §CTOR{ctor001:pub} ()
              §ASSIGN stage RowTableModule.Beat
            §MT{mt001:Twice:pub} () -> i32
              §E{}
              §R §C{RunTwice} §A stage §/C
        """)));

        data.Add(new Shape(5, "local bound from such a field", Frozen.Charged, Src("""
          §CL{c001:Holder:pub}
            §FLD{Func<i32>:stage:pri} §E{cw}
            §CTOR{ctor001:pub} ()
              §ASSIGN stage RowTableModule.Beat
            §MT{mt001:Twice:pub} () -> i32
              §E{}
              §B{s:Func<i32>} stage
              §R §C{RunTwice} §A s §/C
        """), FrozenFixture: "W-001-middleware-stage/seeded/unregistered-resolvable-alias-control-b"));

        data.Add(new Shape(6, "§B-bound §LAM with its row omitted", Frozen.Charged, Src("""
          §F{f003:Twice:pub} () -> i32
            §E{}
            §B{report:Func<i32>} §LAM{lam1}
              §P "beat"
              §R INT:1
            §/LAM{lam1}
            §R §C{RunTwice} §A report §/C
        """), FrozenFixture: "W-002-map-and-report/seeded/unregistered-rowless-lambda-control-b"));

        data.Add(new Shape(7, "direct invocation of a this.-qualified field", Frozen.Charged, Src("""
          §CL{c001:Holder:pub}
            §FLD{Func<i32>:stage:pri} §E{cw}
            §CTOR{ctor001:pub} ()
              §ASSIGN stage RowTableModule.Beat
            §MT{mt001:Twice:pub} () -> i32
              §E{}
              §R §C{this.stage} §/C
        """), FrozenFixture: "W-004-counter-peek/seeded/unregistered-this-qualified-escape-b"));

        // ---- ESCAPED on v0.15.0 — the five S1 had to close ----

        data.Add(new Shape(8, "own §FLD via this.", Frozen.Escaped, Src("""
          §CL{c001:Holder:pub}
            §FLD{Func<i32>:stage:pri} §E{cw}
            §CTOR{ctor001:pub} ()
              §ASSIGN stage RowTableModule.Beat
            §MT{mt001:Twice:pub} () -> i32
              §E{}
              §R §C{RunTwice} §A this.stage §/C
        """), FrozenFixture: "W-001-middleware-stage/seeded/unregistered-this-qualified-escape-b"));

        data.Add(new Shape(9, "§PROP by simple name — no receiver at all", Frozen.Escaped, Src("""
          §CL{c001:Holder:pub}
            §PROP{p001:Stage:Func<i32>:pub:get,set}
            §CTOR{ctor001:pub} ()
              §ASSIGN Stage RowTableModule.Beat
            §MT{mt001:Twice:pub} () -> i32
              §E{}
              §R §C{RunTwice} §A Stage §/C
        """), FrozenFixture: "W-001-middleware-stage/seeded/unregistered-property-backed-escape-b"));

        data.Add(new Shape(10, "own §FLD via a local alias of this", Frozen.Escaped, Src("""
          §CL{c001:Holder:pub}
            §FLD{Func<i32>:stage:pri} §E{cw}
            §CTOR{ctor001:pub} ()
              §ASSIGN stage RowTableModule.Beat
            §MT{mt001:Twice:pub} () -> i32
              §E{}
              §B{me:Holder} this
              §R §C{RunTwice} §A me.stage §/C
        """)));

        data.Add(new Shape(11, "§FLD on another instance of the same class", Frozen.Escaped, Src("""
          §CL{c001:Holder:pub}
            §FLD{Func<i32>:stage:pri} §E{cw}
            §CTOR{ctor001:pub} ()
              §ASSIGN stage RowTableModule.Beat
            §MT{mt002:TwiceOf:pub} (Holder:other) -> i32
              §E{}
              §R §C{RunTwice} §A other.stage §/C
        """), FrozenFixture: "W-001-middleware-stage/seeded/unregistered-other-receiver-escape-b"));

        data.Add(new Shape(12, "instance method group with a parameter receiver", Frozen.Escaped, Src("""
          §CL{c002:Ticker:pub}
            §MT{mt010:Beat:pub} () -> i32
              §E{cw}
              §P "beat"
              §R INT:1

          §CL{c001:Holder:pub}
            §MT{mt002:TwiceOf:pub} (Ticker:ticker) -> i32
              §E{}
              §R §C{RunTwice} §A ticker.Beat §/C
        """), FrozenFixture: "W-001-middleware-stage/seeded/unregistered-method-group-receiver-escape-b"));

        return data;
    }

    /// <summary>
    /// <b>The scored claim.</b> PP-S1(rows): every one of the twelve shapes is
    /// <b>charged</b> (<c>Calor0410</c>) or <b>refused</b> (any error), and none
    /// produces an uncharged <c>warning Calor0425</c> at exit 0.
    /// <para>
    /// "Silent laundering" is the precise failure this measures: the compilation
    /// succeeds (no errors, so a real build would emit and exit 0) while the pass
    /// reports <c>Calor0425</c> — "this is not a function value I can name, so
    /// nothing is charged for it". That combination is what let a <c>§E{}</c> method
    /// invoke a <c>§E{cw}</c> callback with the build still passing.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(TwelveShapes))]
    public void NoArgumentShapeLaundersSilently(Shape shape)
    {
        var result = TestHarness.Compile(shape.Source);

        // (a) The fixture must be WELL-FORMED. Without this, the whole theory is
        // vacuous: a fixture with a typo produces a parse or bind error, HasErrors
        // goes true, "did not launder silently" holds, and the row passes green
        // while measuring nothing at all. Codes below Calor0300 are lexer (0001-0099),
        // parser (0100-0199) and semantic/binding (0200-0299) — none of which this
        // table is about.
        var malformed = result.Diagnostics
            .Where(d => d.Severity == Calor.Compiler.Diagnostics.DiagnosticSeverity.Error)
            .Where(d => string.Compare(d.Code, "Calor0300", StringComparison.Ordinal) < 0)
            .ToList();

        Assert.True(
            malformed.Count == 0,
            $"Shape {shape.Row} ({shape.Name}) is MALFORMED, so it measures nothing: "
            + $"{string.Join(", ", malformed.Select(d => $"{d.Code} {d.Message}"))}");

        // (b) The scored claim: charged, and not silently laundering.
        Assert.Contains(
            result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect);

        var launderedSilently =
            !result.HasErrors
            && result.Diagnostics.Any(d => d.Code == DiagnosticCode.EffectRowUnknown);

        Assert.False(
            launderedSilently,
            $"Shape {shape.Row} ({shape.Name}) still launders silently: no error, and "
            + $"Calor0425 charges nothing. Frozen on v0.15.0 as {shape.FrozenOnV0150}. "
            + $"Diagnostics: {Describe(result)}");
    }

    /// <summary>
    /// <b>The seven controls must not change.</b> Registered separately from the
    /// scored claim because a passing five-of-five reads like success: if S1 closed
    /// the escapes by making the pass refuse things it used to charge correctly, the
    /// table would go all-green while the compiler got worse. A control is charged
    /// with <c>Calor0410</c> and must still be.
    /// </summary>
    [Theory]
    [MemberData(nameof(TwelveShapes))]
    public void ControlsStayCharged(Shape shape)
    {
        if (shape.FrozenOnV0150 != Frozen.Charged)
        {
            return;
        }

        var result = TestHarness.Compile(shape.Source);

        Assert.Contains(
            result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    /// <summary>
    /// The two shapes #1136 carries as <i>disclosures</i> rather than rows. Reported,
    /// <b>never scored</b> — they are outside the twelve, and this test asserts
    /// nothing about their verdict on purpose.
    /// <para>
    /// #1136 recorded "inherited field, unqualified" as "escapes on the effect side,
    /// not cleanly measurable end-to-end today", because a class with <c>§EXT</c>
    /// could not call a module function unqualified (<c>Calor1002</c>). <b>#1137
    /// landed in v0.17</b>, so it may now be cleanly measurable. The roadmap requires
    /// that outcome be checked and recorded — and equally requires that it does
    /// <b>not</b> join the scored denominator (roadmap-v0.18 §3.1 M1).
    /// </para>
    /// </summary>
    [Fact]
    public void Disclosure_ShapesAreReported_NeverScored()
    {
        // Disclosure 1 — inherited field, unqualified. Blocked end-to-end pre-#1137.
        var inheritedField = TestHarness.Compile(Src("""
          §CL{c000:Base:pub}
            §FLD{Func<i32>:stage:pro} §E{cw}

          §CL{c001:Holder:pub}
            §EXT{Base}
            §MT{mt001:Twice:pub} () -> i32
              §E{}
              §R §C{RunTwice} §A stage §/C
        """));

        // Disclosure 2 — module function module-qualified, from inside a class body.
        // Charged in module scope (shape 2); from a class body it additionally drew
        // a Calor0425.
        var moduleQualifiedInClass = TestHarness.Compile(Src("""
          §CL{c001:Holder:pub}
            §MT{mt001:Twice:pub} () -> i32
              §E{}
              §R §C{RunTwice} §A RowTableModule.Beat §/C
        """));

        // Recorded, not asserted. The test output is the record.
        Assert.True(true,
            $"DISCLOSURE 1 (inherited field, unqualified): {Describe(inheritedField)}\n"
            + $"DISCLOSURE 2 (module-qualified module function inside a class body): "
            + $"{Describe(moduleQualifiedInClass)}");
    }

    /// <summary>
    /// <b>NOT-ADJUDICATED guard.</b> The outcome map says the proof point is
    /// NOT-ADJUDICATED if a fixture or its frozen multiset is edited during the
    /// release. The before-state lives in <c>pairs/ppw-seeded-compiles.json</c>;
    /// these are the arm-B records for the six committed <c>unregistered-*</c> roles,
    /// transcribed here so an edit to that file fails a test rather than quietly
    /// moving the baseline.
    /// <para>
    /// Skips on a tree where the bench corpus is not present, in the manner of
    /// <c>BinderIncompleteRatchetTests</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void FrozenBeforeState_IsUnedited()
    {
        // (pair, role) -> "exitCode|CODE/severity*count,..."  — arm B, v0.15.0, no flags.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["W-001-middleware-stage/unregistered-this-qualified-escape"] = "0|Calor0425/warning*1",
            ["W-001-middleware-stage/unregistered-property-backed-escape"] = "0|Calor0425/warning*1",
            ["W-001-middleware-stage/unregistered-other-receiver-escape"] = "0|Calor0425/warning*1",
            ["W-001-middleware-stage/unregistered-method-group-receiver-escape"] = "0|Calor0425/warning*1",
            ["W-001-middleware-stage/unregistered-resolvable-alias-control"] = "1|Calor0410/error*1",
            ["W-002-map-and-report/unregistered-rowless-lambda-control"] = "1|Calor0410/error*2",
            ["W-004-counter-peek/unregistered-this-qualified-escape"] = "1|Calor0410/error*1,Calor0411/warning*1",
            ["W-006-map-doubler/unregistered-this-qualified-escape"] = "0|Calor0425/warning*2",
            ["W-006-map-doubler/unregistered-property-backed-escape"] = "0|Calor0425/warning*3",
        };

        var path = FindBenchFile("pairs/ppw-seeded-compiles.json");
        Skip.If(path is null, "bench/phase0-agent-native not present in this tree");

        using var doc = JsonDocument.Parse(File.ReadAllText(path!));
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var record in doc.RootElement.GetProperty("compiles").EnumerateArray())
        {
            if (record.GetProperty("arm").GetString() != "B")
            {
                continue;
            }

            var role = record.GetProperty("role").GetString() ?? string.Empty;
            if (!role.StartsWith("unregistered-", StringComparison.Ordinal))
            {
                continue;
            }

            var key = $"{record.GetProperty("pair").GetString()}/{role}";
            if (!expected.ContainsKey(key))
            {
                continue;  // roles outside the twelve-shape table's before-state
            }

            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in record.GetProperty("diagnostics").EnumerateArray())
            {
                var k = $"{d.GetProperty("code").GetString()}/{d.GetProperty("severity").GetString()}";
                counts[k] = counts.TryGetValue(k, out var n) ? n + 1 : 1;
            }

            var multiset = string.Join(",", counts.Select(kv => $"{kv.Key}*{kv.Value}"));
            actual[key] = $"{record.GetProperty("exitCode").GetInt32()}|{multiset}";
        }

        Assert.Equal(
            expected.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList(),
            actual.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList());
    }

    private static string Describe(Calor.Compiler.CompilationResult result)
    {
        var codes = result.Diagnostics
            .GroupBy(d => $"{d.Code}/{d.Severity}", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}*{g.Count()}");

        return $"hasErrors={result.HasErrors} [{string.Join(", ", codes)}]";
    }

    /// <summary>
    /// Walks up from the test assembly looking for the repo marker, then resolves a
    /// path under <c>bench/phase0-agent-native/</c>. Anchors on <c>Calor.sln</c>
    /// beside a <c>bench</c> directory — deliberately not the "any ancestor with a
    /// case-insensitive entry" match that #1121 reports.
    /// </summary>
    private static string? FindBenchFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "Calor.sln")))
            {
                continue;
            }

            var candidate = Path.Combine(dir.FullName, "bench", "phase0-agent-native", relative);
            return File.Exists(candidate) ? candidate : null;
        }

        return null;
    }
}
