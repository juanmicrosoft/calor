using System.Reflection;
using Calor.RoundTrip.Harness.Oracle;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// #1667: the member oracle's false-negative audit. Every way a member comparison can end without a DIFFERENT verdict
/// (AGREE, SKIPPED, INCOMPLETE, NONDETERMINISTIC, TIMEOUT, INCONCLUSIVE, and the HANG/CRASH replay paths; the list is in
/// <c>tools/Calor.RoundTrip.Harness/Oracle/README.md</c>) gets a mutation fixture here: a hand-made original/converted
/// pair with a real behavior change that the path could hide. Each fixture asserts that the oracle reports the change,
/// or that the member is excluded with a reason that keeps it out of coverage. Fixtures marked "false negative fixed"
/// passed silently (AGREE, TIMEOUT or NONDETERMINISTIC) before #1667.
/// </summary>
[Collection(MemberOracleCollection.Name)]
public class MemberOracleAuditTests
{
    private static List<MemberVerdict> Compare(string original, string converted, string[] types, int depth = StateRenderer.DefaultDepth, double timeoutSeconds = 5) =>
        MemberDifferentialOracle.Compare(MemberOracleTests.Compile("original", original), MemberOracleTests.Compile("converted", converted), types,
            new OracleOptions { Depth = depth, SeedKey = "fixture", MemberTimeout = TimeSpan.FromSeconds(timeoutSeconds) });

    private static MemberVerdict Single(List<MemberVerdict> verdicts, string member) => Assert.Single(verdicts, v => v.Member == member);

    private static MemberVerdict Single(List<MemberVerdict> verdicts, string type, string member) => Assert.Single(verdicts, v => v.Type == type && v.Member == member);

    private static void AssertDifferent(MemberVerdict verdict, string? witnessFragment = null)
    {
        Assert.True(verdict.Status == MemberStatus.Different, $"{verdict.Key}: {verdict.Status} ({verdict.Reason})");
        Assert.NotNull(verdict.Fingerprint);
        if (witnessFragment is not null)
            Assert.Contains(witnessFragment, string.Join(" || ", verdict.Witnesses), StringComparison.Ordinal);
    }

    /// <summary>The member is not AGREE, is not counted as compared coverage, and carries a skip category.</summary>
    private static void AssertExcluded(MemberVerdict verdict, string category)
    {
        Assert.NotEqual(MemberStatus.Agree, verdict.Status);
        Assert.NotEqual(MemberStatus.Different, verdict.Status);
        Assert.Equal(category, SkipCategory.Of(verdict));
        Assert.Equal(0, OracleRunner.Compared(new OracleFileReport { File = "f.cs", Status = "exercised", Members = [verdict] }));
    }

    private static string Obs(params (string Part, string Value)[] parts) =>
        string.Join(MemberDifferentialOracle.PartSeparator, parts.Select(p => p.Part + MemberDifferentialOracle.NameSeparator + p.Value));

    // ---- AGREE: a stable difference on any compared input fails ----------------------------------------------

    /// <summary>AGREE with some inputs not compared (setup failed identically): a difference on a compared input still fails.</summary>
    [Fact]
    public void Agree_InputsWithSetupFailures_DoNotHideADifferenceOnTheOthers()
    {
        const string widget = "public class Widget { public Widget() { throw new System.InvalidOperationException(\"no\"); } } ";
        var verdicts = Compare(widget + "public static class S { public static int F(Widget w) => w == null ? 1 : 0; }",
            widget + "public static class S { public static int F(Widget w) => w == null ? 2 : 0; }", ["Widget", "S"]);
        AssertDifferent(Single(verdicts, "static F(Widget)->System.Int32"), "(null)");
    }

    /// <summary>A true agreement counts as coverage, with its compared inputs.</summary>
    [Fact]
    public void Agree_IdenticalBuilds_CountAsCoverage()
    {
        const string source = "public static class S { public static int F(int x) => x * 3; }";
        var f = Single(Compare(source, source, ["S"]), "static F(System.Int32)->System.Int32");
        Assert.Equal(MemberStatus.Agree, f.Status);
        Assert.Null(SkipCategory.Of(f));
        Assert.True(f.Inputs > 10);
    }

    // ---- Exceptions of the same type ----------------------------------------------------------------------

