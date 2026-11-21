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
/// <para><b>What it does NOT pin:</b> diagnostics or effect verdicts — those belong
/// to the ledgers. It pins the guard's own two numbers and, since 0.30, how many
/// corpus files the converter refuses (its population).</para>
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
    /// deepest is 13 frames (since 0.30 the sweep enforces the 206 that convert;
    /// see <see cref="RegisteredRefusals"/>). No single module owns that number — several reach
    /// it, and <c>deepestFile</c> below names whichever RAISED the maximum
    /// first in the Ordinal file order, so the name is a debugging aid and not
    /// a fact about the corpus. Held a
    /// little above the measurement so an unrelated converter tweak does not
    /// fail the build for one extra frame, and far below
    /// <see cref="EffectEnforcementPass.AstResolutionBound.DefaultDepthCap"/>
    /// (224) so the "order of magnitude" claim stays testable.
    /// </summary>
    private const int MaxExpectedFrames = 24;

    /// <summary>The 364 .cs files under the three pinned subjects' src/ trees.</summary>
    private const int CorpusFiles = 364;

    /// <summary>
    /// Corpus files the converter refuses under the sweep's one-file
    /// Lossy/SelectActiveBranchLossy options (2026-11-17, 0.30): 141 fail the
    /// original-source validation of the lossy scoped-import path, 4 are not fully
    /// native, 13 have an inexpressible namespace topology. 206 modules are enforced.
    /// 2026-11-20: 160 -> 158, the two FluentValidation files that
    /// <c>Calor0270CorpusVolumeTests</c> attributes in its after-i8 refresh, both formerly
    /// not fully native: TestMessages.Designer.cs (accessors may declare a row, h1
    /// 06f6f63fc) and CultureScope.cs (constructor-initializer arguments stay inline, i5
    /// 5427061f0). This pin was not moved with that ledger refresh.
    /// </summary>
    private const int RegisteredRefusals = 158;

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
        var files = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        // Files the sweep does not enforce, by reason. None is dropped silently.
        var refused = new List<string>();
        var notEnforced = new List<string>();

        foreach (var file in files)
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
            catch (Exception ex)
            {
                notEnforced.Add($"{file}: conversion threw {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (string.IsNullOrEmpty(conversion.CalorSource))
            {
                refused.Add(file);
                continue;
            }

            // Frame-aware normalization: raw C# payloads are length-prefixed, so a blind
            // CRLF -> LF rewrite truncates them (it failed 5 CRLF FluentValidation files).
            var parseDiagnostics = new DiagnosticBag();
            var module = new Parser(
                new Lexer(RawCSharpFrame.NormalizeLineEndings(conversion.CalorSource), parseDiagnostics)
                    .TokenizeAllForParser(),
                parseDiagnostics).Parse();
            if (parseDiagnostics.HasErrors)
            {
                notEnforced.Add($"{file}: converted output failed to parse: "
                    + parseDiagnostics.Errors.First().Message);
                continue;
            }

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

        // The sweep must cover the corpus it claims to. Before 0.30 every one of the
        // 364 corpus files converted, and the guard was "at least 250 modules". Since
        // 92b4d8c42 the converter refuses (returns no Calor for) files whose
        // namespace-local imports cannot pass the validated lossy path in a one-file
        // model, so the population is now "every module the converter emits": all of
        // them must be enforced, and the refusals are a pinned count, never a silent
        // shrink. Calor0270CorpusVolumeTests pins the same refusals file by file.
        Assert.True(notEnforced.Count == 0,
            "every converted corpus module must parse and be enforced:\n  "
            + string.Join("\n  ", notEnforced));
        Assert.Equal(CorpusFiles, files.Length);
        Assert.Equal(files.Length, modules + refused.Count);
        Assert.True(refused.Count == RegisteredRefusals,
            $"the converter refused {refused.Count} corpus files, registered {RegisteredRefusals}; "
            + $"enforced {modules} modules. A change here moves the sweep's population: "
            + "name its cause and update RegisteredRefusals with Calor0270's fixture.");

        Assert.True(
            bound.MaxObservedDepth <= MaxExpectedFrames,
            $"AST-resolution depth over the corpus rose to {bound.MaxObservedDepth} frames "
            + $"(deepest: {deepestFile}), above the registered {MaxExpectedFrames}. The frame cap "
            + "is justified by ordinary code sitting an order of magnitude below it — re-derive "
            + "the cap (see AstResolutionBound.DefaultDepthCap) before raising this number.");

        Assert.Equal(0, bound.DepthCapStops);

        // The corpus had two known cycles (Serilog Logger.cs and BatchingSink.cs). #943
        // (0.25 F1) keeps both members that formed them as C# (their `out var` types do not
        // resolve in a one-file conversion, D1 RO-DECL-1), so the sweep now reaches none.
        // Cycle detection itself stays covered by EffectInferrerRecursionTests; pinning 0
        // here makes a returning cycle a visible change rather than a silent one.
        Assert.Equal(0, bound.CycleStops);
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
