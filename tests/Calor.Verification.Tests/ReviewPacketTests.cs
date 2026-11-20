using Calor.Compiler.Reporting;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Ids;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// D-W3.3 (wedge plan v0.11): review packet v1 — the per-change report that
/// leads with the unproven remainder. Pins: seven-status collection with
/// assumptions/vacuity, waiver disclosure on the first line, per-module
/// interop fraction, caller impact from the in-memory call graph, and the
/// #782 refinement honesty note present on every packet.
/// </summary>
public class ReviewPacketTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "calor-review-identities-" + Guid.NewGuid().ToString("N"));

    public ReviewPacketTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string MixedModule = @"
module Pricing
  func Square public (int:x | ((# >= 0) && (# <= 46340))) → int

    !
    => result >= 0
    return x * x

  func BrokenCap public (int:amount, int:cap) → int

    !

    => result <= cap
    return amount

  func Caller public (int:y) → int
    !

    return Square(y)
";

    private ReviewPacketBuilder.Result Build(
        string source,
        ReviewPacketBuilder.Options? options = null)
    {
        var file = Path.Combine(_root, "pricing.calr");
        File.WriteAllText(file, source);
        new DeclarationIdentityStore(_root).Sync([file]);
        return ReviewPacketBuilder.Build(
            [new ReviewPacketBuilder.InputFile(file, source)],
            options ?? new ReviewPacketBuilder.Options());
    }

    private string Id(string name) =>
        new DeclarationIdentityStore(_root).Sync().Declarations.Single(d => d.Name == name).Id;

    [SkippableFact]
    public void Packet_LeadsWithUnprovenRemainder_RefutedFirst()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = Build(MixedModule);
        var packet = result.Packet;

        Assert.False(result.HasCompileErrors);
        // #1618: Square's two preconditions are one conjoined refinement on x, so the module
        // states three contracts (that refinement and the two postconditions).
        Assert.True(packet.Summary.TotalContracts >= 3);
        Assert.True(packet.Summary.UnprovenRemainder >= 1);

        // BrokenCap's §S is refutable (amount unconstrained) — it must be in
        // the remainder, and refuted entries sort before everything else.
        var refuted = packet.UnprovenRemainder.Where(c => c.Status == "refuted").ToList();
        Assert.Contains(refuted, c => c.FunctionName == "BrokenCap");
        if (refuted.Count > 0)
        {
            Assert.Equal("refuted", packet.UnprovenRemainder[0].Status);
            Assert.NotNull(packet.UnprovenRemainder.First(c => c.FunctionName == "BrokenCap").Counterexample);
        }

        // The remainder never contains a clean non-vacuous proven contract.
        Assert.DoesNotContain(packet.UnprovenRemainder, c => c.Status == "proven" && !c.Vacuous);
    }

    [SkippableFact]
    public void Packet_ContractText_SlicedFromSource()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = Build(MixedModule);
        var all = result.Packet.UnprovenRemainder.Concat(result.Packet.Proven).ToList();
        var brokenCap = all.First(c => c.FunctionName == "BrokenCap" && c.ContractKind == "postcondition");
        Assert.NotNull(brokenCap.Text);
        Assert.Contains("result", brokenCap.Text);
    }

    [SkippableFact]
    public void Markdown_LeadsWithWaiver_WhenPermissive()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = Build(MixedModule, new ReviewPacketBuilder.Options(PermissiveEffects: true));
        var markdown = ReviewPacketBuilder.RenderMarkdown(result.Packet);

        var firstLine = markdown.Split('\n')[0];
        Assert.Contains("WAIVER", firstLine);
        Assert.Contains("--permissive-effects", firstLine);
        Assert.Contains("VOID", firstLine);
    }

    [SkippableFact]
    public void Markdown_LeadsWithWaiver_WhenContractChecksOff()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = Build(MixedModule, new ReviewPacketBuilder.Options(ContractChecksOff: true));
        var markdown = ReviewPacketBuilder.RenderMarkdown(result.Packet);

        var firstLine = markdown.Split('\n')[0];
        Assert.Contains("WAIVER", firstLine);
        Assert.Contains("contract", firstLine, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void Markdown_UnprovenRemainderSection_PrecedesProvenSection()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = Build(MixedModule);
        var markdown = ReviewPacketBuilder.RenderMarkdown(result.Packet);

        var remainderIndex = markdown.IndexOf("## Unproven remainder", StringComparison.Ordinal);
        var provenIndex = markdown.IndexOf("## Proven", StringComparison.Ordinal);
        Assert.True(remainderIndex >= 0);
        if (provenIndex >= 0)
            Assert.True(remainderIndex < provenIndex, "unproven remainder must lead");
    }

    [Fact]
    public void Packet_AlwaysCarriesRefinementHonestyNote()
    {
        // No Z3 needed: the honesty note is unconditional.
        var result = Build("module Empty\n  func Id public (int:x) → int\n    !\n    return x\n");
        Assert.Contains(ReviewPacketBuilder.RefinementHonestyNote, result.Packet.HonestyNotes);

        var markdown = ReviewPacketBuilder.RenderMarkdown(result.Packet);
        Assert.Contains("runtime-enforced", markdown);
        Assert.DoesNotContain("NOT runtime-enforced", markdown);
        Assert.Contains("#782", markdown);
    }

    [Fact]
    public void Packet_CallerImpact_ListsDirectCallers()
    {
        Build(MixedModule);
        var changed = Id("Square");
        var caller = Id("Caller");
        var result = Build(MixedModule, new ReviewPacketBuilder.Options(
            ChangedDeclarations: [changed]));

        var impact = Assert.Single(result.Packet.CallerImpact);
        Assert.Equal(changed, impact.ChangedId);
        Assert.Contains(impact.Callers, c => c.Id == caller);
    }

    [Fact]
    public void Packet_ChangedLineRanges_MapToDeclarations()
    {
        // Lines covering BrokenCap only (#1618: two lines shorter since Square's preconditions
        // moved into its parameter list).
        var ranges = new Dictionary<string, IReadOnlyList<(int, int)>>(StringComparer.Ordinal)
        {
            [Path.Combine(_root, "pricing.calr")] = [(9, 14)]
        };
        var result = Build(MixedModule, new ReviewPacketBuilder.Options(
            ChangedLineRanges: ranges, BaselineRef: "HEAD"));

        Assert.Contains(Id("BrokenCap"), result.Packet.ChangedDeclarations);
        Assert.DoesNotContain(Id("Caller"), result.Packet.ChangedDeclarations);
    }

    [Fact]
    public void Packet_CallerImpact_FindsCrossFileCallers()
    {
        // M2 pin (PR #841 review): a caller in one file of a declaration
        // changed in another file of the SAME invocation must be found.
        const string lib = @"
module Lib
  func Square public (int:x) → int
    !
    return x * x
";
        const string app = @"
module App
  func UseSquare public (int:y) → int
    !
    return Square(y)
";
        var libFile = Path.Combine(_root, "lib.calr");
        var appFile = Path.Combine(_root, "app.calr");
        File.WriteAllText(libFile, lib);
        File.WriteAllText(appFile, app);
        new DeclarationIdentityStore(_root).Sync([libFile, appFile]);
        var changed = Id("Square");
        var caller = Id("UseSquare");
        var result = ReviewPacketBuilder.Build(
            [
                new ReviewPacketBuilder.InputFile(libFile, lib),
                new ReviewPacketBuilder.InputFile(appFile, app)
            ],
            new ReviewPacketBuilder.Options(ChangedDeclarations: [changed]));

        var impact = Assert.Single(result.Packet.CallerImpact);
        Assert.Equal(changed, impact.ChangedId);
        Assert.Contains(impact.Callers, c => c.Id == caller);
        Assert.Empty(result.UnmatchedChangedDeclarations);
    }

    [Fact]
    public void Packet_UnknownChangedSelector_ReportedNotSilent()
    {
        // m1 pin: a typo'd --changed selector is surfaced, not swallowed.
        var result = Build(MixedModule, new ReviewPacketBuilder.Options(
            ChangedDeclarations: ["zz999"]));
        Assert.Contains("zz999", result.UnmatchedChangedDeclarations);
        Assert.Empty(result.Packet.CallerImpact);
    }

    [SkippableFact]
    public void Summary_Proven_ExcludesVacuous()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        // m2 pin: a vacuously-proven contract (unsatisfiable §Q set) must not
        // let summary.proven read clean — proven counts non-vacuous only.
        const string vacuous = @"
