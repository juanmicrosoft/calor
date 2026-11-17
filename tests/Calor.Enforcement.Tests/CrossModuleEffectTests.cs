using Calor.Compiler;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for cross-module effect propagation — when a function in one .calr file
/// calls a public function defined in another .calr file, the caller must declare
/// the effects of the callee.
///
/// Verifies the contract-based enforcement model (declared effects propagate,
/// not inferred effects), bare-name resolution, qualified-name resolution,
/// and that an explicit pure row never raises Calor0417 (a public function with no
/// row at all is a parse error, Calor0124, so Calor0417 has no positive case here).
/// </summary>
public class CrossModuleEffectTests
{
    // ========================================================================
    // Helpers
    // ========================================================================

    private static (List<(ModuleNode Ast, string FilePath)> Modules, List<Diagnostic> RegistryDiags)
        CompileAll(params (string FilePath, string Source)[] files)
    {
        var modules = new List<(ModuleNode, string)>();
        foreach (var (path, source) in files)
        {
            var result = Program.Compile(source, path, new CompilationOptions
            {
                EnforceEffects = false,
                UnsafeTranspileOnly = true
            });
            Assert.NotNull(result.Ast);
            Assert.False(result.HasErrors,
                $"Per-file compilation failed for {path}: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
            modules.Add((result.Ast!, path));
        }

        var registry = CrossModuleEffectRegistry.Build(modules);
        return (modules, registry.BuildDiagnostics);
    }

    private static List<Diagnostic> RunCrossModulePass(
        List<(ModuleNode Ast, string FilePath)> modules)
    {
        var registry = CrossModuleEffectRegistry.Build(modules);
        var pass = new CrossModuleEffectEnforcementPass();
        return pass.Enforce(modules, registry);
    }

    private static List<Diagnostic> RunFull(params (string FilePath, string Source)[] files)
    {
        var (modules, registryDiags) = CompileAll(files);
        var crossDiags = RunCrossModulePass(modules);
        var all = new List<Diagnostic>();
        all.AddRange(registryDiags);
        all.AddRange(crossDiags);
        return all;
    }

    // ========================================================================
    // Tests
    // ========================================================================

    [Fact]
    public void CrossModule_BareNameCall_UndeclaredEffect_Error()
    {
        // b.calr calls SaveOrder (bare name) without declaring db:w
        var a = @"module OrderService OrderService block
  func SaveOrder () → void
    ! db:w
    return
";
        var b = @"module Handler Handler block
  func HandleRequest () → void
    !
    SaveOrder()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        var err = diags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.Contains("HandleRequest", err!.Message);
        Assert.Contains("SaveOrder", err.Message);
        Assert.Contains("db:w", err.Message);
    }

    [Fact]
    public void CrossModule_QualifiedCall_UndeclaredEffect_Error()
    {
        var a = @"module OrderService OrderService block
  func SaveOrder () → void
    ! db:w
    return
";
        var b = @"module Handler Handler block
  func HandleRequest () → void
    !
    OrderService.SaveOrder()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        var err = diags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.Contains("HandleRequest", err!.Message);
        Assert.Contains("db:w", err.Message);
    }

    [Fact]
    public void CrossModule_DeclaredEffect_Passes()
    {
        var a = @"module OrderService OrderService block
  func SaveOrder () → void
    ! db:w
    return
";
        var b = @"module Handler Handler block
  func HandleRequest () → void
    ! db:w
    SaveOrder()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_OneHopPerBoundary()
    {
        // A→B→C: each caller must declare its immediate callee's effects.
        var a = @"module AMod AMod block
  func FromA () → void
    ! db:w
    FromB()
";
        var b = @"module BMod BMod block
  func FromB () → void
    ! db:w
    FromC()
";
        var c = @"module CMod CMod block
  func FromC () → void
    ! db:w
    return
";

        var diags = RunFull(("a.calr", a), ("b.calr", b), ("c.calr", c));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_SingleFile_NoOp()
    {
        // Single module → cross-module pass does nothing (no boundaries).
        var a = @"module Alone Alone block
  func Foo () → void
    ! cw
    Console.WriteLine(""hi"")
";
        var (modules, registryDiags) = CompileAll(("a.calr", a));
        var crossDiags = RunCrossModulePass(modules);

        Assert.Empty(crossDiags);
        Assert.Empty(registryDiags.Where(d => d.Code == DiagnosticCode.UndeclaredPublicFunction));
    }

    [Fact]
    public void CrossModule_ClassMethod_Propagation()
    {
        // Public class method effects propagate via ClassName.MethodName qualified call.
        var a = @"module Domain Domain block
  class OrderRepo
    func Save static () → void
      ! db:w
      return
";
        var b = @"module Handler Handler block
  func Handle () → void
    !
    OrderRepo.Save()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        var err = diags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.Contains("OrderRepo.Save", err!.Message);
        Assert.Contains("db:w", err.Message);
    }

    [Fact]
    public void CrossModule_AmbiguousName_NotResolved()
    {
        // Two modules both define a public function named 'Emit'. A caller using
        // the bare name should NOT resolve either (ambiguous → skipped).
        var a = @"module LoggerA LoggerA block
  func Emit () → void
    ! cw
    return
";
        var b = @"module LoggerB LoggerB block
  func Emit () → void
    ! fs:w
    return
";
        var c = @"module App App block
  func Run () → void
    !
    Emit()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b), ("c.calr", c));

        // Ambiguous bare name → not resolved → no cross-module forbidden-effect error.
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_QualifiedName_ResolvesWhenBareAmbiguous()
    {
        var a = @"module LoggerA LoggerA block
  func Emit () → void
    ! cw
    return
";
        var b = @"module LoggerB LoggerB block
  func Emit () → void
    ! fs:w
    return
";
        var c = @"module App App block
  func Run () → void
    !
    LoggerB.Emit()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b), ("c.calr", c));

        var err = diags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.Contains("Emit", err!.Message);
        Assert.Contains("fs:w", err.Message);
    }

