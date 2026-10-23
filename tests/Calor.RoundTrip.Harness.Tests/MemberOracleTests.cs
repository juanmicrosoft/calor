using System.Globalization;
using Calor.RoundTrip.Harness.Oracle;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// #1587: the member-level differential oracle. Each fixture compiles an "original" and a "converted"
/// C# source in memory and checks what the oracle reports. The issue's repro runs through the real
/// converter; every other miscompilation is injected by hand so the oracle is tested independently of
/// what the converter does today. The oracle redirects the process-wide console and working directory
/// while it drives members, so this class never runs in parallel with other tests.
/// </summary>
[Collection(MemberOracleCollection.Name)]
public class MemberOracleTests
{
    private static readonly Lazy<List<MetadataReference>> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Order(StringComparer.Ordinal).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList());

    /// <summary>The issue's two-member repro, verbatim.</summary>
    public const string HolderSource = """
        using System.Collections.Generic;
        using System.Text;

        public class Holder
        {
            public List<int> Xs = new List<int> { 5 };
            public StringBuilder Sb { get; set; } = new StringBuilder { Capacity = 64 };
        }
        """;

    /// <summary>What <c>calor convert</c> then <c>calor --input</c> produced for the repro at HEAD 512491093 and 54ae66ee9.</summary>
    public const string HolderMiscompiled = """
        using System.Collections.Generic;
        using System.Text;

        public class Holder
        {
            public List<int> Xs = new List<int>(5);
            public StringBuilder Sb { get; set; } = new System.Text.StringBuilder();
        }
        """;

    internal static ArmImage Compile(string name, string source)
    {
        var compilation = CSharpCompilation.Create("OracleFixture",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), "Fixture.cs")],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, deterministic: true));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return new ArmImage(name, stream.ToArray(), new Dictionary<string, string>());
    }

    private static List<MemberVerdict> Compare(string original, string converted, string[] types, int depth = StateRenderer.DefaultDepth, double timeoutSeconds = 5) =>
        MemberDifferentialOracle.Compare(Compile("original", original), Compile("converted", converted), types,
            new OracleOptions { Depth = depth, SeedKey = "fixture", MemberTimeout = TimeSpan.FromSeconds(timeoutSeconds) });

    private static MemberVerdict Single(List<MemberVerdict> verdicts, string member) =>
        Assert.Single(verdicts, v => v.Member == member);

    // ---- The issue's acceptance criterion -----------------------------------------------------

    [Fact]
    public void Repro_InjectedMiscompilation_FailsAtConstructionDepth2()
    {
        var verdicts = Compare(HolderSource, HolderMiscompiled, ["Holder"]);
        var ctor = Single(verdicts, "ctor()");
        Assert.Equal(MemberStatus.Different, ctor.Status);
        Assert.Contains("Capacity=64", ctor.Witnesses[0], StringComparison.Ordinal);
        // The element 5 became a capacity argument: the list is empty.
        Assert.Contains("Xs=System.Collections.Generic.List<System.Int32>[]", ctor.Witnesses[0], StringComparison.Ordinal);
    }

    /// <summary>The StringBuilder drop alone is invisible to ToString() and to depth 1; depth 2 catches it.</summary>
    [Fact]
    public void Repro_CapacityDrop_IsVisibleOnlyAtDepth2()
    {
        const string original = "public class Holder { public System.Text.StringBuilder Sb { get; set; } = new System.Text.StringBuilder { Capacity = 64 }; }";
        const string converted = "public class Holder { public System.Text.StringBuilder Sb { get; set; } = new System.Text.StringBuilder(); }";
        Assert.Equal(MemberStatus.Agree, Single(Compare(original, converted, ["Holder"], depth: 1), "ctor()").Status);
        Assert.Equal(MemberStatus.Different, Single(Compare(original, converted, ["Holder"], depth: 2), "ctor()").Status);
    }

    /// <summary>
    /// The real converter, with the CLI's default (Lossless) options. While the converter still miscompiles
    /// the repro (#1541, #1524), the oracle must flag it; once it is fixed, the round-trip must agree.
    /// </summary>
    [Fact]
    public void Repro_RealConverter_IsFlaggedWhileTheConverterStillMiscompilesIt()
    {
        var result = OracleRunner.RoundTrip(HolderSource, "Holder.cs", global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot);
        Assert.Equal("converted", result.Status);
        Assert.False(result.LossRecorded, "the repro is reported lossless; the oracle is what catches it");
        var ctor = Single(Compare(HolderSource, result.CSharp!, ["Holder"]), "ctor()");
        var stillBroken = result.CSharp!.Contains("new List<int>(5)", StringComparison.Ordinal)
            || !result.CSharp.Contains("Capacity", StringComparison.Ordinal);
        Assert.Equal(stillBroken ? MemberStatus.Different : MemberStatus.Agree, ctor.Status);
    }

    // ---- Methods ------------------------------------------------------------------------------

    [Fact]
    public void InstanceMethod_ReturnValueDifference_IsDifferent()
    {
        var verdicts = Compare(
            "public class C { public int Bump(int x) => x + 1; }",
            "public class C { public int Bump(int x) => x + 2; }", ["C"]);
        var bump = Single(verdicts, "Bump(System.Int32)->System.Int32");
        Assert.Equal(MemberStatus.Different, bump.Status);
        Assert.Equal("OBSERVATION", bump.Reason);
        Assert.Equal(3, bump.Witnesses.Count);
    }

    [Fact]
    public void StaticMethod_ExceptionTypeDifference_IsDifferent()
    {
        var verdicts = Compare(
            "public static class S { public static int F(string s) => s.Length; }",
            "public static class S { public static int F(string s) => s?.Length ?? throw new System.ArgumentNullException(); }", ["S"]);
        var f = Single(verdicts, "static F(System.String)->System.Int32");
        Assert.Equal(MemberStatus.Different, f.Status);
        Assert.Contains("NullReferenceException", f.Witnesses[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Setter_PostCallReceiverState_IsCompared()
    {
        var verdicts = Compare(
            "public class C { private int _v; public int V { get => _v; set => _v = value; } }",
            "public class C { private int _v; public int V { get => _v; set => _v = -value; } }", ["C"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "set_V(System.Int32)->System.Void").Status);
    }

    [Fact]
    public void ArgumentState_AfterTheCall_IsCompared()
    {
        var verdicts = Compare(
            "public static class S { public static void Fill(int[] a) { if (a != null && a.Length > 0) a[0] = 9; } public static void Out(out int x) { x = 1; } }",
            "public static class S { public static void Fill(int[] a) { } public static void Out(out int x) { x = 2; } }", ["S"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "static Fill(System.Int32[])->System.Void").Status);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "static Out(out ref System.Int32)->System.Void").Status);
    }

    [Fact]
    public void GenericTypesAndMethods_AreClosedAndDriven()
    {
        var verdicts = Compare(
            "public class Box<T> { public T Value { get; set; } public int Count<U>(U[] items) => items?.Length ?? 0; }",
            "public class Box<T> { public T Value { get; set; } public int Count<U>(U[] items) => items?.Length ?? 1; }", ["Box`1"]);
        Assert.Contains(verdicts, v => v.Type == "Box<System.Object>" && v.Member == "ctor()" && v.Status == MemberStatus.Agree);
        Assert.Contains(verdicts, v => v.Type == "Box<System.Object>" && v.Member.StartsWith("Count<U>", StringComparison.Ordinal) && v.Status == MemberStatus.Different);
    }

    [Fact]
    public void EnumValues_AreCompared()
    {
        var verdicts = Compare("public enum E { A = 1, B = 2 }", "public enum E { A = 1, B = 3 }", ["E"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "<enum-values>").Status);
    }

    [Fact]
    public void SurfaceDifference_IsDifferent()
    {
        var verdicts = Compare(
            "public class C { public int A() => 1; public int B() => 2; }",
            "public class C { public int A() => 1; }", ["C"]);
        var b = Single(verdicts, "B()->System.Int32");
        Assert.Equal(MemberStatus.Different, b.Status);
        Assert.Equal("SURFACE", b.Reason);
    }

    [Fact]
    public void IdenticalBuilds_AllAgree()
    {
        const string source = """
            public class Account
            {
                public decimal Balance { get; private set; }
                public System.Collections.Generic.List<string> Log { get; } = new() { "open" };
                public void Deposit(decimal amount) { if (amount <= 0) throw new System.ArgumentOutOfRangeException(); Balance += amount; Log.Add("d"); }
                public static string Describe(Kind k, bool? flag) => k + ":" + flag;
            }
            public enum Kind { Cash, Card }
            """;
        var verdicts = Compare(source, source, ["Account", "Kind"]);
        Assert.All(verdicts, v => Assert.Equal(MemberStatus.Agree, v.Status));
        Assert.Contains(verdicts, v => v.Member.StartsWith("Deposit", StringComparison.Ordinal) && v.Inputs > 1);
    }

    // ---- Skips, never failures --------------------------------------------------------------

    [Fact]
    public void NondeterministicMember_IsSkippedNotFailed()
    {
        const string source = "public class C { public string Id { get; } = System.Guid.NewGuid().ToString(); }";
        var ctor = Single(Compare(source, source, ["C"]), "ctor()");
        Assert.Equal(MemberStatus.Nondeterministic, ctor.Status);
    }

    /// <summary>An unstable input never hides a difference on the inputs that are stable on both builds.</summary>
    [Fact]
    public void StableInputDifference_IsDifferentEvenWhenAnotherInputIsUnstable()
    {
        const string body = "if (b) return System.Guid.NewGuid().ToString();";
        var verdicts = Compare(
            "public static class S { public static string F(bool b) { " + body + " return \"x\"; } }",
            "public static class S { public static string F(bool b) { " + body + " return \"y\"; } }", ["S"]);
        var f = Single(verdicts, "static F(System.Boolean)->System.String");
        Assert.Equal(MemberStatus.Different, f.Status);
        Assert.Contains("(false)", f.Witnesses[0], StringComparison.Ordinal);
    }

    /// <summary>Each run gets a fresh load context, so deterministic static mutation is not mistaken for nondeterminism.</summary>
    [Fact]
    public void StaticStateMutation_IsDeterministicAcrossRuns()
    {
        const string original = "public static class Counter { public static int Count; public static int Next() => ++Count; }";
        const string converted = "public static class Counter { public static int Count; public static int Next() => Count += 2; }";
        Assert.Equal(MemberStatus.Agree, Single(Compare(original, original, ["Counter"]), "static Next()->System.Int32").Status);
        Assert.Equal(MemberStatus.Different, Single(Compare(original, converted, ["Counter"]), "static Next()->System.Int32").Status);
    }

    [Fact]
    public void UnsupportedParameters_AreSkippedWithAReason()
    {
        const string source = "public static class S { public static int Apply(System.Func<int, int> f) => f(1); }";
        var apply = Single(Compare(source, source, ["S"]), "static Apply(System.Func<System.Int32,System.Int32>)->System.Int32");
        Assert.Equal(MemberStatus.Skipped, apply.Status);
        Assert.StartsWith("unsupported parameter type", apply.Reason, StringComparison.Ordinal);
    }

    /// <summary>A converted build that hangs where the original returns at once is a difference, not a timeout skip.</summary>
    [Fact]
    public void ConvertedHang_IsDifferent()
    {
        var verdicts = Compare(
            "public static class S { public static int F(bool b) => b ? 1 : 0; }",
            "public static class S { public static int F(bool b) { if (b) System.Threading.Thread.Sleep(4000); return b ? 1 : 0; } }",
            ["S"], timeoutSeconds: 1);
        var f = Single(verdicts, "static F(System.Boolean)->System.Int32");
        Assert.Equal(MemberStatus.Different, f.Status);
        Assert.Equal("HANG", f.Reason);
    }

    /// <summary>When both builds time out, the inputs that finished before the timeout are still compared.</summary>
    [Fact]
    public void SharedTimeout_StillComparesTheFinishedPrefix()
    {
        const string slow = "if (n == int.MaxValue) System.Threading.Thread.Sleep(4000);";
        var verdicts = Compare(
            "public static class S { public static int F(int n) { " + slow + " return n; } }",
            "public static class S { public static int F(int n) { " + slow + " return n == 7 ? 8 : n; } }",
            ["S"], timeoutSeconds: 1);
        var f = Single(verdicts, "static F(System.Int32)->System.Int32");
        Assert.Equal(MemberStatus.Different, f.Status);
        Assert.Contains("(7)", f.Witnesses[0], StringComparison.Ordinal);
    }

    // ---- Gate ---------------------------------------------------------------------------------

    private static OracleProjectReport GateReport(bool lossRecorded, string witness = "w1") => new()
    {
        Project = "P",
        Files =
        {
            new OracleFileReport
            {
                File = "a.cs", Status = "exercised", LossRecorded = lossRecorded,
                Members =
                [
                    new MemberVerdict { Type = "T", Member = "M()", Kind = "instance", Status = MemberStatus.Different, Witnesses = [witness], Fingerprint = "fp-" + witness },
                    new MemberVerdict { Type = "T", Member = "N()", Kind = "instance", Status = MemberStatus.Agree },
                ],
            },
        },
    };

    /// <summary>The gate fixtures' file a.cs is expected to be exercised.</summary>
    private static readonly OracleExpected Expected = new() { Projects = { ["P"] = ["a.cs"] } };

    private static OracleBaselineEntry Entry(string member, string witness) =>
        new() { Project = "P", File = "a.cs", Member = member, Finding = "finding", Fingerprint = "fp-" + witness, Witness = witness };

    [Fact]
    public void Gate_UnlistedDifferenceOnALosslessFile_Fails()
    {
        var report = GateReport(lossRecorded: false);
        OracleRunner.ApplyGate("P", new OracleBaseline(), Expected, report, ["a.cs"]);
        Assert.False(report.Passed);
        Assert.Equal("failing", Assert.Single(report.Gate).Decision);
    }

    [Fact]
    public void Gate_DifferenceOnAFileWithARecordedLoss_IsTolerated()
    {
        var report = GateReport(lossRecorded: true);
        OracleRunner.ApplyGate("P", new OracleBaseline(), Expected, report, ["a.cs"]);
        Assert.True(report.Passed);
        Assert.Equal("loss-recorded", Assert.Single(report.Gate).Decision);
    }

    [Fact]
    public void Gate_BaselinedDifference_PassesOnlyWithTheSameFingerprint()
    {
        var same = GateReport(lossRecorded: false);
        OracleRunner.ApplyGate("P", new OracleBaseline { Entries = [Entry("T::M()", "w1")] }, Expected, same, ["a.cs"]);
        Assert.True(same.Passed);

        var changed = GateReport(lossRecorded: false, witness: "w2");
        OracleRunner.ApplyGate("P", new OracleBaseline { Entries = [Entry("T::M()", "w1")] }, Expected, changed, ["a.cs"]);
        Assert.False(changed.Passed);
        Assert.Equal("baseline-changed", Assert.Single(changed.Gate).Decision);
    }

    [Fact]
    public void Gate_BaselineEntryForAnAgreeingMember_IsStale()
    {
        var report = GateReport(lossRecorded: true);
        OracleRunner.ApplyGate("P", new OracleBaseline { Entries = [Entry("T::N()", "w")] }, Expected, report, ["a.cs"]);
        Assert.False(report.Passed);
        Assert.Contains(report.Gate, g => g.Decision == "baseline-stale" && g.Member == "T::N()");
    }

    /// <summary>A file the oracle could not finish (a crash outside any member, a process timeout) fails the gate.</summary>
    [Fact]
    public void Gate_FileFailure_Fails()
    {
        var report = new OracleProjectReport
        {
            Project = "P",
            Files = { new OracleFileReport { File = "a.cs", Status = "oracle-crashed", Checkpoint = "plan", Detail = "exit 134" } },
        };
        OracleRunner.ApplyGate("P", new OracleBaseline(), Expected, report, ["a.cs"]);
        Assert.False(report.Passed);
        Assert.Contains(report.Gate, g => g is { File: "a.cs", Member: "<file>", Decision: "failing" });
    }

    /// <summary>Coverage is checked by file identity: an expected file that is no longer exercised fails, even if another file is new.</summary>
    [Fact]
    public void Gate_ExpectedFileNoLongerExercised_Fails()
    {
        var report = new OracleProjectReport
        {
            Project = "P",
            Files =
            {
                new OracleFileReport { File = "a.cs", Status = "swap-build-failed" },
                new OracleFileReport { File = "b.cs", Status = "exercised" },
            },
        };
        OracleRunner.ApplyGate("P", new OracleBaseline(), Expected, report, ["a.cs", "b.cs"]);
        Assert.False(report.Passed);
        Assert.Contains(report.Gate, g => g is { File: "a.cs", Member: "<coverage>", Decision: "failing" });
        Assert.Contains(report.Gate, g => g is { File: "b.cs", Decision: "new-coverage" });
    }

    [Fact]
    public void Gate_TooFewComparedMembers_Fails()
    {
        var report = GateReport(lossRecorded: true) with { MinimumComparedMembers = 5 };
        OracleRunner.ApplyGate("P", new OracleBaseline(), Expected, report, ["a.cs"]);
        Assert.Contains(report.Gate, g => g.Member == "<coverage>" && g.Decision == "failing");
    }

    [Fact]
    public void Gate_InconclusiveMember_Fails()
    {
        var report = GateReport(lossRecorded: true);
        report.Files[0].Members.Add(new MemberVerdict { Type = "T", Member = "P()", Kind = "static", Status = MemberStatus.Inconclusive, Reason = "replay never reached the input" });
        OracleRunner.ApplyGate("P", new OracleBaseline(), Expected, report, ["a.cs"]);
        Assert.Contains(report.Gate, g => g.Member == "T::P()" && g.Decision == "failing");
    }

    [Fact]
    public void Gate_InconclusiveRun_Fails()
    {
        var report = new OracleProjectReport { Project = "P", Inconclusive = "original does not compile" };
        Assert.False(report.Passed);
    }

    /// <summary>A baselined member that gains another differing input changes its fingerprint, so the gate fails.</summary>
    [Fact]
    public void Fingerprint_CoversEveryDifferingInput()
    {
        const string original = "public static class S { public static int F(int x) => x; }";
        var one = Single(Compare(original, "public static class S { public static int F(int x) => x == 0 ? 9 : x; }", ["S"]), "static F(System.Int32)->System.Int32");
        var two = Single(Compare(original, "public static class S { public static int F(int x) => x == 0 ? 9 : x == 7 ? 8 : x; }", ["S"]), "static F(System.Int32)->System.Int32");
        Assert.Equal(one.Witnesses[0], two.Witnesses[0]);
        Assert.NotEqual(one.Fingerprint, two.Fingerprint);
    }

    // ---- Codex round 1 false negatives --------------------------------------------------------

    [Fact]
    public void InternalTypeWithAPublicConstructor_IsExercised()
    {
        var verdicts = Compare("internal class C { public int X = 1; }", "internal class C { public int X = 2; }", ["C"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "ctor()").Status);
    }

    [Fact]
    public void InheritedMemberAndHierarchyChanges_AreDifferent()
    {
        const string bases = "public class B1 { public int F() => 1; } public class B2 { public int F() => 2; } ";
        var verdicts = Compare(bases + "public class C : B1 { }", bases + "public class C : B2 { }", ["B1", "B2", "C"]);
        Assert.Contains(verdicts, v => v.Type == "C" && v.Member == "<hierarchy>" && v.Status == MemberStatus.Different);
        Assert.Contains(verdicts, v => v.Type == "C" && v.Member.StartsWith("B1.F", StringComparison.Ordinal) && v.Reason == "SURFACE");
    }

    [Fact]
    public void BoxedScalarRuntimeType_IsCompared()
    {
        var verdicts = Compare("public static class S { public static object F() => 1; }", "public static class S { public static object F() => 1L; }", ["S"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "static F()->System.Object").Status);
    }

    [Fact]
    public void HiddenBaseMember_IsPartOfTheState()
    {
        const string b = "public class Base { public int X = 1; } ";
        var verdicts = Compare(b + "public class C : Base { public new int X = 5; public C() { base.X = 1; } }",
            b + "public class C : Base { public new int X = 5; public C() { base.X = 2; } }", ["Base", "C"]);
        Assert.Contains(verdicts, v => v.Type == "C" && v.Member == "ctor()" && v.Status == MemberStatus.Different);
    }

    [Fact]
    public void StringBuilderContent_IsCompared()
    {
        var verdicts = Compare("public static class S { public static System.Text.StringBuilder F() => new(\"a\"); }",
            "public static class S { public static System.Text.StringBuilder F() => new(\"b\"); }", ["S"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "static F()->System.Text.StringBuilder").Status);
    }

    [Fact]
    public void AsyncResult_IsAwaitedBeforeComparison()
    {
        const string body = "await System.Threading.Tasks.Task.Delay(1500); return ";
        var verdicts = Compare("public static class S { public static async System.Threading.Tasks.Task<int> F() { " + body + "1; } }",
            "public static class S { public static async System.Threading.Tasks.Task<int> F() { " + body + "2; } }", ["S"]);
        var f = Single(verdicts, "static F()->System.Threading.Tasks.Task<System.Int32>");
        Assert.Equal(MemberStatus.Different, f.Status);
        Assert.Contains("RanToCompletion", f.Witnesses[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowingConstructor_StillComparesStaticState()
    {
        var verdicts = Compare("public class C { public static int N; public C() { N = 1; throw new System.InvalidOperationException(); } }",
            "public class C { public static int N; public C() { N = 2; throw new System.InvalidOperationException(); } }", ["C"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "ctor()").Status);
    }

    /// <summary>The receiver comes from a tuple that actually constructs, not the first generated one.</summary>
    [Fact]
    public void Receiver_IsBuiltFromATupleThatConstructs()
    {
        const string ctor = "public C(int n) { if (n <= 0) throw new System.ArgumentOutOfRangeException(); }";
        var verdicts = Compare("public class C { " + ctor + " public int F() => 1; }", "public class C { " + ctor + " public int F() => 2; }", ["C"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "F()->System.Int32").Status);
    }

    [Fact]
    public void UnsignedEnumAboveLongMaxValue_IsDrivenAndRendered()
    {
        const string e = "public enum E : ulong { Small = 1, Huge = 18446744073709551615 } ";
        var verdicts = Compare(e + "public static class S { public static ulong F(E v) => (ulong)v; }",
            e + "public static class S { public static ulong F(E v) => (ulong)v / 2; }", ["E", "S"]);
        var f = Single(verdicts, "static F(E)->System.UInt64");
        Assert.Equal(MemberStatus.Different, f.Status);
        Assert.Equal(MemberStatus.Agree, Single(verdicts, "<enum-values>").Status);
    }

    [Fact]
    public void LongRendering_IsComparedInFull()
    {
        // The two observations share a prefix far longer than the stored display length; only the hash can tell them apart.
        var verdicts = Compare("public static class S { public static string F() => new string('a', 40000) + \"x\"; }",
            "public static class S { public static string F() => new string('a', 40000) + \"y\"; }", ["S"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "static F()->System.String").Status);
    }

    [Fact]
    public void TimeoutClassification_FollowsTheCleanStateReplays()
    {
        var prefix = new MemberVerdict { Type = "T", Member = "F()", Kind = "static", Status = MemberStatus.Timeout, LimitedAt = 3, Inputs = 3 };
        var options = new OracleOptions { MemberTimeout = TimeSpan.FromSeconds(4) };
        ProbeResult Hang() => new(ProbeOutcome.Hung, 4, null);
        ProbeResult Crash() => new(ProbeOutcome.Crashed, 0, null);
        ProbeResult Quick(string o = "r") => new(ProbeOutcome.Finished, 0.1, o);
        ProbeResult Lost() => new(ProbeOutcome.Infrastructure, 0, "replay timed out");
        MemberVerdict Classify(ProbeResult[] o, ProbeResult[] c) => MemberDifferentialOracle.ClassifyTimeout(prefix, o, c, options);
        Assert.Equal("HANG", Classify([Quick(), Quick()], [Hang(), Hang()]).Reason);
        Assert.Equal("CRASH", Classify([Quick(), Quick()], [Crash(), Crash()]).Reason);
        Assert.Equal(MemberStatus.Timeout, Classify([Hang(), Hang()], [Hang(), Hang()]).Status);
        Assert.Equal(MemberStatus.Nondeterministic, Classify([Quick(), Hang()], [Hang(), Hang()]).Status);
        // Self-consistency is checked before HANG: an original whose replays disagree is never a HANG difference.
        Assert.Equal(MemberStatus.Nondeterministic, Classify([Quick("a"), Quick("b")], [Hang(), Hang()]).Status);
        Assert.Equal(MemberStatus.Different, Classify([Quick("a"), Quick("a")], [Quick("b"), Quick("b")]).Status);
        Assert.Equal(MemberStatus.Timeout, Classify([Quick(), Quick()], [Quick(), Quick()]).Status);
        Assert.Equal(MemberStatus.Inconclusive, Classify([Quick(), Quick()], [Lost(), Quick()]).Status);
    }

    /// <summary>A prefix that already differs still folds the replay outcome into its fingerprint.</summary>
    [Fact]
    public void TimeoutClassification_ChangesTheFingerprintOfADifferingPrefix()
    {
        var prefix = new MemberVerdict { Type = "T", Member = "F()", Kind = "static", Status = MemberStatus.Different, LimitedAt = 3, Fingerprint = "p", Witnesses = ["w"] };
        var options = new OracleOptions { MemberTimeout = TimeSpan.FromSeconds(4) };
        var shared = MemberDifferentialOracle.ClassifyTimeout(prefix, [new(ProbeOutcome.Hung, 4, null), new(ProbeOutcome.Hung, 4, null)],
            [new(ProbeOutcome.Hung, 4, null), new(ProbeOutcome.Hung, 4, null)], options);
        var asymmetric = MemberDifferentialOracle.ClassifyTimeout(prefix, [new(ProbeOutcome.Finished, 0.1, "r"), new(ProbeOutcome.Finished, 0.1, "r")],
            [new(ProbeOutcome.Hung, 4, null), new(ProbeOutcome.Hung, 4, null)], options);
        Assert.Equal(MemberStatus.Different, shared.Status);
        Assert.NotEqual(shared.Fingerprint, asymmetric.Fingerprint);
    }

    /// <summary>A converted build that hangs only on a later call (state from earlier calls) is replayed with that sequence.</summary>
    [Fact]
    public void SequenceDependentHang_IsReplayedWithItsPrefix()
    {
        var verdicts = Compare(
            "public static class S { static int calls; public static int F(bool b) { calls++; return 1; } }",
            "public static class S { static int calls; public static int F(bool b) { if (++calls == 2) System.Threading.Thread.Sleep(4000); return 1; } }",
            ["S"], timeoutSeconds: 1);
        var f = Single(verdicts, "static F(System.Boolean)->System.Int32");
        Assert.Equal(MemberStatus.Different, f.Status);
        Assert.Equal("HANG", f.Reason);
    }

    // ---- Codex round 2 false negatives --------------------------------------------------------

    [Fact]
    public void DefaultInterfaceImplementation_IsDrivenThroughTheImplementingType()
    {
        var verdicts = Compare("public interface I { int F() => 1; } public class C : I { }", "public interface I { int F() => 2; } public class C : I { }", ["I", "C"]);
        Assert.Contains(verdicts, v => v.Type == "C" && v.Member.StartsWith("via I.F", StringComparison.Ordinal) && v.Status == MemberStatus.Different);
        Assert.Contains(verdicts, v => v.Type == "I" && v.Kind == "interface-member");
    }

    [Fact]
    public void ExplicitInterfaceImplementation_IsDriven()
    {
        var verdicts = Compare("public interface I { int F(); } public class C : I { int I.F() => 1; }", "public interface I { int F(); } public class C : I { int I.F() => 2; }", ["I", "C"]);
        Assert.Contains(verdicts, v => v.Type == "C" && v.Member.StartsWith("via I.F", StringComparison.Ordinal) && v.Status == MemberStatus.Different);
    }

    [Fact]
    public void HiddenSameSignatureBaseProperty_IsPartOfTheState()
    {
        const string b = "public class Base { public int X { get; set; } = 1; } ";
        var verdicts = Compare(b + "public class C : Base { public new int X { get; set; } = 5; public C() { base.X = 1; } }",
            b + "public class C : Base { public new int X { get; set; } = 5; public C() { base.X = 2; } }", ["Base", "C"]);
        Assert.Contains(verdicts, v => v.Type == "C" && v.Member == "ctor()" && v.Status == MemberStatus.Different);
    }

    [Fact]
    public void HiddenSameSignatureBaseMethod_IsDriven()
    {
        const string c = "public class C : Base { public new int F() => 5; }";
        var verdicts = Compare("public class Base { public int F() => 1; } " + c, "public class Base { public int F() => 2; } " + c, ["Base", "C"]);
        Assert.Contains(verdicts, v => v.Type == "C" && v.Member.StartsWith("Base.F", StringComparison.Ordinal) && v.Status == MemberStatus.Different);
    }

    [Fact]
    public void ReturnedDelegate_IsCalled()
    {
        var verdicts = Compare("public static class S { public static System.Func<int, int> F() => x => x + 1; }",
            "public static class S { public static System.Func<int, int> F() => x => x + 2; }", ["S"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "static F()->System.Func<System.Int32,System.Int32>").Status);
    }

    [Fact]
    public void DelegateItCannotCall_IsNeverAgreement()
    {
        const string source = "public static class S { public static System.Func<System.IO.Stream, int> F() => s => 1; }";
        var f = Single(Compare(source, source, ["S"]), "static F()->System.Func<System.IO.Stream,System.Int32>");
        Assert.NotEqual(MemberStatus.Agree, f.Status);
    }

    [Fact]
    public void TokenElementsOfCollectionArguments_AreMaterialized()
    {
        var verdicts = Compare("public static class S { public static int F(System.Exception[] e) => e?.Length ?? 0; }",
            "public static class S { public static int F(System.Exception[] e) => e == null ? 0 : e.Length > 0 && e[0] != null ? 99 : e.Length; }", ["S"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "static F(System.Exception[])->System.Int32").Status);
    }

    [Fact]
    public void MutationOfAnotherTypesStatics_IsCompared()
    {
        const string a = "public static class A { public static int X; } ";
        var verdicts = Compare(a + "public static class Z { public static void F() { A.X = 1; } }", a + "public static class Z { public static void F() { A.X = 2; } }", ["A", "Z"]);
        Assert.Equal(MemberStatus.Different, Single(verdicts, "static F()->System.Void").Status);
    }

    [Fact]
    public void AllTypesScope_IncludesTypesNotListed()
    {
        var verdicts = Compare("public class Listed { } public class Unlisted { public int X = 1; }", "public class Listed { } public class Unlisted { public int X = 2; }", ["*"]);
        Assert.Contains(verdicts, v => v.Type == "Unlisted" && v.Member == "ctor()" && v.Status == MemberStatus.Different);
    }

    // ---- Inputs ---------------------------------------------------------------------------------

    [Fact]
    public void InputGenerator_IsAForkUnderItsOwnId()
    {
        Assert.Equal("1587-member-inputs-v1", MemberOracleInputs.GeneratorId);
    }

    [Fact]
    public void Renderer_IsCultureInvariant()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.5d", StateRenderer.Render(1.5, 0));
            Assert.Equal("2.25m", StateRenderer.Render(2.25m, 0));
            Assert.Equal("2024-02-29T13:45:30.1230000Z/Utc", StateRenderer.Render(new DateTime(2024, 2, 29, 13, 45, 30, 123, DateTimeKind.Utc), 0));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MemberOracleCollection
{
    public const string Name = "member-oracle (process-wide console and working directory)";
}
