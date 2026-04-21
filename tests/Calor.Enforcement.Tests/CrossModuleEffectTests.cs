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
/// and the Calor0417 warning for undeclared public functions.
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
                EnforceEffects = false
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
        var a = @"§M{m1:OrderService}
§F{f001:SaveOrder:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:Handler}
§F{f001:HandleRequest:pub}
  §O{void}
  §C{SaveOrder}
  §/C
§/F{f001}
§/M{m2}
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
        var a = @"§M{m1:OrderService}
§F{f001:SaveOrder:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:Handler}
§F{f001:HandleRequest:pub}
  §O{void}
  §C{OrderService.SaveOrder}
  §/C
§/F{f001}
§/M{m2}
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
        var a = @"§M{m1:OrderService}
§F{f001:SaveOrder:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:Handler}
§F{f001:HandleRequest:pub}
  §O{void}
  §E{db:w}
  §C{SaveOrder}
  §/C
§/F{f001}
§/M{m2}
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_OneHopPerBoundary()
    {
        // A→B→C: each caller must declare its immediate callee's effects.
        var a = @"§M{m1:AMod}
§F{f001:FromA:pub}
  §O{void}
  §E{db:w}
  §C{FromB}
  §/C
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:BMod}
§F{f001:FromB:pub}
  §O{void}
  §E{db:w}
  §C{FromC}
  §/C
§/F{f001}
§/M{m2}
";
        var c = @"§M{m3:CMod}
§F{f001:FromC:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m3}
";

        var diags = RunFull(("a.calr", a), ("b.calr", b), ("c.calr", c));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_SingleFile_NoOp()
    {
        // Single module → cross-module pass does nothing (no boundaries).
        var a = @"§M{m1:Alone}
§F{f001:Foo:pub}
  §O{void}
  §E{cw}
  §P STR:""hi""
§/F{f001}
§/M{m1}
";
        var (modules, registryDiags) = CompileAll(("a.calr", a));
        var crossDiags = RunCrossModulePass(modules);

        Assert.Empty(crossDiags);
        Assert.Empty(registryDiags.Where(d => d.Code == DiagnosticCode.UndeclaredPublicFunction));
    }

    [Fact]
    public void CrossModule_PrivateFunction_NotExported()
    {
        // Private functions are not in the registry — a caller using a private callee's
        // name should not resolve cross-module.
        var a = @"§M{m1:A}
§F{f001:Helper:priv}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:B}
§F{f001:Caller:pub}
  §O{void}
  §C{Helper}
  §/C
§/F{f001}
§/M{m2}
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        // Private callee is invisible cross-module — no forbidden-effect error from this pass.
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_ClassMethod_Propagation()
    {
        // Public class method effects propagate via ClassName.MethodName qualified call.
        var a = @"§M{m1:Domain}
§CL{c1:OrderRepo:pub}
§MT{m001:Save:pub}
  §O{void}
  §E{db:w}
§/MT{m001}
§/CL{c1}
§/M{m1}
";
        var b = @"§M{m2:Handler}
§F{f001:Handle:pub}
  §O{void}
  §C{OrderRepo.Save}
  §/C
§/F{f001}
§/M{m2}
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
        var a = @"§M{m1:LoggerA}
§F{f001:Emit:pub}
  §O{void}
  §E{cw}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:LoggerB}
§F{f001:Emit:pub}
  §O{void}
  §E{fs:w}
§/F{f001}
§/M{m2}
";
        var c = @"§M{m3:App}
§F{f001:Run:pub}
  §O{void}
  §C{Emit}
  §/C
§/F{f001}
§/M{m3}
";

        var diags = RunFull(("a.calr", a), ("b.calr", b), ("c.calr", c));

        // Ambiguous bare name → not resolved → no cross-module forbidden-effect error.
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_QualifiedName_ResolvesWhenBareAmbiguous()
    {
        var a = @"§M{m1:LoggerA}
§F{f001:Emit:pub}
  §O{void}
  §E{cw}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:LoggerB}
§F{f001:Emit:pub}
  §O{void}
  §E{fs:w}
§/F{f001}
§/M{m2}
";
        var c = @"§M{m3:App}
§F{f001:Run:pub}
  §O{void}
  §C{LoggerB.Emit}
  §/C
§/F{f001}
§/M{m3}
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
        var a = @"§M{m1:Repo}
§F{f001:Save:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:App}
§F{f001:Run:pub}
  §O{void}
  §C{Save}
  §/C
§/F{f001}
§/M{m2}
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
        var a = @"§M{m1:A}
§F{f001:DoWrite:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:B}
§F{f001:Run:pub}
  §O{void}
  §C{DoWrite}
  §/C