module Vac
  func Weird public (int:x | ((# >= 1) && (# <= 0))) → int
    !
    => result == 5
    return 7
";
        var result = Build(vacuous);
        var summary = result.Packet.Summary;

        var vacuousRecords = result.Packet.UnprovenRemainder
            .Where(c => c.Status == "proven" && c.Vacuous).ToList();
        Assert.NotEmpty(vacuousRecords);
        Assert.Equal(vacuousRecords.Count, summary.ProvenVacuous);

        // No clean-proven contract exists in this module, and vacuous ones
        // never count as proven.
        Assert.DoesNotContain(result.Packet.Proven, c => c.Vacuous);
        Assert.True(summary.Proven + summary.ProvenVacuous
            <= summary.TotalContracts);
        Assert.DoesNotContain(result.Packet.UnprovenRemainder,
            c => c.Status == "proven" && !c.Vacuous);
    }

    [Fact]
    public void Packet_CompileError_SurfacesDiagnosticsAndFlags()
    {
        // A parse-level error: unterminated call argument list.
        var result = ReviewPacketBuilder.Build(
            [new ReviewPacketBuilder.InputFile(Path.Combine(_root, "broken.calr"),
                "module Broken\n  func Bad public (int:x) → int\n    !\n    return Add(x\n")],
            new ReviewPacketBuilder.Options());
        Assert.True(result.HasCompileErrors);
        Assert.NotEmpty(result.CompileDiagnostics);
    }
}
