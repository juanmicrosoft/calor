using Calor.Compiler.Ids;
using Calor.Evaluation.Core;
using Calor.Evaluation.Metrics;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// The ID-based metrics read declaration IDs through the compiler's identity store
/// (<c>.calor/ids.json</c>), not from source text.
/// </summary>
public class CalorIdentitiesTests
{
    private const string Source = """
        module Shop Shop block
          func Total public (int:price, int:count | (# >= 0)) → int
            !
            return price * count
          func Twice public (int:x) → int
            !
            return x + x
        """;

    [Fact]
    public void Read_AssignsAnIdToEveryTrackedDeclaration()
    {
        var ids = CalorIdentities.Read(Source);

        Assert.Single(ids, d => d.Kind == IdKind.Module && d.Name == "Shop");
        Assert.Equal(["Total", "Twice"], ids.Where(d => d.Kind == IdKind.Function).Select(d => d.Name).Order());
        Assert.Equal(ids.Count, ids.Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public void Track_KeepsAFunctionIdAcrossARename()
    {
        var after = Source.Replace("func Twice", "func Double");

        var (before, reconciled) = CalorIdentities.Track(Source, after);

        var twice = Assert.Single(before, d => d.Name == "Twice");
        var renamed = Assert.Single(reconciled, d => d.Name == "Double");
        Assert.Equal(twice.Id, renamed.Id);
        Assert.Equal(before.Count, CalorIdentities.Preserved(before, reconciled));
    }

    [Fact]
    public void Track_DropsTheIdOfADeletedFunction()
    {
        var after = Source[..Source.IndexOf("  func Twice", StringComparison.Ordinal)];

        var (before, reconciled) = CalorIdentities.Track(Source, after);

        Assert.Equal(before.Count - 1, CalorIdentities.Preserved(before, reconciled));
        Assert.DoesNotContain(reconciled, d => d.Name == "Twice");
    }

    [Fact]
    public void Read_ReusesTheIdsAnExistingSidecarHolds()
    {
        var fresh = CalorIdentities.Read(Source, file: "src/shop.calr");
        var sidecar = System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = DeclarationIdentityStore.SchemaVersion,
            Files = new[] { "src/shop.calr" },
            Declarations = fresh.Select(d => new
            {
                d.Id, File = "src/shop.calr", d.Container, Kind = (int)d.Kind, d.Name,
                Signature = CalorIdentityTestSignatures.Of(Source, "src/shop.calr", d)
            })
        });

        var reread = CalorIdentities.Read(Source + "\n", sidecar, "src/shop.calr");
        var withoutSidecar = CalorIdentities.Read(Source + "\n", file: "src/shop.calr");

        Assert.Equal(fresh.Select(d => d.Id).Order(), reread.Select(d => d.Id).Order());
        // Control: without the sidecar the store allocates new IDs, so the match above is reuse.
        Assert.Empty(fresh.Select(d => d.Id).Intersect(withoutSidecar.Select(d => d.Id)));
    }

    [Fact]
    public void Read_ReturnsNoIdsForSourceThatDoesNotParse()
    {
        Assert.Empty(CalorIdentities.Read("module Broken Broken block\n  func F (int:x → int\n    !\n    return x\n"));
    }

    [Fact]
    public void Of_UsesTheContextSidecar()
    {
        var context = new EvaluationContext { CalorSource = Source, CSharpSource = "", FileName = "shop" };

        Assert.Equal(CalorIdentities.Read(Source).Count, CalorIdentities.Of(context).Count);
    }
}

internal static class CalorIdentityTestSignatures
{
    public static string Of(string source, string file, CalorDeclarationId declaration) =>
        DeclarationIdentityStore.Describe(file, source)
            .Single(d => d.Kind == declaration.Kind && d.Name == declaration.Name && d.Container == declaration.Container)
            .Signature;
}