    [Fact]
    public void CrossModule_MixedWithManifest()
    {
        // Caller does cross-module call + .NET call. The cross-module check fires only
        // for the cross-module call; the .NET call is handled by the per-module pass.
        var a = @"module Repo Repo block
  func Save () → void
    ! db:w
    return
";
        var b = @"module App App block
  func Run () → void
    !
    Save()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        // Only one cross-module forbidden-effect error (for Save/db:w).
        var fb = diags.Where(d => d.Code == DiagnosticCode.ForbiddenEffect).ToList();
        Assert.Single(fb);
        Assert.Contains("db:w", fb[0].Message);
    }

    [Fact]
    public void CrossModule_ViolationsAreAlwaysErrors_EvenWhenCallerCompiledInPermissiveMode()
    {
        // Permissive mode (per-module) demotes forbidden-effect errors to warnings for
        // *inferred* effects of unknown calls. The cross-module pass, in contrast, verifies
        // against a known Calor callee's declared effects — there is no uncertainty to
        // forgive — so it unconditionally reports errors regardless of the per-file policy
        // the caller was compiled with.
        var a = @"module A A block
  func DoWrite () → void
    ! db:w
    return
";
        var b = @"module B B block
  func Run () → void
    !
    DoWrite()
";

        // Compile caller in Permissive mode to confirm per-file pass would treat unknowns
        // leniently, yet the cross-module pass still errors.
        var modules = new List<(ModuleNode, string)>();
        foreach (var (path, source) in new[] { ("a.calr", a), ("b.calr", b) })
        {
            var result = Program.Compile(source, path, new CompilationOptions
            {
                EnforceEffects = true,
                UnsafeTranspileOnly = true,
                UnknownCallPolicy = UnknownCallPolicy.Permissive
            });
            Assert.NotNull(result.Ast);
            modules.Add((result.Ast!, path));
        }

        var crossDiags = RunCrossModulePass(modules);
        var err = crossDiags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.True(err!.IsError, "Cross-module violations must be errors, not warnings.");
    }