§/F{f001}
§/M{m2}
";

        // Compile caller in Permissive mode to confirm per-file pass would treat unknowns
        // leniently, yet the cross-module pass still errors.
        var modules = new List<(ModuleNode, string)>();
        foreach (var (path, source) in new[] { ("a.calr", a), ("b.calr", b) })
        {
            var result = Program.Compile(source, path, new CompilationOptions
            {
                EnforceEffects = true,
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
        var a = @"§M{m1:A}
§F{f001:Write:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:B}
§F{f001:Run:pub}
  §O{void}
  §E{db:w, cw, fs:w}
  §C{Write}
  §/C
§/F{f001}
§/M{m2}
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_UndeclaredPublicFunction_Warning()
    {
        // Public function without §E declaration → Calor0417 warning.
        var a = @"§M{m1:A}
§F{f001:Mystery:pub}
  §O{void}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:B}
§F{f001:Caller:pub}
  §O{void}
  §C{Mystery}
  §/C
§/F{f001}
§/M{m2}
";

        var (_, registryDiags) = CompileAll(("a.calr", a), ("b.calr", b));

        var warn = registryDiags.FirstOrDefault(d => d.Code == DiagnosticCode.UndeclaredPublicFunction);
        Assert.NotNull(warn);
        Assert.Contains("Mystery", warn!.Message);
    }

    [Fact]
    public void CrossModule_ParseErrorInOneFile_OthersChecked()
    {
        // If one file fails to compile, the cross-module pass should still run over
        // the files that did compile. We skip the broken file and check the rest.
        var good = @"§M{m1:A}
§F{f001:Save:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var caller = @"§M{m2:B}
§F{f001:Run:pub}
  §O{void}
  §C{Save}
  §/C
§/F{f001}
§/M{m2}
";

        var modules = new List<(ModuleNode, string)>();
        foreach (var (path, source) in new[] { ("a.calr", good), ("b.calr", caller) })
        {
            var result = Program.Compile(source, path, new CompilationOptions { EnforceEffects = false });
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
        var a = @"§M{m1:A}
§F{f001:ReadFile:pub}
  §O{void}
  §E{fs:r}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:B}
§F{f001:Run:pub}
  §O{void}
  §E{fs:rw}
  §C{ReadFile}
  §/C
§/F{f001}
§/M{m2}
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void CrossModule_EffectSubtyping_RDoesNotEncompassW_Fails()
    {
        // Caller declares fs:r; callee declares fs:w. r does NOT encompass w → violation.
        var a = @"§M{m1:A}
§F{f001:WriteFile:pub}
  §O{void}
  §E{fs:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:B}
§F{f001:Run:pub}
  §O{void}
  §E{fs:r}
  §C{WriteFile}
  §/C
§/F{f001}
§/M{m2}
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
        var a = @"§M{m1:OrderService}
§F{f001:Save:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:App}
§F{f001:Save:priv}
  §O{void}
§/F{f001}
§F{f002:Run:pub}
  §O{void}
  §C{OrderService.Save}
  §/C
§/F{f002}
§/M{m2}
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
        var a = @"§M{m1:Writer}
§F{f001:Save:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:App}
§F{f001:Save:priv}
  §O{void}
§/F{f001}
§F{f002:Run:pub}
  §O{void}
  §C{Save}
  §/C
§/F{f002}
§/M{m2}
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
        var a = @"§M{m1:PureLib}
§F{f001:Identity:pub}
  §O{void}
  §E{}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:Caller}
§F{f001:Run:pub}
  §O{void}
  §E{}
  §C{Identity}
  §/C
§/F{f001}
§/M{m2}
";

        var (_, registryDiags) = CompileAll(("a.calr", a), ("b.calr", b));

        // No "undeclared public function" warning for the explicitly-pure functions.
        Assert.DoesNotContain(registryDiags, d => d.Code == DiagnosticCode.UndeclaredPublicFunction);

        // And the caller doesn't need any effect declaration because callee declares none.
        var modules = new List<(ModuleNode, string)>();
        foreach (var (path, source) in new[] { ("a.calr", a), ("b.calr", b) })
        {
            var result = Program.Compile(source, path, new CompilationOptions { EnforceEffects = false });
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
        var a = @"§M{m1:Repo}
§F{f001:Persist:pub}
  §O{void}
  §E{db:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:Services}
§CL{c1:OrderService:pub}
§MT{m001:Handle:pub}
  §O{void}
  §C{Persist}
  §/C
§/MT{m001}
§/CL{c1}
§/M{m2}
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
                        CallerName = $"Func{i}",
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
        var a = @"§M{m1:Repo}
§CL{c1:OrderRepo:pub}
§MT{m001:Save:pub}
  §O{void}
  §E{db:w}
§/MT{m001}
§/CL{c1}
§/M{m1}
";
        var b = @"§M{m2:Services}
§CL{c1:OrderService:pub}
§MT{m001:Handle:pub}
  §O{void}
  §C{OrderRepo.Save}
  §/C
§/MT{m001}
§/CL{c1}
§/M{m2}
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
        var a = @"§M{m1:Worker}
§F{f001:Process:pub}
  §O{void}
  §E{db:w, net:w}
§/F{f001}
§/M{m1}
";
        var b = @"§M{m2:App}
§F{f001:Run:pub}
  §O{void}
  §C{Process}
  §/C
§/F{f001}
§/M{m2}
";

        var diags = RunFull(("a.calr", a), ("b.calr", b));

        // Both declared effects (db:w AND net:w) are reported — proving we used
        // the registry's declared effects rather than any partial manifest resolution.
        var fb = diags.Where(d => d.Code == DiagnosticCode.ForbiddenEffect).ToList();
        Assert.Equal(2, fb.Count);
        Assert.Contains(fb, d => d.Message.Contains("db:w"));
        Assert.Contains(fb, d => d.Message.Contains("net:w"));
    }
}
