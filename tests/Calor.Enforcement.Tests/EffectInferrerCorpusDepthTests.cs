using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// #1104 (v0.16 W3(c)), review round 1 MAJOR M3 — the measurement the frame
/// cap rests on, committed rather than left in a scratch tool.
/// </summary>
/// <remarks>
/// <para>The cap's whole justification is "ordinary code sits an order of
/// magnitude below it". That was a one-off sweep on an uncommitted tool, and a
/// cap stop is SILENT: if converted corpus code ever climbed past the cap,
/// resolution would quietly decline and nothing would go red. This test is the
/// instrument that makes that visible — it converts every corpus module the way
/// the Calor0425 ledger does, enforces it WITHOUT binding (the crash path), and
/// pins two things: the deepest frame any module needs, and that no module
/// reaches the cap.</para>
///
/// <para><b>What it does NOT pin:</b> diagnostics, effect verdicts, or module
/// counts — those belong to the ledgers. Only the guard's own two numbers.</para>
///
/// <para><b>Skips</b> when the corpus submodules are not initialized, the same
/// way <c>BinderIncompleteRatchetTests</c> does, so a bare clone stays green.
/// Run <c>git submodule update --init</c> to unskip.</para>
///
/// <para><b>Discriminating:</b> raise the observed depth past
/// <see cref="MaxExpectedFrames"/> (a converter change that nests deeper, or a
/// resolution change that asks more) and this goes red BEFORE a user meets a
/// silent decline; lower the cap under the observed depth and
/// <c>capStops</c> goes non-zero, red on the same test.</para>
/// </remarks>
public class EffectInferrerCorpusDepthTests
{
    /// <summary>
    /// Measured over all 364 corpus modules at the pinned submodule SHAs:
    /// deepest is 13 frames. No single module owns that number — several reach
    /// it, and <c>deepestFile</c> below names whichever RAISED the maximum
    /// first in the Ordinal file order, so the name is a debugging aid and not
    /// a fact about the corpus. Held a
    /// little above the measurement so an unrelated converter tweak does not
    /// fail the build for one extra frame, and far below
    /// <see cref="EffectEnforcementPass.AstResolutionBound.DefaultDepthCap"/>
    /// (224) so the "order of magnitude" claim stays testable.
    /// </summary>
    private const int MaxExpectedFrames = 24;

    [SkippableFact]
    public void CorpusEnforcedWithoutBinding_StaysAnOrderOfMagnitudeBelowTheCap()
    {
        var roots = CorpusRoots();
        Skip.IfNot(roots.All(Directory.Exists), "corpus submodules not initialized");

        var bound = new EffectEnforcementPass.AstResolutionBound();
        var parseOptions = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview,
            Microsoft.CodeAnalysis.DocumentationMode.Parse,
            Microsoft.CodeAnalysis.SourceCodeKind.Regular,
            preprocessorSymbols: Array.Empty<string>());

        var modules = 0;
        var deepestFile = "";
        var deepestSoFar = 0;

        foreach (var file in roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            Compiler.Migration.ConversionResult conversion;
            try
            {
                conversion = new Compiler.Migration.CSharpToCalorConverter(
                    new Compiler.Migration.ConversionOptions
                    {
                        Fidelity = Compiler.Migration.ConversionFidelity.Lossy,
                        PreprocessorMode = Compiler.Migration.PreprocessorConversionMode
                            .SelectActiveBranchLossy,
                        ParseOptions = parseOptions,
                        DefinedSymbols = Array.Empty<string>(),
                        ModuleName = "Issue1104DepthSweep",
                        GracefulFallback = true,
                        AutoGenerateIds = true
                    }).Convert(File.ReadAllText(file), Path.GetFileName(file));
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(conversion.CalorSource))
                continue;

            var parseDiagnostics = new DiagnosticBag();
            var module = new Parser(
                new Lexer(conversion.CalorSource.Replace("\r\n", "\n"), parseDiagnostics)
                    .TokenizeAllForParser(),
                parseDiagnostics).Parse();
            if (parseDiagnostics.HasErrors)
                continue;

            // The point of the sweep: the pass runs with NO binder in front of
            // it, which is what #1104 crashed on and what the MCP edit_preview
            // path and the in-process ledgers actually do.
            new EffectEnforcementPass(new DiagnosticBag()) { AstResolution = bound }
                .Enforce(module);
            modules++;

            if (bound.MaxObservedDepth > deepestSoFar)
            {
                deepestSoFar = bound.MaxObservedDepth;
                deepestFile = file;
            }
        }

        Assert.True(modules >= 250,
            $"the sweep must cover the corpus it claims to; enforced {modules} modules");

        Assert.True(
            bound.MaxObservedDepth <= MaxExpectedFrames,
            $"AST-resolution depth over the corpus rose to {bound.MaxObservedDepth} frames "
            + $"(deepest: {deepestFile}), above the registered {MaxExpectedFrames}. The frame cap "
            + "is justified by ordinary code sitting an order of magnitude below it — re-derive "
            + "the cap (see AstResolutionBound.DefaultDepthCap) before raising this number.");

        Assert.Equal(0, bound.DepthCapStops);

        // The corpus's two known cycles (Serilog Logger.cs and BatchingSink.cs)
        // are still found — a sweep that stopped finding them would be
        // measuring nothing. The count is the pass's, not a ledger's.
        Assert.True(bound.CycleStops > 0,
            "the corpus's two cyclic modules must still be reached by this sweep");
    }

    private static string[] CorpusRoots()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "bench", "corpus", "serilog", "src"),
            Path.Combine(root, "bench", "corpus", "MediatR", "src"),
            Path.Combine(root, "bench", "corpus", "FluentValidation", "src"),
        ];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !Directory.Exists(Path.Combine(directory.FullName, "src", "Calor.Compiler")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("repository root not found");
    }
}