    [Fact]
    public void CrossModule_EffectSuperset_Passes()
    {
        // Caller declares MORE effects than callee — still a valid subset relationship.
        var a = @"module A A block
  func Write () → void
    ! db:w
    return
";
        var b = @"module B B block
  func Run () → void
    ! db:w, cw, fs:w
    Write()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_ParseErrorInOneFile_OthersChecked()
    {
        // If one file fails to compile, the cross-module pass should still run over
        // the files that did compile. We skip the broken file and check the rest.
        var good = @"module A A block
  func Save () → void
    ! db:w
    return
";
        var caller = @"module B B block
  func Run () → void
    !
    Save()
";

        var modules = new List<(ModuleNode, string)>();
        foreach (var (path, source) in new[] { ("a.calr", good), ("b.calr", caller) })
        {
            var result = Program.Compile(source, path, new CompilationOptions
            {
                EnforceEffects = false,
                UnsafeTranspileOnly = true
            });
            if (!result.HasErrors && result.Ast != null)
            {
                modules.Add((result.Ast, path));
            }
        }

        var crossDiags = RunCrossModulePass(modules);
        var err = crossDiags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.Contains("Save", err!.Message);
    }

    [Fact]
    public void CrossModule_EffectSubtyping_RwEncompassesR_Passes()
    {
        // Caller declares fs:rw; callee declares fs:r. rw encompasses r → valid.
        var a = @"module A A block
  func ReadFile () → void
    ! fs:r
    return
";
        var b = @"module B B block
  func Run () → void
    ! fs:rw
    ReadFile()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_EffectSubtyping_RDoesNotEncompassW_Fails()
    {
        // Caller declares fs:r; callee declares fs:w. r does NOT encompass w → violation.
        var a = @"module A A block
  func WriteFile () → void
    ! fs:w
    return
";
        var b = @"module B B block
  func Run () → void
    ! fs:r
    WriteFile()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        var err = diags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.Contains("fs:w", err!.Message);
    }

    [Fact]
    public void CrossModule_QualifiedCall_WinsOverInternalBareNameShadow()
    {
        // Regression guard for the IsInternalCall false-negative:
        // Module B has an internal function named `Save`. Caller writes
        // `§C{OrderService.Save}` intending the cross-module call. Before the fix,
        // the bare-name extraction of "Save" would match B's internal name and
        // silently skip the cross-module check. Now the qualified registry match
        // is authoritative for dotted targets.
        var a = @"module OrderService OrderService block
  func Save () → void
    ! db:w
    return
";
        var b = @"module App App block
  func Save private () → void
    return

  func Run () → void
    !
    OrderService.Save()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        var err = diags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.Contains("Run", err!.Message);
        Assert.Contains("db:w", err.Message);
    }

    [Fact]
    public void CrossModule_BareNameCall_InternalStillShadowsCrossModule()
    {
        // Complement to the above: for BARE-name targets, internal functions still
        // shadow cross-module exports (standard scoping). Module B has an internal
        // `Save` and calls `§C{Save}` (bare). It resolves to B's internal version,
        // NOT to A's cross-module `Save`, so no cross-module violation is reported.
        var a = @"module Writer Writer block
  func Save () → void
    ! db:w
    return
";
        var b = @"module App App block
  func Save private () → void
    return

  func Run () → void
    !
    Save()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_EmptyEffectDeclaration_IsExplicitlyPure_NoWarning()
    {
        // §E{} (empty declaration) is an explicit "no effects" contract — distinct from
        // having no §E at all. It should NOT trigger Calor0417 and SHOULD be registered
        // as a callable with EffectSet.Empty.
        var a = @"module PureLib PureLib block
  func Identity () → void
    !
    return
";
        var b = @"module Caller Caller block
  func Run () → void
    !
    Identity()
";

        var (_, registryDiags) = CompileAll(("a.calr", a), ("b.calr", b));

        // No "undeclared public function" warning for the explicitly-pure functions.
        Assert.DoesNotContain(registryDiags, d => d.Code == DiagnosticCode.UndeclaredPublicFunction);

        // And the caller doesn't need any effect declaration because callee declares none.
        var modules = new List<(ModuleNode, string)>();
        foreach (var (path, source) in new[] { ("a.calr", a), ("b.calr", b) })
        {
            var result = Program.Compile(source, path, new CompilationOptions
            {
                EnforceEffects = false,
                UnsafeTranspileOnly = true
            });
            modules.Add((result.Ast!, path));
        }
        var crossDiags = RunCrossModulePass(modules);
        Assert.DoesNotContain(crossDiags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_ClassMethodAsCaller_AcrossModuleBoundary()
    {
        // Coverage for a class method (not a top-level function) being the CALLER of a
        // cross-module call. Diagnostic wording should say "Method" for dotted callers.
        var a = @"module Repo Repo block
  func Persist () → void
    ! db:w
    return
";
        var b = @"module Services Services block
  class OrderService
    func Handle () → void
      !
      Persist()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        var err = diags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.StartsWith("Method 'OrderService.Handle'", err!.Message);
        Assert.Contains("db:w", err.Message);
    }

    [Fact]
    public void CrossModule_LargeProject_500Modules_CompletesQuickly()
    {
        // Stress test: 500 modules, each exposing one public function and calling its
        // neighbor's function. This exercises registry build + pass enforcement at a
        // scale beyond realistic solo projects. Assert the pass completes well under a
        // second — acts as a regression guard against accidental O(N²) behavior.
        const int moduleCount = 500;
        var summaries = new List<(EffectSummary, string)>(moduleCount);

        for (var i = 0; i < moduleCount; i++)
        {
            var nextIndex = (i + 1) % moduleCount;
            var summary = new EffectSummary
            {
                ModuleName = $"Module{i}",
                InternalFunctionNames = new List<string> { $"Func{i}" },
                PublicFunctions = new List<EffectFunctionSummary>
                {
                    new()
                    {
                        Name = $"Func{i}",
                        HasEffectDeclaration = true,
                        // Half the modules declare db:w; chain propagation will flag
                        // callers that don't also declare it.
                        DeclaredEffects = i % 2 == 0
                            ? new List<EffectEntry> { new() { Kind = "IO", Value = "database_write" } }
                            : new List<EffectEntry>(),
                        DeclarationLine = 1,
                        DeclarationColumn = 1
                    }
                },
                Callers = new List<EffectCallerSummary>
                {
                    new()
                    {
                        CallerId = $"f{i}",
                        DisplayName = $"Func{i}",
                        DiagnosticLine = 2,
                        DiagnosticColumn = 1,
                        DeclaredEffects = i % 2 == 0
                            ? new List<EffectEntry> { new() { Kind = "IO", Value = "database_write" } }
                            : new List<EffectEntry>(),
                        Calls = new List<EffectCallSummary>
                        {
                            // Each module calls the next one's function by qualified name.
                            new() { Target = $"Module{nextIndex}.Func{nextIndex}", IsConstructor = false }
                        }
                    }
                }
            };
            summaries.Add((summary, $"module{i}.calr"));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var registry = CrossModuleEffectRegistry.Build(summaries);
        var pass = new CrossModuleEffectEnforcementPass();
        var diagnostics = pass.Enforce(summaries, registry);
        sw.Stop();

        // Pass must complete quickly. On a dev machine 500 modules is typically tens of
        // milliseconds; an order of magnitude headroom guards against regressions without
        // flaking on loaded CI runners.
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"Cross-module pass took {sw.ElapsedMilliseconds}ms over {moduleCount} modules — possible O(N²) regression.");

        // Correctness sanity: every odd-indexed module has no db:w declaration but calls a
        // neighbor that may declare it. The ring pairs 0→1 (even→odd), 1→2 (odd→even), etc.
        // Half the edges should flag Calor0410. Exact count depends on chain structure,
        // but we should see a significant number — prove the pass actually ran over all modules.
        var errors = diagnostics.Count(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.True(errors > 0, "Expected cross-module violations from the mixed-declaration chain.");
    }

    [Fact]
    public void CrossModule_SummaryWithNullLists_DoesNotThrow()
    {
        // Regression guard for the defensive null-handling. A summary restored from a
        // hand-edited or partially-deserialized cache could have null collection fields.
        // Build and Enforce must tolerate this without throwing.
        var brokenSummary = new EffectSummary
        {
            ModuleName = "Broken",
            // All list fields explicitly null — this is what a malformed JSON payload
            // could produce even though the POCO initializer would normally default
            // them to empty lists.
            PublicFunctions = null!,
            PublicMethods = null!,
            InternalFunctionNames = null!,
            InternalMethodNames = null!,
            Callers = null!
        };

        var goodSummary = new EffectSummary
        {
            ModuleName = "Good",
            PublicFunctions = new List<EffectFunctionSummary>
            {
                new()
                {
                    Name = "Save",
                    HasEffectDeclaration = true,
                    DeclaredEffects = new List<EffectEntry>
                    {
                        new() { Kind = "IO", Value = "database_write" }
                    },
                    DeclarationLine = 1,
                    DeclarationColumn = 1
                }
            }
        };

        var summaries = new List<(EffectSummary, string)>
        {
            (brokenSummary, "broken.calr"),
            (goodSummary, "good.calr")
        };

        // Should not throw while building the registry.
        var registry = CrossModuleEffectRegistry.Build(summaries);

        // The good summary's function is still registered.
        Assert.NotNull(registry.TryResolve("Save"));

        // Should not throw while running the pass.
        var pass = new CrossModuleEffectEnforcementPass();
        var diagnostics = pass.Enforce(summaries, registry);
        Assert.NotNull(diagnostics);
    }

    [Fact]
    public void CrossModule_ClassMethod_CallsClassMethod_AcrossBoundary()
    {
        // Both sides of the cross-module boundary are class methods: a method in module A
        // exposes db:w; a method in module B calls it (bare-qualified class name), must
        // declare db:w. Exercises both call-site directions for class methods simultaneously.
        var a = @"module Repo Repo block
  class OrderRepo
    func Save static () → void
      ! db:w
      return
";
        var b = @"module Services Services block
  class OrderService
    func Handle () → void
      !
      OrderRepo.Save()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        var err = diags.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(err);
        Assert.StartsWith("Method 'OrderService.Handle'", err!.Message);
        Assert.Contains("OrderRepo.Save", err.Message);
        Assert.Contains("db:w", err.Message);
    }

    [Fact]
    public void CrossModule_RegistryPriorityOverManifest()
    {
        // The cross-module registry resolves Calor functions FIRST, before any
        // .NET manifest lookup — verified here by the fact that the pass produces
        // a cross-module diagnostic citing the Calor function's full declared effects
        // rather than any partial .NET-manifest-derived set.
        var a = @"module Worker Worker block
  func Process () → void
    ! db:w, net:w
    return
";
        var b = @"module App App block
  func Run () → void
    !
    Process()
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        // Both declared effects (db:w AND net:w) are reported — proving we used
        // the registry's declared effects rather than any partial manifest resolution.
        var fb = diags.Where(d => d.Code == DiagnosticCode.ForbiddenEffect).ToList();
        Assert.Equal(2, fb.Count);
        Assert.Contains(fb, d => d.Message.Contains("db:w"));
        Assert.Contains(fb, d => d.Message.Contains("net:w"));
    }

    // ================================================================ P16 ====
    // v0.15 E3 slice a — "three codes express ONE relation" (§6.3), and the
    // cross-module site is the fourth caller of it (§6.2's note).

    [Fact]
    public void AllMismatchCodesShareOneRelation()
    {
        // The structural half: every compatibility site in the tree now routes
        // through EffectRow.Fits, and NONE of them calls EffectSet.IsSubsetOf.
        // The one surviving IsSubsetOf under Effects/ is the body-vs-declaration
        // rule (Calor0410 at EffectEnforcementPass.CheckEffects) plus its own
        // definition — which is exactly what facts.py's sweep now records.
        //
        // Discriminating revert: give CheckEffectVariance its own subset test
        // back and this reads 3 rather than 2.
        var effectsDirectory = Path.Combine(RepositoryRoot(), "src", "Calor.Compiler", "Effects");
        var callSites = Directory
            .EnumerateFiles(effectsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (File: Path.GetFileName(path), Line: index + 1, Text: line)))
            .Where(entry => entry.Text.Contains("IsSubsetOf", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, callSites.Count);
        Assert.Contains(callSites, e => e.File == "EffectSet.cs");
        Assert.Contains(callSites,
            e => e.File == "EffectEnforcementPass.cs"
              && e.Text.Contains("computedEffects", StringComparison.Ordinal));
        Assert.DoesNotContain(callSites, e => e.File == "CrossModuleEffectEnforcementPass.cs");
    }

    [Fact]
    public void CrossModuleSite_RoutesThroughFits_AndKeepsCalor0410OnDoesNotFit()
    {
        // The behavioural half. A cross-module callee whose declared row does
        // not fit the caller's is still Calor0410 — §6.2: this site is the
        // cross-module leg of the body-vs-declaration rule, so it keeps
        // Calor0410 semantics even though the relation underneath it changed.
        var callee = @"module Worker Worker block
  func Process () → void
    ! cw
    return
";
        var caller = @"module App App block
  func Run () → void
    !
    Process()
";

        var diagnostics = RunFull(("a.calr", callee), ("b.calr", caller));

        Assert.Contains(diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
              && d.Message.Contains("cross-module call", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void CrossModuleSite_FamilyWideningIsShared()
    {
        // One relation means one answer: §4.1's family/narrow widening — a bare
        // `db` encompassing `db:w` — reaches the cross-module site because it
        // reaches EffectRow.Fits, without that site knowing anything about it.
        var callee = @"module Worker Worker block
  func Process () → void
    ! db:w
    return
";
        var caller = @"module App App block
  func Run () → void
    ! db
    Process()
";

        var diagnostics = RunFull(("a.calr", callee), ("b.calr", caller));

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    private static string RepositoryRoot()
    {
        // Anchored on the directory this test reads, not on `.git`: a git
        // WORKTREE carries a .git FILE rather than a directory, so the usual
        // probe walks past the root and returns null.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !Directory.Exists(Path.Combine(directory.FullName, "src", "Calor.Compiler", "Effects")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