    /// <summary>False negative fixed: a thrown exception used to be compared by type only.</summary>
    [Fact]
    public void ThrownException_SameTypeDifferentMessage_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static int F(int x) => x < 0 ? throw new System.InvalidOperationException(\"negative\") : x; }",
            "public static class S { public static int F(int x) => x < 0 ? throw new System.InvalidOperationException(\"below zero\") : x; }", ["S"]);
        AssertDifferent(Single(verdicts, "static F(System.Int32)->System.Int32"), "negative");
    }

    /// <summary>False negative fixed: <see cref="Exception.Data"/> entries are compared.</summary>
    [Fact]
    public void ThrownException_SameMessageDifferentData_IsDifferent()
    {
        const string body = "var e = new System.InvalidOperationException(\"m\"); e.Data[\"code\"] = ";
        var verdicts = Compare("public static class S { public static void F() { " + body + "1; throw e; } }",
            "public static class S { public static void F() { " + body + "2; throw e; } }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Void"), "data=");
    }

    /// <summary>False negative fixed: the inner exception of a thrown exception is compared.</summary>
    [Fact]
    public void ThrownException_DifferentInnerException_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static void F() => throw new System.InvalidOperationException(\"m\", new System.FormatException(\"a\")); }",
            "public static class S { public static void F() => throw new System.InvalidOperationException(\"m\", new System.FormatException(\"b\")); }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Void"), "inner=");
    }

    /// <summary>False negative fixed: a subject exception's own properties (and framework ones such as ParamName) are compared.</summary>
    [Fact]
    public void ThrownException_SubjectExceptionProperty_IsDifferent()
    {
        const string ex = "public class CodedException : System.Exception { public int Code { get; } public CodedException(int code) : base(\"m\") { Code = code; } } ";
        var verdicts = Compare(ex + "public static class S { public static void F() => throw new CodedException(1); }",
            ex + "public static class S { public static void F() => throw new CodedException(2); }", ["CodedException", "S"]);
        AssertDifferent(Single(verdicts, "S", "static F()->System.Void"), "Code=");
    }

    /// <summary>False negative fixed: a constructor that throws is compared by its full exception.</summary>
    [Fact]
    public void ThrowingConstructor_DifferentMessage_IsDifferent()
    {
        var verdicts = Compare("public class C { public C() { throw new System.ArgumentException(\"a\", \"x\"); } }",
            "public class C { public C() { throw new System.ArgumentException(\"a\", \"y\"); } }", ["C"]);
        AssertDifferent(Single(verdicts, "ctor()"), "ParamName=");
    }

    /// <summary>A faulted task's exception message is compared.</summary>
    [Fact]
    public void FaultedTask_DifferentMessage_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static System.Threading.Tasks.Task<int> F() => System.Threading.Tasks.Task.FromException<int>(new System.InvalidOperationException(\"a\")); }",
            "public static class S { public static System.Threading.Tasks.Task<int> F() => System.Threading.Tasks.Task.FromException<int>(new System.InvalidOperationException(\"b\")); }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Threading.Tasks.Task<System.Int32>"), "InvalidOperationException(\"a\"");
    }

    /// <summary>False negative fixed: a property getter that throws in rendered state is compared by its message.</summary>
    [Fact]
    public void ThrowingGetter_DifferentMessage_IsDifferent()
    {
        var verdicts = Compare("public class C { public int P => throw new System.InvalidOperationException(\"a\"); }",
            "public class C { public int P => throw new System.InvalidOperationException(\"b\"); }", ["C"]);
        AssertDifferent(Single(verdicts, "ctor()"), "P=!");
    }

    /// <summary>A message that names the per-run sandbox is stable once the sandbox root is normalized (no false NONDETERMINISTIC).</summary>
    [Fact]
    public void ExceptionNamingTheSandbox_IsStable()
    {
        const string source = "public static class S { public static int F() => System.IO.File.ReadAllText(\"missing.txt\").Length; }";
        var f = Single(Compare(source, source, ["S"]), "static F()->System.Int32");
        Assert.Equal(MemberStatus.Agree, f.Status);
    }

    // ---- Nondeterminism -----------------------------------------------------------------------------------

    /// <summary>False negative fixed: a converted build that turns a stable value into a changing one was NONDETERMINISTIC (skipped).</summary>
    [Fact]
    public void OneSidedNondeterminism_ConvertedUnstable_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static string F() => \"fixed\"; }",
            "public static class S { public static string F() => System.Guid.NewGuid().ToString(); }", ["S"]);
        var f = Single(verdicts, "static F()->System.String");
        AssertDifferent(f, "fixed");
        Assert.StartsWith("ONE-SIDED-NONDETERMINISM", f.Reason, StringComparison.Ordinal);
    }

    /// <summary>The same, the other way round: the original changes, the converted build is constant.</summary>
    [Fact]
    public void OneSidedNondeterminism_OriginalUnstable_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static string F() => System.Guid.NewGuid().ToString(); }",
            "public static class S { public static string F() => \"fixed\"; }", ["S"]);
        Assert.StartsWith("ONE-SIDED-NONDETERMINISM", Single(verdicts, "static F()->System.String").Reason, StringComparison.Ordinal);
    }

    /// <summary>The one-sided fingerprint leaves the changing values out, so it is the same on every run (a baseline can match it).</summary>
    [Fact]
    public void OneSidedNondeterminism_FingerprintIsStable()
    {
        const string original = "public static class S { public static string F() => \"fixed\"; }";
        const string converted = "public static class S { public static string F() => System.Guid.NewGuid().ToString(); }";
        Assert.Equal(Single(Compare(original, converted, ["S"]), "static F()->System.String").Fingerprint,
            Single(Compare(original, converted, ["S"]), "static F()->System.String").Fingerprint);
    }

    /// <summary>Nondeterminism in the receiver part never hides a stable difference in the result part.</summary>
    [Fact]
    public void NondeterministicReceiver_DoesNotHideAStableResultDifference()
    {
        const string id = "public string Id = System.Guid.NewGuid().ToString(); ";
        var verdicts = Compare("public class C { " + id + "public int F(int x) => x + 1; }", "public class C { " + id + "public int F(int x) => x + 2; }", ["C"]);
        var f = Single(verdicts, "F(System.Int32)->System.Int32");
        AssertDifferent(f, "[result]");
        Assert.Contains("this", f.Reason, StringComparison.Ordinal);
    }

    /// <summary>Nondeterministic on both builds: the change is hidden, and the member is excluded from coverage.</summary>
    [Fact]
    public void NondeterministicOnBothBuilds_IsExcludedFromCoverage()
    {
        var f = Single(Compare("public static class S { public static string F() => System.Guid.NewGuid().ToString(); }",
            "public static class S { public static string F() => System.Guid.NewGuid().ToString() + \"x\"; }", ["S"]), "static F()->System.String");
        Assert.Equal(MemberStatus.Nondeterministic, f.Status);
        AssertExcluded(f, "nondeterministic");
    }

    /// <summary>A crash that did not reproduce turns AGREE into NONDETERMINISTIC, and never touches a DIFFERENT verdict.</summary>
    [Fact]
    public void UnreproducedCrash_NeverHidesADifference()
    {
        var different = new MemberVerdict { Type = "T", Member = "F()", Kind = "static", Status = MemberStatus.Different, Fingerprint = "fp", Witnesses = ["w"], Inputs = 3 };
        var agree = different with { Status = MemberStatus.Agree, Fingerprint = null, Witnesses = [] };
        var crashed = new HashSet<string> { "T::F()" };
        Assert.Equal(different, OracleRunner.UnreproducedCrash(different, crashed));
        var downgraded = OracleRunner.UnreproducedCrash(agree, crashed);
        Assert.Equal(MemberStatus.Nondeterministic, downgraded.Status);
        Assert.Equal(agree, OracleRunner.UnreproducedCrash(agree, new HashSet<string>()));
    }

    // ---- Replayed inputs: timeouts, segments, replays ---------------------------------------------------------

    /// <summary>
    /// False negative fixed: a member limited at its failing input stopped there, so every later input went uncompared.
    /// The input pool puts int.MinValue (#14) right after int.MaxValue (#13); the difference at #14 is now found by the
    /// suffix segment.
    /// </summary>
    [Fact]
    public void SharedTimeout_InputsAfterTheFailingOneAreCompared()
    {
        const string slow = "if (n == int.MaxValue) System.Threading.Thread.Sleep(4000);";
        var verdicts = Compare("public static class S { public static int F(int n) { " + slow + " return n; } }",
            "public static class S { public static int F(int n) { " + slow + " return n == int.MinValue ? 0 : n; } }", ["S"], timeoutSeconds: 1);
        var head = Single(verdicts, "static F(System.Int32)->System.Int32");
        Assert.Equal(MemberStatus.Timeout, head.Status);
        Assert.Equal(13, head.LimitedAt);
        var tail = Single(verdicts, "static F(System.Int32)->System.Int32 [inputs 14..]");
        AssertDifferent(tail, "#14 (-2147483648)");
    }

    /// <summary>A member gets at most <see cref="OraclePlanner.MaxSegments"/> segments; the expansion depends only on the plan and the limits.</summary>
    [Fact]
    public void Segments_AreBoundedAndDeterministic()
    {
        var member = new PlannedMember("T", "F()", "static", Enumerable.Range(0, 20).Select(i => new object?[] { i }).ToList(), null);
        var limits = new Dictionary<string, int> { ["T::F()"] = 2, ["T::F() [inputs 3..]"] = 4, ["T::F() [inputs 8..]"] = 1 };
        var plan = OraclePlanner.Expand([member], limits);
        Assert.Equal(["F()", "F() [inputs 3..]", "F() [inputs 8..]"], plan.Select(m => m.Member));
        Assert.Equal([0, 3, 8], plan.Select(m => m.Offset));
        Assert.All(plan.Skip(1), m => Assert.Equal("F()", m.Callable));
        Assert.Equal(8, plan[2].Inputs[0][0]);
        Assert.Equal(plan.Select(m => m.Key), OraclePlanner.Expand([member], limits).Select(m => m.Key));
    }

    /// <summary>A TIMEOUT member that compared nothing is excluded from coverage.</summary>
    [Fact]
    public void TimeoutWithNoComparedInput_IsExcludedFromCoverage()
    {
        AssertExcluded(new MemberVerdict { Type = "T", Member = "F()", Kind = "static", Status = MemberStatus.Timeout, LimitedAt = 0, Inputs = 0 }, "timeout");
    }

    /// <summary>False negative fixed: one build hanging where the other crashes (each reproduced twice) was a shared TIMEOUT.</summary>
    [Fact]
    public void Replay_HangVersusCrash_IsDifferent()
    {
        var prefix = new MemberVerdict { Type = "T", Member = "F()", Kind = "static", Status = MemberStatus.Timeout, LimitedAt = 3, Inputs = 3 };
        var verdict = MemberDifferentialOracle.ClassifyTimeout(prefix, [new(ProbeOutcome.Hung, 4, null), new(ProbeOutcome.Hung, 4, null)],
            [new(ProbeOutcome.Crashed, 0, null), new(ProbeOutcome.Crashed, 0, null)], new OracleOptions { MemberTimeout = TimeSpan.FromSeconds(4) });
        Assert.Equal(MemberStatus.Different, verdict.Status);
        Assert.Equal("FAILURE-MODE", verdict.Reason);
    }

    /// <summary>Replays that all finish compare every part: a stable stderr difference fails even though the result agrees.</summary>
    [Fact]
    public void Replay_StableDifferenceInASecondaryPart_IsDifferent()
    {
        var prefix = new MemberVerdict { Type = "T", Member = "F()", Kind = "static", Status = MemberStatus.Timeout, LimitedAt = 3, Inputs = 3 };
        ProbeResult Finished(string stderr) => new(ProbeOutcome.Finished, 0.1, Obs(("result", "return=1"), ("stderr", stderr)));
        var verdict = MemberDifferentialOracle.ClassifyTimeout(prefix, [Finished("\"a\""), Finished("\"a\"")], [Finished("\"b\""), Finished("\"b\"")],
            new OracleOptions { MemberTimeout = TimeSpan.FromSeconds(4) });
        Assert.Equal(MemberStatus.Different, verdict.Status);
        Assert.Contains("[stderr]", verdict.Witnesses[0], StringComparison.Ordinal);
    }

    /// <summary>A replay that never reached the input is INCONCLUSIVE, which the gate fails (it can hide anything).</summary>
    [Fact]
    public void Replay_NeverReachingTheInput_FailsTheGate()
    {
        var prefix = new MemberVerdict { Type = "T", Member = "F()", Kind = "static", Status = MemberStatus.Timeout, LimitedAt = 3, Inputs = 3 };
        var verdict = MemberDifferentialOracle.ClassifyTimeout(prefix, [new(ProbeOutcome.Infrastructure, 0, "lost"), new(ProbeOutcome.Hung, 4, null)],
            [new(ProbeOutcome.Hung, 4, null), new(ProbeOutcome.Hung, 4, null)], new OracleOptions { MemberTimeout = TimeSpan.FromSeconds(4) });
        Assert.Equal(MemberStatus.Inconclusive, verdict.Status);
        var report = new OracleProjectReport { Project = "P", Files = { new OracleFileReport { File = "a.cs", Status = "exercised", Members = [verdict] } } };
        OracleRunner.ApplyGate("P", new OracleBaseline(), new OracleExpected(), report, ["a.cs"], new HashSet<string> { "a.cs" });
        Assert.False(report.Passed);
    }

    // ---- Fingerprint aliasing -----------------------------------------------------------------------------

    private static void BoolShape(bool b) => GC.KeepAlive(b);

    /// <summary>
    /// False negative fixed: inputs that render alike (two <c>false</c> calls) shared a witness label, so a difference that
    /// moved from one to the other kept its fingerprint and still matched a baseline entry. Labels carry the input index.
    /// </summary>
    [Fact]
    public void Fingerprint_DistinguishesInputsThatRenderAlike()
    {
        var shape = typeof(MemberOracleAuditTests).GetMethod(nameof(BoolShape), BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters();
        var inputs = MemberOracleInputs.Generate("fixture", "S::static F(System.Boolean)->System.Int32", shape);
        var later = Enumerable.Range(1, inputs.Count - 1).First(i => inputs[i][0] is false);
        const string original = "public static class S { static int calls; public static int F(bool b) { ++calls; return 0; } }";
        string Converted(int call) => "public static class S { static int calls; public static int F(bool b) => ++calls == " + call + " ? 1 : 0; }";
        var first = Single(Compare(original, Converted(1), ["S"]), "static F(System.Boolean)->System.Int32");
        var moved = Single(Compare(original, Converted(later + 1), ["S"]), "static F(System.Boolean)->System.Int32");
        AssertDifferent(first, "#0 (false)");
        AssertDifferent(moved, $"#{later} (false)");
        Assert.NotEqual(first.Fingerprint, moved.Fingerprint);
    }

    /// <summary>False negative fixed: every culture input rendered as the CultureInfo type name; the label now names the culture.</summary>
    [Fact]
    public void Fingerprint_NamesCultureInputs()
    {
        const string original = "public static class S { public static int F(System.Globalization.CultureInfo c) => 0; }";
        var en = Single(Compare(original, "public static class S { public static int F(System.Globalization.CultureInfo c) => c?.Name == \"en-US\" ? 1 : 0; }", ["S"]),
            "static F(System.Globalization.CultureInfo)->System.Int32");
        var de = Single(Compare(original, "public static class S { public static int F(System.Globalization.CultureInfo c) => c?.Name == \"de-DE\" ? 1 : 0; }", ["S"]),
            "static F(System.Globalization.CultureInfo)->System.Int32");
        AssertDifferent(en, "culture:en-US");
        AssertDifferent(de, "culture:de-DE");
        Assert.NotEqual(en.Fingerprint, de.Fingerprint);
    }

    // ---- Incomplete renderings ------------------------------------------------------------------------------

    /// <summary>A difference inside a part that cannot be rendered in full is excluded from coverage, never AGREE.</summary>
    [Fact]
    public void IncompleteResult_IsExcludedFromCoverage()
    {
        var f = Single(Compare("public static class S { public static System.Func<System.IO.Stream, int> F() => s => 1; }",
            "public static class S { public static System.Func<System.IO.Stream, int> F() => s => 2; }", ["S"]), "static F()->System.Func<System.IO.Stream,System.Int32>");
        AssertExcluded(f, "incomplete-observation");
    }

    /// <summary>A rendering incomplete on one build only differs from the other build's.</summary>
    [Fact]
    public void IncompleteOnOneBuildOnly_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static System.Func<System.IO.Stream, int> F() => s => 1; }",
            "public static class S { public static System.Func<System.IO.Stream, int> F() => null; }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Func<System.IO.Stream,System.Int32>"));
    }

    /// <summary>An incomplete secondary part never hides a stable difference in the result.</summary>
    [Fact]
    public void IncompleteSecondaryPart_DoesNotHideAStableResultDifference()
    {
        const string d = "public static System.Func<System.IO.Stream, int> D = s => 1; ";
        var verdicts = Compare("public static class S { " + d + "public static int F(int x) => x + 1; }", "public static class S { " + d + "public static int F(int x) => x + 2; }", ["S"]);
        AssertDifferent(Single(verdicts, "static F(System.Int32)->System.Int32"), "[result]");
    }

    /// <summary>A cached private delegate is rendered without calls, so it does not make every input of the project incomplete.</summary>
    [Fact]
    public void PrivateStaticDelegate_DoesNotMakeObservationsIncomplete()
    {
        const string source = "public static class S { private static readonly System.Func<System.IO.Stream, int> Cache = s => 1; public static int F(int x) => x + 1; }";
        Assert.Equal(MemberStatus.Agree, Single(Compare(source, source, ["S"]), "static F(System.Int32)->System.Int32").Status);
    }

    // ---- Static state ---------------------------------------------------------------------------------------

    /// <summary>
    /// False negative fixed: plan order runs <c>Get</c> before <c>Set</c>, so the only reader of the private static ran before
    /// the writer and the change was never observed. Private statics are now part of every input's observation.
    /// </summary>
    [Fact]
    public void PrivateStatic_WrittenAfterItsOnlyReader_IsDifferent()
    {
        var verdicts = Compare("public static class S { static int _n; public static int Get() => _n; public static void Set(int x) { _n = x * 2; } }",
            "public static class S { static int _n; public static int Get() => _n; public static void Set(int x) { _n = x * 3; } }", ["S"]);
        AssertDifferent(Single(verdicts, "static Set(System.Int32)->System.Void"), "[private-statics]");
    }

    /// <summary>False negative fixed: generic types were closed over object only, so a static of <c>Cache&lt;int&gt;</c> was never observed.</summary>
    [Fact]
    public void StaticOfAGenericTypeClosedOverInt_IsObserved()
    {
        const string cache = "public static class Cache<T> { public static T Last; } ";
        var verdicts = Compare(cache + "public static class S { public static void F(int x) { Cache<int>.Last = x; } }",
            cache + "public static class S { public static void F(int x) { Cache<int>.Last = x + 1; } }", ["Cache`1", "S"]);
        AssertDifferent(Single(verdicts, "S", "static F(System.Int32)->System.Void"), "Cache<System.Int32>");
    }

    /// <summary>A public static of another type changed by the call is compared after every input.</summary>
    [Fact]
    public void PublicStaticOfAnotherType_IsDifferent()
    {
        const string other = "public static class Other { public static string Last; } ";
        var verdicts = Compare(other + "public static class S { public static void F(string s) { Other.Last = s; } }",
            other + "public static class S { public static void F(string s) { Other.Last = s?.Trim(); } }", ["Other", "S"]);
        AssertDifferent(Single(verdicts, "S", "static F(System.String)->System.Void"), "[project]");
    }

    // ---- Console and file output ----------------------------------------------------------------------------

    /// <summary>False negative fixed: standard output and standard error went to one buffer, so moving a message between them agreed.</summary>
    [Fact]
    public void Console_StdoutVersusStderr_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static void F() => System.Console.WriteLine(\"warning\"); }",
            "public static class S { public static void F() => System.Console.Error.WriteLine(\"warning\"); }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Void"), "[std");
    }

    /// <summary>
    /// False negative fixed: a writer captured from Console.Out in a type initializer kept the first input's buffer, so
    /// later inputs' output through it was never read. The console routers keep it pointed at the current input.
    /// </summary>
    [Fact]
    public void Console_WriterCapturedInATypeInitializer_IsObservedOnLaterInputs()
    {
        const string log = "public static class Log { public static readonly System.IO.TextWriter W = System.Console.Out; } ";
        var verdicts = Compare(log + "public static class S { public static void F(int x) { Log.W.Write(x == 7 ? \"a\" : \"b\"); } }",
            log + "public static class S { public static void F(int x) { Log.W.Write(x == 7 ? \"c\" : \"b\"); } }", ["Log", "S"]);
        AssertDifferent(Single(verdicts, "S", "static F(System.Int32)->System.Void"), "#7 (7) [stdout]");
    }

    [Fact]
    public void FileContent_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static void F() => System.IO.File.WriteAllText(\"a.txt\", \"1\"); }",
            "public static class S { public static void F() => System.IO.File.WriteAllText(\"a.txt\", \"2\"); }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Void"), "[files]");
    }

    /// <summary>False negative fixed: only files were listed, so a directory created under another name (or not at all) agreed.</summary>
    [Fact]
    public void CreatedDirectory_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static void F() { System.IO.Directory.CreateDirectory(\"out\"); } }",
            "public static class S { public static void F() { System.IO.Directory.CreateDirectory(\"output\"); } }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Void"), "[files]");
    }

    // ---- Async results --------------------------------------------------------------------------------------

    /// <summary>False negative fixed: a ValueTask was rendered without being awaited, so state set after its await was never seen.</summary>
    [Fact]
    public void ValueTask_StateAfterCompletion_IsDifferent()
    {
        const string counter = "public static class Counter { public static int X; } ";
        const string body = "await System.Threading.Tasks.Task.Delay(300); Counter.X = ";
        var verdicts = Compare(counter + "public static class S { public static async System.Threading.Tasks.ValueTask F() { " + body + "1; } }",
            counter + "public static class S { public static async System.Threading.Tasks.ValueTask F() { " + body + "2; } }", ["Counter", "S"]);
        AssertDifferent(Single(verdicts, "S", "static F()->System.Threading.Tasks.ValueTask"), "[project]");
    }

    [Fact]
    public void ValueTaskResult_IsDifferent()
    {
        const string body = "await System.Threading.Tasks.Task.Delay(100); return ";
        var verdicts = Compare("public static class S { public static async System.Threading.Tasks.ValueTask<int> F() { " + body + "1; } }",
            "public static class S { public static async System.Threading.Tasks.ValueTask<int> F() { " + body + "2; } }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Threading.Tasks.ValueTask<System.Int32>"), "ValueTask:Task(RanToCompletion");
    }

    /// <summary>A task's receiver state is observed after the task completes.</summary>
    [Fact]
    public void Task_ReceiverStateAfterCompletion_IsDifferent()
    {
        const string body = "public int X; public async System.Threading.Tasks.Task F() { await System.Threading.Tasks.Task.Delay(100); X = ";
        var verdicts = Compare("public class C { " + body + "1; } }", "public class C { " + body + "2; } }", ["C"]);
        AssertDifferent(Single(verdicts, "F()->System.Threading.Tasks.Task"), "[this]");
    }

    /// <summary>False negative fixed: an async stream rendered as an opaque compiler-generated object, so what it yielded was never compared.</summary>
    [Fact]
    public void AsyncStream_ItemsAreCompared()
    {
        const string head = "public static class S { public static async System.Collections.Generic.IAsyncEnumerable<int> F() { yield return 1; await System.Threading.Tasks.Task.Yield(); yield return ";
        var verdicts = Compare(head + "2; } }", head + "3; } }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Collections.Generic.IAsyncEnumerable<System.Int32>"), "async[");
    }

    // ---- Returned delegates and iterators -------------------------------------------------------------------

    /// <summary>False negative fixed: a returned delegate that throws was compared by exception type only.</summary>
    [Fact]
    public void ReturnedDelegate_ThrowingDifferentMessages_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static System.Func<int, int> F() => x => throw new System.InvalidOperationException(\"a\" + x); }",
            "public static class S { public static System.Func<int, int> F() => x => throw new System.InvalidOperationException(\"b\" + x); }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Func<System.Int32,System.Int32>"));
    }

    /// <summary>A returned closure's captured state is compared.</summary>
    [Fact]
    public void ReturnedDelegate_CapturedStateDifference_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static System.Func<int> F(int x) { var y = x * 2; return () => y - y; } }",
            "public static class S { public static System.Func<int> F(int x) { var y = x * 3; return () => y - y; } }", ["S"]);
        AssertDifferent(Single(verdicts, "static F(System.Int32)->System.Func<System.Int32>"), "target=");
    }

    /// <summary>False negative fixed: an iterator returned as IEnumerator rendered as an opaque compiler-generated object.</summary>
    [Fact]
    public void ReturnedIterator_ItemsAreCompared()
    {
        var verdicts = Compare("public static class S { public static System.Collections.Generic.IEnumerator<int> F() { yield return 1; yield return 2; } }",
            "public static class S { public static System.Collections.Generic.IEnumerator<int> F() { yield return 1; yield return 3; } }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Collections.Generic.IEnumerator<System.Int32>"), "enumerator[");
    }

    // ---- Depth-2 state --------------------------------------------------------------------------------------

    private const string Nested = "public class Inner { public int V; } public class Outer { public Inner I = new(); } ";

    /// <summary>False negative fixed: the argument list consumed a level, so an argument's state was seen only one hop deep.</summary>
    [Fact]
    public void ArgumentStateAtDepth2_IsDifferent()
    {
        var verdicts = Compare(Nested + "public static class S { public static void F(Outer o) { if (o != null) o.I.V = 1; } }",
            Nested + "public static class S { public static void F(Outer o) { if (o != null) o.I.V = 2; } }", ["Inner", "Outer", "S"]);
        AssertDifferent(Single(verdicts, "S", "static F(Outer)->System.Void"), "V=");
    }

    [Fact]
    public void ReceiverStateAtDepth2_IsDifferent()
    {
        var verdicts = Compare(Nested + "public class C { public Inner I = new(); public void F() { I.V = 1; } }",
            Nested + "public class C { public Inner I = new(); public void F() { I.V = 2; } }", ["Inner", "Outer", "C"]);
        // this -> I (hop 1) -> V (hop 2).
        AssertDifferent(Single(verdicts, "C", "F()->System.Void"), "[this]");
    }

    [Fact]
    public void ReturnStateAtDepth2_IsDifferent()
    {
        var verdicts = Compare(Nested + "public static class S { public static Outer F() { var o = new Outer(); o.I.V = 1; return o; } }",
            Nested + "public static class S { public static Outer F() { var o = new Outer(); o.I.V = 2; return o; } }", ["Inner", "Outer", "S"]);
        AssertDifferent(Single(verdicts, "S", "static F()->Outer"), "V=");
    }

    /// <summary>The documented bound: a change three hops deep is outside depth 2 and inside depth 3 (<c>--depth</c>).</summary>
    [Fact]
    public void StateBeyondTheDepth_IsTheDocumentedBound()
    {
        // Holder -> A (hop 1) -> B (hop 2) -> V (hop 3).
        const string deep = "public class L2 { public int V; } public class L1 { public L2 B = new(); } ";
        var original = deep + "public class Holder { public L1 A = new(); public Holder() { A.B.V = 1; } }";
        var converted = deep + "public class Holder { public L1 A = new(); public Holder() { A.B.V = 2; } }";
        Assert.Equal(MemberStatus.Agree, Single(Compare(original, converted, ["Holder"], depth: 2), "Holder", "ctor()").Status);
        AssertDifferent(Single(Compare(original, converted, ["Holder"], depth: 3), "Holder", "ctor()"));
    }

    // ---- Inherited and interface members --------------------------------------------------------------------

    /// <summary>An abstract type's instance members are skipped there (excluded) and driven through a concrete subclass.</summary>
    [Fact]
    public void AbstractBaseMember_IsDrivenThroughItsSubclass()
    {
        const string sub = "public class Concrete : Base { } ";
        var verdicts = Compare(sub + "public abstract class Base { public int F(int x) => x + 1; }", sub + "public abstract class Base { public int F(int x) => x + 2; }", ["Base", "Concrete"]);
        AssertExcluded(Single(verdicts, "Base", "F(System.Int32)->System.Int32"), "abstract-receiver");
        AssertDifferent(Single(verdicts, "Concrete", "Base.F(System.Int32)->System.Int32"));
    }

    /// <summary>An interface member is skipped on the interface (excluded) and driven through its implementer.</summary>
    [Fact]
    public void InterfaceMember_IsDrivenThroughItsImplementer()
    {
        const string i = "public interface IShape { int Sides(); } ";
        var verdicts = Compare(i + "public class Square : IShape { public int Sides() => 4; }", i + "public class Square : IShape { public int Sides() => 5; }", ["IShape", "Square"]);
        AssertExcluded(Assert.Single(verdicts, v => v.Type == "IShape" && v.Kind == "interface-member"), "interface-member");
        AssertDifferent(Single(verdicts, "Square", "Sides()->System.Int32"));
    }

    /// <summary>A public implementation inherited from a base class is driven on the derived type too.</summary>
    [Fact]
    public void InterfaceImplementedByABaseClass_IsDrivenOnTheDerivedType()
    {
        const string i = "public interface IShape { int Sides(); } public class Square : Polygon { } ";
        var verdicts = Compare(i + "public class Polygon : IShape { public int Sides() => 4; }", i + "public class Polygon : IShape { public int Sides() => 5; }", ["IShape", "Polygon", "Square"]);
        AssertDifferent(Single(verdicts, "Square", "Polygon.Sides()->System.Int32"));
    }

    /// <summary>An explicit implementation of a closed generic framework interface is driven through the interface.</summary>
    [Fact]
    public void ExplicitGenericInterfaceImplementation_IsDriven()
    {
        var verdicts = Compare("public class ByLength : System.Collections.Generic.IComparer<string> { int System.Collections.Generic.IComparer<string>.Compare(string a, string b) => (a?.Length ?? 0) - (b?.Length ?? 0); }",
            "public class ByLength : System.Collections.Generic.IComparer<string> { int System.Collections.Generic.IComparer<string>.Compare(string a, string b) => (b?.Length ?? 0) - (a?.Length ?? 0); }", ["ByLength"]);
        AssertDifferent(Assert.Single(verdicts, v => v.Type == "ByLength" && v.Member.StartsWith("via System.Collections.Generic.IComparer<System.String>.Compare", StringComparison.Ordinal)));
    }

    /// <summary>A default interface implementation replaced by an explicit one in the converted build is compared under the same key.</summary>
    [Fact]
    public void DefaultImplementationReplacedByAnExplicitOne_IsDifferent()
    {
        var verdicts = Compare("public interface I { int F() => 1; } public class C : I { }", "public interface I { int F() => 1; } public class C : I { int I.F() => 2; }", ["I", "C"]);
        AssertDifferent(Assert.Single(verdicts, v => v.Type == "C" && v.Member.StartsWith("via I.F", StringComparison.Ordinal)));
    }

    // ---- Generic closing ------------------------------------------------------------------------------------

    /// <summary>False negative fixed: closing a generic method over object only hid a change that shows for a value type.</summary>
    [Fact]
    public void GenericMethod_DifferenceOnlyForAValueType_IsDifferent()
    {
        var verdicts = Compare("public static class S { public static bool IsDefault<T>(T v) => System.Collections.Generic.EqualityComparer<T>.Default.Equals(v, default); }",
            "public static class S { public static bool IsDefault<T>(T v) => v is null; }", ["S"]);
        Assert.Equal(MemberStatus.Agree, Single(verdicts, "static IsDefault<T>(T)->System.Boolean").Status);
        AssertDifferent(Single(verdicts, "static IsDefault<T>(T)->System.Boolean over System.Int32"), "(0)");
    }

    /// <summary>False negative fixed: the same for a generic type, now also closed over int.</summary>
    [Fact]
    public void GenericType_DifferenceOnlyForAValueType_IsDifferent()
    {
        var verdicts = Compare("public class Box<T> { public bool IsEmpty(T v) => System.Collections.Generic.EqualityComparer<T>.Default.Equals(v, default); }",
            "public class Box<T> { public bool IsEmpty(T v) => v is null; }", ["Box`1"]);
        Assert.Equal(MemberStatus.Agree, Single(verdicts, "Box<System.Object>", "IsEmpty(System.Object)->System.Boolean").Status);
        AssertDifferent(Single(verdicts, "Box<System.Int32>", "IsEmpty(System.Int32)->System.Boolean"));
    }

    /// <summary>
    /// A nested type of a generic type is driven for each closing under its own name. Its name used to show the open
    /// parent (<c>Outer&lt;T&gt;+Inner</c>), so the int closing collided with the object one and was never driven.
    /// </summary>
    [Fact]
    public void NestedTypeOfAGenericType_IsDrivenForEachClosing()
    {
        var verdicts = Compare("public class Outer<T> { public class Inner { public bool IsEmpty(T v) => System.Collections.Generic.EqualityComparer<T>.Default.Equals(v, default); } }",
            "public class Outer<T> { public class Inner { public bool IsEmpty(T v) => v is null; } }", ["Outer`1+Inner"]);
        Assert.Equal(MemberStatus.Agree, Single(verdicts, "Outer<System.Object>+Inner", "IsEmpty(System.Object)->System.Boolean").Status);
        AssertDifferent(Single(verdicts, "Outer<System.Int32>+Inner", "IsEmpty(System.Int32)->System.Boolean"));
        Assert.Equal(verdicts.Count, verdicts.Select(v => v.Key).Distinct().Count());
    }

    /// <summary>A generic whose constraints reject every closing argument is skipped and excluded from coverage.</summary>
    [Fact]
    public void GenericNoClosingFits_IsExcludedFromCoverage()
    {
        var verdicts = Compare("public static class S { public static int F<T>(T s) where T : System.IO.Stream => 1; }",
            "public static class S { public static int F<T>(T s) where T : System.IO.Stream => 2; }", ["S"]);
        AssertExcluded(Single(verdicts, "static F<T>(T)->System.Int32"), "open-generic");
    }

    // ---- SKIPPED: what the oracle cannot drive is excluded with a reason ------------------------------------------

    [Fact]
    public void UnsupportedParameter_IsExcludedFromCoverage()
    {
        var verdicts = Compare("public static class S { public static int F(System.Func<int> f) => 1; }", "public static class S { public static int F(System.Func<int> f) => 2; }", ["S"]);
        AssertExcluded(Single(verdicts, "static F(System.Func<System.Int32>)->System.Int32"), "unsupported-parameter");
    }

    [Fact]
    public void NoReceiver_IsExcludedFromCoverage()
    {
        const string ctor = "public C(System.Func<int> f) { } ";
        var verdicts = Compare("public class C { " + ctor + "public int F() => 1; }", "public class C { " + ctor + "public int F() => 2; }", ["C"]);
        AssertExcluded(Single(verdicts, "F()->System.Int32"), "no-receiver");
    }

    /// <summary>A receiver whose construction fails on every call (after planning chose it) compares nothing and is excluded.</summary>
    [Fact]
    public void ReceiverFailingOnEveryCall_IsExcludedFromCoverage()
    {
        const string ctor = "static int made; public C() { if (made++ > 0) throw new System.InvalidOperationException(); } ";
        var verdicts = Compare("public class C { " + ctor + "public int F() => 1; }", "public class C { " + ctor + "public int F() => 2; }", ["C"]);
        AssertExcluded(Single(verdicts, "F()->System.Int32"), "no-input-compared");
    }

    [Fact]
    public void ByRefReturn_IsExcludedFromCoverage()
    {
        const string source = "public static class S { static int x; public static ref int F() => ref x; }";
        AssertExcluded(Single(Compare(source, source, ["S"]), "static F()->ref System.Int32"), "byref-return");
    }

    // ---- Skip-reason distribution -----------------------------------------------------------------------------

    private static OracleProjectReport SkipReport(int skipped, string? shard = null, int files = 1)
    {
        var report = new OracleProjectReport { Project = "P", Shard = shard };
        for (var f = 0; f < files; f++)
        {
            report.Files.Add(new OracleFileReport
            {
                File = $"f{f}.cs", Status = "exercised",
                Members = Enumerable.Range(0, skipped).Select(i => new MemberVerdict
                {
                    Type = "T", Member = $"M{i}()", Kind = "static", Status = MemberStatus.Skipped, Reason = "unsupported parameter type System.Func<System.Int32>",
                }).Append(new MemberVerdict { Type = "T", Member = "Ok()", Kind = "static", Status = MemberStatus.Agree, Inputs = 1 }).ToList(),
            });
        }
        return report;
    }

    private static OracleExpected Recorded(int unsupported) => new()
    {
        Projects = { ["P"] = new() { ["f0.cs"] = 1, ["f1.cs"] = 1 } },
        Skips = { ["P"] = new() { ["unsupported-parameter"] = unsupported } },
    };

    [Fact]
    public void SkipDistribution_CountsDistinctMembersAcrossFiles()
    {
        var report = SkipReport(skipped: 7, files: 2);
        Assert.Equal(new Dictionary<string, int> { ["unsupported-parameter"] = 7 }, report.SkipDistribution);
    }

    [Fact]
    public void SkipGate_MaterialRise_Fails()
    {
        var report = SkipReport(skipped: 43, files: 2);
        OracleRunner.ApplyGate("P", new OracleBaseline(), Recorded(40), report, ["f0.cs", "f1.cs"], new HashSet<string> { "f0.cs", "f1.cs" });
        Assert.Contains(report.Gate, g => g is { Member: "<skips:unsupported-parameter>", Decision: "failing" });
        Assert.False(report.Passed);
    }

    [Fact]
    public void SkipGate_RiseWithinTolerance_Passes()
    {
        var report = SkipReport(skipped: 42, files: 2);
        OracleRunner.ApplyGate("P", new OracleBaseline(), Recorded(40), report, ["f0.cs", "f1.cs"], new HashSet<string> { "f0.cs", "f1.cs" });
        Assert.True(report.Passed, string.Join("; ", report.Gate.Select(g => g.Detail)));
    }

    /// <summary>A category with no recorded members fails once it holds more than 2 (a new way to lose coverage).</summary>
    [Fact]
    public void SkipGate_NewCategory_FailsAboveTheMinimumTolerance()
    {
        var report = SkipReport(skipped: 0, files: 2);
        foreach (var file in report.Files)
            file.Members.AddRange(Enumerable.Range(0, 3).Select(i => new MemberVerdict
                { Type = "T", Member = $"N{i}()", Kind = "static", Status = MemberStatus.Skipped, Reason = "incomplete observation: the result part could not be rendered in full on either build" }));
        OracleRunner.ApplyGate("P", new OracleBaseline(), Recorded(0), report, ["f0.cs", "f1.cs"], new HashSet<string> { "f0.cs", "f1.cs" });
        Assert.Contains(report.Gate, g => g is { Member: "<skips:incomplete-observation>", Decision: "failing" });
    }

    /// <summary>A shard sees only part of the files, so the project-level distribution is checked unsharded only.</summary>
    [Fact]
    public void SkipGate_IsNotAppliedInAShard()
    {
        var report = SkipReport(skipped: 80, shard: "1/2", files: 2);
        OracleRunner.ApplyGate("P", new OracleBaseline(), Recorded(40), report, ["f0.cs", "f1.cs"], new HashSet<string> { "f0.cs", "f1.cs" });
        Assert.DoesNotContain(report.Gate, g => g.Member.StartsWith("<skips:", StringComparison.Ordinal));
    }

    [Fact]
    public void SkipDistribution_IsInTheJobSummary()
    {
        var markdown = OracleRunner.Markdown([SkipReport(skipped: 3)], Recorded(1));
        Assert.Contains("| Skip category | Distinct members | Recorded | Change |", markdown, StringComparison.Ordinal);
        Assert.Contains("| unsupported-parameter | 3 | 1 | +2 |", markdown, StringComparison.Ordinal);
    }

    /// <summary>The committed expected file records a skip distribution for every project it lists, so the check is on in CI.</summary>
    [Fact]
    public void CommittedExpectedFile_RecordsSkipsForEveryProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eng", "member-oracle-expected.json")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var expected = OracleExpected.Load(OracleExpected.DefaultPath(dir.FullName));
        Assert.NotEmpty(expected.Projects);
        Assert.All(expected.Projects.Keys, project => Assert.NotNull(expected.SkipsFor(project)));
    }

    [Fact]
    public void SkipCategory_MapsEveryReasonTheOracleWrites()
    {
        static string? Of(string status, string? reason, int inputs = 0) =>
            SkipCategory.Of(new MemberVerdict { Type = "T", Member = "M", Kind = "static", Status = status, Reason = reason, Inputs = inputs });
        Assert.Equal("no-receiver", Of(MemberStatus.Skipped, "no public constructor the generator can supply, for a receiver"));
        Assert.Equal("no-receiver", Of(MemberStatus.Skipped, "receiver construction timed out on an earlier attempt"));
        Assert.Equal("interface-member", Of(MemberStatus.Skipped, "interface member (surface; driven through implementing types)"));
        Assert.Equal("open-generic", Of(MemberStatus.Skipped, "open generic type (no closing argument meets its constraints)"));
        Assert.Equal("no-input-compared", Of(MemberStatus.Skipped, "no input could be compared: setup or observation failed"));
        Assert.Equal("other", Of(MemberStatus.Skipped, "something new"));
        Assert.Equal("inconclusive", Of(MemberStatus.Inconclusive, "x"));
        Assert.Null(Of(MemberStatus.Timeout, "prefix", inputs: 5));
        Assert.Null(Of(MemberStatus.Different, "OBSERVATION"));
    }

    // ---- Codex round 1 (#1667) --------------------------------------------------------------------------------

    /// <summary>The console routers outlive load contexts: a NewLine the original set must not carry into the converted run.</summary>
    [Fact]
    public void Console_WriterStateDoesNotLeakBetweenBuilds()
    {
        var verdicts = Compare("public static class S { public static void F() { System.Console.Out.NewLine = \"!\"; System.Console.WriteLine(\"x\"); } }",
            "public static class S { public static void F() { System.Console.WriteLine(\"x\"); } }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Void"), "[stdout]");
    }

    /// <summary>Identical low-entropy randomness (a coin flip repeats by chance) is never a one-sided difference.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void LowEntropyRandomness_OnIdenticalBuilds_IsNeverDifferent(int outcomes)
    {
        var source = "public static class S { public static int F() => System.Random.Shared.Next(" + outcomes + "); }";
        for (var attempt = 0; attempt < 4; attempt++)
            Assert.NotEqual(MemberStatus.Different, Single(Compare(source, source, ["S"]), "static F()->System.Int32").Status);
    }

    /// <summary>A one-sided fingerprint covers every affected input, so a baseline stops matching when another input is affected.</summary>
    [Fact]
    public void OneSidedNondeterminism_FingerprintCoversEveryAffectedInput()
    {
        const string original = "public static class S { public static string F(bool b) => \"fixed\"; }";
        var onlyFalse = Single(Compare(original, "public static class S { public static string F(bool b) => b ? \"fixed\" : System.Guid.NewGuid().ToString(); }", ["S"]),
            "static F(System.Boolean)->System.String");
        var both = Single(Compare(original, "public static class S { public static string F(bool b) => System.Guid.NewGuid().ToString(); }", ["S"]),
            "static F(System.Boolean)->System.String");
        AssertDifferent(onlyFalse);
        AssertDifferent(both);
        Assert.NotEqual(onlyFalse.Fingerprint, both.Fingerprint);
    }

    /// <summary>The storage of a private static auto-property is private state too.</summary>
    [Fact]
    public void PrivateStaticAutoProperty_WrittenAfterItsOnlyReader_IsDifferent()
    {
        var verdicts = Compare("public static class S { static int N { get; set; } public static int Get() => N; public static void Set(int x) { N = x * 2; } }",
            "public static class S { static int N { get; set; } public static int Get() => N; public static void Set(int x) { N = x * 3; } }", ["S"]);
        AssertDifferent(Single(verdicts, "static Set(System.Int32)->System.Void"), "N (auto-property)");
    }

    /// <summary>A private delegate the generator can call is called, so a replaced callback whose only reader ran earlier is seen.</summary>
    [Fact]
    public void PrivateStaticCallableDelegate_ReplacedAfterItsOnlyReader_IsDifferent()
    {
        const string head = "public static class S { static System.Func<int> cache = () => 0; public static int Get() => cache(); public static void Set(int x) { cache = () => ";
        var verdicts = Compare(head + "x; } }", head + "x + 1; } }", ["S"]);
        AssertDifferent(Single(verdicts, "static Set(System.Int32)->System.Void"), "[private-statics]");
    }

    /// <summary>A suffix segment shares its member's sandbox, so it sees the files the earlier inputs wrote.</summary>
    [Fact]
    public void SuffixSegment_SeesTheFilesTheMembersEarlierInputsWrote()
    {
        const string body = "if (n == 0) System.IO.File.WriteAllText(\"marker\", \"x\"); if (n == int.MaxValue) System.Threading.Thread.Sleep(4000); "
            + "return n == int.MinValue && System.IO.File.Exists(\"marker\") ? ";
        var verdicts = Compare("public static class S { public static int F(int n) { " + body + "1 : 0; } }",
            "public static class S { public static int F(int n) { " + body + "2 : 0; } }", ["S"], timeoutSeconds: 1);
        AssertDifferent(Single(verdicts, "static F(System.Int32)->System.Int32 [inputs 14..]"), "#14");
    }

    /// <summary>A segment that itself times out is replayed like any member (the probe derives the same segments).</summary>
    [Fact]
    public void SuffixSegment_ThatTimesOut_IsReplayedAndTheNextSegmentCompared()
    {
        const string slow = "if (n == int.MaxValue || n == int.MinValue) System.Threading.Thread.Sleep(4000);";
        var verdicts = Compare("public static class S { public static int F(int n) { " + slow + " return n; } }",
            "public static class S { public static int F(int n) { " + slow + " return n < -100 && n > -1000 ? n + 1 : n; } }", ["S"], timeoutSeconds: 1);
        var segment = Single(verdicts, "static F(System.Int32)->System.Int32 [inputs 14..]");
        Assert.Equal(MemberStatus.Timeout, segment.Status);
        Assert.Contains("on both builds (replayed from clean state)", segment.Reason, StringComparison.Ordinal);
        AssertDifferent(Single(verdicts, "static F(System.Int32)->System.Int32 [inputs 15..]"));
    }

    /// <summary>A member and its segments are one member for coverage and skips: segments never inflate either count.</summary>
    [Fact]
    public void Segments_CountOnceForCoverageAndSkips()
    {
        MemberVerdict V(string member, string status, int inputs, string? origin = null, string? reason = null) =>
            new() { Type = "T", Member = member, Kind = "static", Status = status, Inputs = inputs, Origin = origin, Reason = reason };
        var file = new OracleFileReport
        {
            File = "a.cs", Status = "exercised",
            Members =
            [
                V("F()", MemberStatus.Timeout, 0),
                V("F() [inputs 1..]", MemberStatus.Agree, 5, origin: "F()"),
                V("F() [inputs 7..]", MemberStatus.Agree, 5, origin: "F()"),
                V("G()", MemberStatus.Timeout, 0),
                V("G() [inputs 1..]", MemberStatus.Timeout, 0, origin: "G()"),
            ],
        };
        Assert.Equal(1, OracleRunner.Compared(file));
        Assert.Equal(new Dictionary<string, int> { ["timeout"] = 1 }, OracleRunner.SkipDistributionOf([file]));
    }

    /// <summary>A subject that throws a wrapper exception keeps the wrapper: only the oracle's own reflection wrapper is removed.</summary>
    [Fact]
    public void SubjectThrownWrapperExceptions_AreKept()
    {
        const string original = "public static class S { public static void F() => throw new System.InvalidOperationException(\"m\"); }";
        AssertDifferent(Single(Compare(original,
            "public static class S { public static void F() => throw new System.AggregateException(\"changed\", new System.InvalidOperationException(\"m\")); }", ["S"]),
            "static F()->System.Void"), "AggregateException");
        AssertDifferent(Single(Compare(original,
            "public static class S { public static void F() => throw new System.Reflection.TargetInvocationException(new System.InvalidOperationException(\"m\")); }", ["S"]),
            "static F()->System.Void"), "TargetInvocationException");
    }

    /// <summary>A faulted task keeps a subject AggregateException inside the task's own wrapper.</summary>
    [Fact]
    public void FaultedTask_SubjectAggregateException_IsKept()
    {
        const string head = "public static class S { public static async System.Threading.Tasks.Task F() { await System.Threading.Tasks.Task.Yield(); throw ";
        var verdicts = Compare(head + "new System.InvalidOperationException(\"m\"); } }",
            head + "new System.AggregateException(\"changed\", new System.InvalidOperationException(\"m\")); } }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Threading.Tasks.Task"), "AggregateException");
    }

    /// <summary>Two crashes with different exit codes are a difference; a build whose crashes disagree is nondeterministic.</summary>
    [Fact]
    public void Replay_CrashesWithDifferentExitCodes()
    {
        var prefix = new MemberVerdict { Type = "T", Member = "F()", Kind = "static", Status = MemberStatus.Timeout, LimitedAt = 3, Inputs = 3 };
        var options = new OracleOptions { MemberTimeout = TimeSpan.FromSeconds(4) };
        ProbeResult Crash(int code) => new(ProbeOutcome.Crashed, 0, $"probe process exit {code}", ExitCode: code);
        var different = MemberDifferentialOracle.ClassifyTimeout(prefix, [Crash(11), Crash(11)], [Crash(12), Crash(12)], options);
        Assert.Equal(MemberStatus.Different, different.Status);
        Assert.Equal("FAILURE-MODE", different.Reason);
        Assert.Equal(MemberStatus.Timeout, MemberDifferentialOracle.ClassifyTimeout(prefix, [Crash(11), Crash(11)], [Crash(11), Crash(11)], options).Status);
        Assert.Equal(MemberStatus.Nondeterministic, MemberDifferentialOracle.ClassifyTimeout(prefix, [Crash(11), Crash(12)], [Crash(12), Crash(12)], options).Status);
    }

    /// <summary>A file name with the observation's separators is escaped, so it cannot replace another part.</summary>
    [Fact]
    public void DirectoryNameWithSeparators_CannotReplaceAPart()
    {
        const string dir = "System.IO.Directory.CreateDirectory(\"\\u001eresult\\u001freturn=0\");";
        var verdicts = Compare("public static class S { public static int F() { " + dir + " return 1; } }", "public static class S { public static int F() { " + dir + " return 2; } }", ["S"]);
        AssertDifferent(Single(verdicts, "static F()->System.Int32"), "[result]");
    }

    [Fact]
    public void Parts_RepeatedNameNeverOverwritesTheFirst()
    {
        var parts = MemberDifferentialOracle.Parts(Obs(("result", "return=1"), ("files", "x"), ("result", "return=0")));
        Assert.Equal("return=1", parts["result"]);
        Assert.Equal("return=0", parts["result#2"]);
    }

    /// <summary>The sandbox root renders as a token no literal string can produce.</summary>
    [Fact]
    public void SandboxRoot_DoesNotAliasALiteral()
    {
        const string original = "public static class S { public static string F() => System.IO.Directory.GetCurrentDirectory(); }";
        AssertDifferent(Single(Compare(original, "public static class S { public static string F() => \"<sandbox>\"; }", ["S"]), "static F()->System.String"));
        AssertDifferent(Single(Compare(original, "public static class S { public static string F() => \"§sandbox§\"; }", ["S"]), "static F()->System.String"));
        Assert.Equal(MemberStatus.Agree, Single(Compare(original, original, ["S"]), "static F()->System.String").Status);
    }

    /// <summary>An abstract declaration is surface: changing its signature, or making it concrete, is a difference.</summary>
    [Fact]
    public void AbstractDeclaration_SignatureChange_IsASurfaceDifference()
    {
        var changed = Compare("public abstract class A { public abstract int F(); }", "public abstract class A { public abstract string F(); }", ["A"]);
        Assert.Contains(changed, v => v.Type == "A" && v.Member.StartsWith("abstract F()", StringComparison.Ordinal) && v.Reason == "SURFACE");
        var concrete = Compare("public abstract class A { public abstract int F(); }", "public abstract class A { public virtual int F() => 0; }", ["A"]);
        Assert.Contains(concrete, v => v.Type == "A" && v.Reason == "SURFACE");
        var same = Compare("public abstract class A { public abstract int F(); }", "public abstract class A { public abstract int F(); }", ["A"]);
        AssertExcluded(Single(same, "A", "abstract F()->System.Int32"), "surface-only");
    }

    /// <summary>A delegate type is surface: a changed signature is a difference, not a dropped type.</summary>
    [Fact]
    public void DelegateType_SignatureChange_IsASurfaceDifference()
    {
        var verdicts = Compare("public delegate int Handler(int x);", "public delegate long Handler(int x);", ["Handler"]);
        Assert.Contains(verdicts, v => v.Type == "Handler" && v.Reason == "SURFACE");
    }

    /// <summary>A reviewed baseline entry tolerates a file failure only with the same fingerprint; any change fails.</summary>
    [Fact]
    public void Gate_BaselinedFileFailure_PassesOnlyWithTheSameFingerprint()
    {
        OracleProjectReport Report(string detail) => new()
        {
            Project = "P", Files = { new OracleFileReport { File = "a.cs", Status = "candidate-build-failed", Detail = detail } },
        };
        var same = Report("dotnet build failed (1): error CS0103: The name 'Foo' does not exist");
        var entry = new OracleBaselineEntry { Project = "P", File = "a.cs", Member = "<file>", Finding = "f", Fingerprint = OracleRunner.FailureFingerprint(same.Files[0]), Witness = "w" };
        OracleRunner.ApplyGate("P", new OracleBaseline { Entries = [entry] }, new OracleExpected(), same, ["a.cs"], new HashSet<string> { "a.cs" });
        Assert.True(same.Passed);
        var changed = Report("dotnet build failed (1): error CS0103: The name 'Bar' does not exist");
        OracleRunner.ApplyGate("P", new OracleBaseline { Entries = [entry] }, new OracleExpected(), changed, ["a.cs"], new HashSet<string> { "a.cs" });
        Assert.Contains(changed.Gate, g => g.Decision == "baseline-changed");
    }
}
