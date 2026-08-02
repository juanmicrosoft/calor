using Calor.Compiler.Reporting;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// D-W3.3 (wedge plan v0.11): review packet v1 — the per-change report that
/// leads with the unproven remainder. Pins: seven-status collection with
/// assumptions/vacuity, waiver disclosure on the first line, per-module
/// interop fraction, caller impact from the in-memory call graph, and the
/// #782 refinement honesty note present on every packet.
/// </summary>
public class ReviewPacketTests
{
    private const string MixedModule = @"
§M{m001:Pricing}
  §F{f001:Square:pub}
      §I{i32:x}
      §O{i32}
      §Q (>= x 0)
      §Q (<= x 46340)
      §S (>= result 0)
      §R (* x x)

  §F{f002:BrokenCap:pub}
      §I{i32:amount}
      §I{i32:cap}
      §O{i32}
      §S (<= result cap)
      §R amount

  §F{f003:Caller:pub}
      §I{i32:y}
      §O{i32}
      §R §C{Square} §A y §/C
";

    private static ReviewPacketBuilder.Result Build(
        string source,
        ReviewPacketBuilder.Options? options = null)
    {
        return ReviewPacketBuilder.Build(
            [new ReviewPacketBuilder.InputFile("pricing.calr", source)],
            options ?? new ReviewPacketBuilder.Options());
    }

    [SkippableFact]
    public void Packet_LeadsWithUnprovenRemainder_RefutedFirst()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var result = Build(MixedModule);
        var packet = result.Packet;

        Assert.False(result.HasCompileErrors);
        Assert.True(packet.Summary.TotalContracts >= 4);
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
        var result = Build("§M{m001:Empty}\n  §F{f001:Id:pub} (i32:x) -> i32\n    §R x\n");
        Assert.Contains(ReviewPacketBuilder.RefinementHonestyNote, result.Packet.HonestyNotes);

        var markdown = ReviewPacketBuilder.RenderMarkdown(result.Packet);
        Assert.Contains("NOT runtime-enforced", markdown);
        Assert.Contains("#782", markdown);
    }

    [Fact]
    public void Packet_ModuleDisclosure_ReportsInteropFraction()
    {
        var source = @"
§M{m001:WithInterop}
  §F{f001:Pure:pub} (i32:x) -> i32
    §R x

  §CSHARP{
    public static int Raw(int x) => x + 1;
  }§/CSHARP
";
        var result = Build(source);
        var module = Assert.Single(result.Packet.Modules);
        Assert.Equal(1, module.InteropBlocks);
        Assert.Equal(2, module.TotalMembers);
        Assert.Equal(0.5, module.InteropFraction, 3);
    }

    [Fact]
    public void Packet_CallerImpact_ListsDirectCallers()
    {
        var result = Build(MixedModule, new ReviewPacketBuilder.Options(
            ChangedDeclarations: ["f001"]));

        var impact = Assert.Single(result.Packet.CallerImpact);
        Assert.Equal("f001", impact.ChangedId);
        Assert.Contains(impact.Callers, c => c.Id == "f003");
    }

    [Fact]
    public void Packet_ChangedLineRanges_MapToDeclarations()
    {
        // Lines covering f002 (BrokenCap) only.
        var ranges = new Dictionary<string, IReadOnlyList<(int, int)>>(StringComparer.Ordinal)
        {
            [System.IO.Path.GetFullPath("pricing.calr")] = [(11, 16)]
        };
        var result = Build(MixedModule, new ReviewPacketBuilder.Options(
            ChangedLineRanges: ranges, BaselineRef: "HEAD"));

        Assert.Contains("f002", result.Packet.ChangedDeclarations);
        Assert.DoesNotContain("f003", result.Packet.ChangedDeclarations);
    }

    [Fact]
    public void Packet_CompileError_SurfacesDiagnosticsAndFlags()
    {
        // A parse-level error: unterminated call argument list.
        var result = Build("§M{m001:Broken}\n  §F{f001:Bad:pub} (i32:x) -> i32\n    §R (+ x\n");
        Assert.True(result.HasCompileErrors);
        Assert.NotEmpty(result.CompileDiagnostics);
    }
}
