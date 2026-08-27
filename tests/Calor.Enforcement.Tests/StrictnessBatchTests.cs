using Calor.Compiler;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Regression pins for the WS-W2 effect-soundness strictness batch (v0.11).
/// One test region per deliverable:
///   D-W2.1 delegate invocation = error under enforcement (Calor0418)
///   D-W2.2 override / interface-implementation effect variance (Calor0420/0421)
///          + static-receiver call-site charging
///   D-W2.3 interop → effect-unknown propagating as an assumption (Calor0419)
///   D-W2.4 bare-name purity removal (fail-closed unknown-call path)
///   D-W2.6 unknown constructs are never silently pure (Calor0419)
/// (D-W2.5, the CLI default flip, is pinned in Calor.Compiler.Tests where the
/// CLI test harness lives.)
/// </summary>
public class StrictnessBatchTests
{
    // ========================================================================
    // D-W2.1 — Delegate invocation is an unconditional error under enforcement
    // ========================================================================

    [Fact]
    public void DelegateInvocation_FunctionTypedParameter_WithoutRow_IsUnknown()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Apply:pub}
      §I{Func<i32,i32>:transform}
      §I{i32:value}
      §O{i32}
      §R §C{transform} §A value §/C
";
        var result = TestHarness.Compile(source);
        // v0.15 E4 (§13.1): row-less ⇒ Unknown ⇒ Calor0425 at the invocation, and the
        // Unknown charge fails closed as Calor0410 'unknown' on the pure declaration.
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DelegateInvocation);
        Assert.Contains(result.Diagnostics.Warnings, d => d.Code == DiagnosticCode.EffectRowUnknown && d.Message.Contains("'transform'"));
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("'unknown'"));
    }

    [Fact]
    public void DelegateInvocation_LambdaBoundLocal_ChargesInferredRow()
    {
        var source = @"
§M{m001:Test}
  §F{f001:UseLambda:pub}
      §O{i32}
      §B{f} §LAM{lam1:x:i32} (+ x 1) §/LAM{lam1}
      §R §C{f} §A INT:1 §/C
";
        var result = TestHarness.Compile(source);
        // v0.15 E4, baseline Y9a: a row-less §B takes its initializer's row (§3.5); ρ_body is pure, so {} is charged and it compiles.
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DelegateInvocation || d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void DelegateInvocation_UnderPermissiveEffects_Calor0425IsSuppressed()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Apply:pub}
      §I{Func<i32,i32>:transform}
      §I{i32:value}
      §O{i32}
      §R §C{transform} §A value §/C
";
        var result = TestHarness.CompileWithEffects(source, enforceEffects: true,
            policy: UnknownCallPolicy.Permissive);
        // v0.15 E4 (§4.5): the flag's one job is to waive "cannot tell" — the Calor0425 is
        // suppressed, nothing is charged, and the file compiles. Its sibling for "does not
        // fit is never waived" is NeverWaived_DoesNotFit_AtEveryMonomorphicSite (P11).
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DelegateInvocation || d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void BareNameCall_InternalFunction_StillResolves()
    {
        // The delegate error must NOT hit same-module bare-name calls.
        var source = @"
§M{m001:Test}
  §F{f001:Helper:pri}
      §I{i32:x}
      §O{i32}
      §R (+ x 1)
  §F{f002:Main:pub}
      §O{i32}
      §R §C{Helper} §A INT:1 §/C
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"Bare-name internal call must still resolve. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.DelegateInvocation);
    }

    [Fact]
    public void FreeBareName_FailsClosed_NotSilentlyPure()
    {
        // Pre-W2 behavior: a free bare-name invocation was silently assumed pure.
        // Now it routes through the unknown-call chain (Calor0411 + worst-case
        // effects → Calor0410) under the default strict policy.
        var source = @"
§M{m001:Test}
  §CL{c001:Mapper:pub}
      §MT{mt001:Apply:pub}
          §I{i32:value}
          §O{i32}
          §R §C{transform} §A value §/C
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "A free bare-name invocation must fail closed");
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("transform"));
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    // ========================================================================
    // D-W2.2 — Override + interface-implementation effect variance
    // ========================================================================

    [Fact]
    public void OverrideWithBroaderEffects_IsError()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Base:pub}
      §MT{mt001:Render:pub:virt}
          §O{void}
          §E{}
  §CL{c002:Derived:pub}
      §EXT{Base}
      §MT{mt002:Render:pub:over}
          §O{void}
          §E{cw}
          §P ""laundered""
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Override broadening the base effect set must fail");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.OverrideEffectVariance && d.Message.Contains("Render"));
    }

    [Fact]
    public void GenericOverrideWithAlphaEquivalentTypeParameters_IsMatchedForVariance()
    {
        var source = """
            §M{m001:Test}
              §CL{c001:Base:pub}
                §MT{mt001:Render<T>:pub:virt} (T:value) -> void
                  §E{}
              §CL{c002:Derived:Base:pub}
                §MT{mt002:Render<U>:pub:over} (U:value) -> void
                  §E{cw}
                  §P "laundered"
            """;

        var result = TestHarness.Compile(source);

        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.OverrideEffectVariance
            && diagnostic.Message.Contains("Render", StringComparison.Ordinal));
    }

    [Fact]
    public void OverrideWithSubsetEffects_Compiles()
    {
        var source = @"
§M{m001:Test}
  §CL{c001:Base:pub}
      §MT{mt001:Render:pub:virt}
          §O{void}
          §E{cw,fs:w}
          §P ""base""
  §CL{c002:Derived:pub}
      §EXT{Base}
      §MT{mt002:Render:pub:over}
          §O{void}
          §E{cw}
          §P ""derived""
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"Override with a subset of base effects must compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void InterfaceImplementationWithBroaderEffects_IsError()
    {
        var source = @"
§M{m001:Test}
  §IFACE{i001:IRenderer}
      §MT{m001:Render}
          §O{void}
          §E{}
  §CL{c001:ConsoleRenderer:pub}
      §IMPL{IRenderer}
      §MT{mt001:Render:pub}
          §O{void}
          §E{cw}
          §P ""rendering""
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Implementation broadening the interface effect set must fail");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.InterfaceEffectVariance && d.Message.Contains("Render"));
    }

    [Fact]
    public void InterfaceImplementationWithinDeclaredEffects_Compiles()
    {
        var source = @"
§M{m001:Test}
  §IFACE{i001:IRenderer}
      §MT{m001:Render}
          §O{void}
          §E{cw}
  §CL{c001:ConsoleRenderer:pub}
      §IMPL{IRenderer}
      §MT{mt001:Render:pub}
          §O{void}
          §E{cw}
          §P ""rendering""
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"Implementation within the interface's declared effects must compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void OverrideOfExternalBase_RoutesToAssumedChannel()
    {
        // Base class is external C# (not in this module): variance cannot be
        // checked, so the assumed channel is Calor0425 (§13.1's `:260` rewrite).
        var source = @"
§M{m001:Test}
  §CL{c001:MyController:pub}
      §EXT{SomeExternalBase}
      §MT{mt001:Handle:pub:over}
          §O{void}
          §E{}
";
        var result = TestHarness.Compile(source);

        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown && d.Message.Contains("external base"));
    }

    [Fact]
    public void CallThroughInterfaceTypedReceiver_ChargesInterfaceDeclaredEffects()
    {
        // D-W2.2 call-site leg: shape.Describe through an IShape-typed parameter
        // charges the interface's declared §E{cw} — no unknown-call diagnostic,
        // and the caller must declare cw.
        var missingDeclaration = @"
§M{m001:Test}
  §IFACE{i001:IShape}
      §MT{m001:Describe}
          §O{void}
          §E{cw}
  §F{f001:Draw:pub}
      §I{IShape:shape}
      §O{void}
      §C{shape.Describe}
      §/C
";
        var failing = TestHarness.Compile(missingDeclaration);
        Assert.True(failing.HasErrors, "Caller must declare the interface's declared effect");
        Assert.Contains(failing.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("Draw"));
        Assert.DoesNotContain(failing.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("shape.Describe"));

        var declared = missingDeclaration.Replace("      §O{void}\n      §C{shape.Describe}",
            "      §O{void}\n      §E{cw}\n      §C{shape.Describe}");
        var passing = TestHarness.Compile(declared);
        Assert.False(passing.HasErrors,
            $"Caller declaring the interface effect must compile. Errors: {string.Join("; ", passing.Diagnostics.Errors.Select(e => e.Message))}");
    }

    // ========================================================================
    // D-W2.3 — Interop is effect-unknown, propagating as an assumption
    // ========================================================================

    [Fact]
    public void InteropContent_SurfacesAssumption()
    {
        var source = @"
§M{m001:Test}
  §F{f001:UsesInterop:pub}
      §O{void}
      §RAW
var x = System.Environment.TickCount;
§/RAW
";
        var result = TestHarness.Compile(source);

        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.AssumedEffects
                && d.Message.Contains("UsesInterop")
                && d.Message.Contains("interop"));
    }

    [Fact]
    public void InteropAssumption_PropagatesToCaller()
    {
        var source = @"
§M{m001:Test}
  §F{f001:UsesInterop:pub}
      §O{void}
      §RAW
var x = 1;
§/RAW
  §F{f002:Caller:pub}
      §O{void}
      §C{UsesInterop}
      §/C
  §F{f003:Transitive:pub}
      §O{void}
      §C{Caller}
      §/C
";
        var result = TestHarness.Compile(source);

        // The direct caller AND the transitive caller inherit the assumption.
        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.AssumedEffects && d.Message.Contains("'Caller'"));
        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.AssumedEffects && d.Message.Contains("'Transitive'"));
    }

    [Fact]
    public void InteropAssumption_UnderStrictEffects_IsError()
    {
        var source = @"
§M{m001:Test}
  §F{f001:UsesInterop:pub}
      §O{void}
      §RAW
var x = 1;
§/RAW
";
        var options = new CompilationOptions
        {
            EnforceEffects = true,
            StrictEffects = true
        };
        var result = TestHarness.Compile(source, options);

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.AssumedEffects && d.Message.Contains("UsesInterop"));
    }

    // ========================================================================
    // D-W2.4 — bare-method-name purity removal
    // ========================================================================

    [Fact]
    public void PurgedMutator_UntypedReceiverAdd_FailsClosed()
    {
        // The #785 reproduction: a pure function calling items.Add must no longer
        // pass strict enforcement. With no type information the call routes
        // through the unknown-call chain (fail loud), not the purged name list.
        var source = @"
§M{m001:Test}
  §F{f001:AddItem:pub}
      §O{void}
      §C{items.Add}
        §A INT:1
      §/C
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "items.Add in a pure function must fail enforcement");
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("items.Add"));
    }

    [Fact]
    public void TypedListReceiver_Add_ResolvesToMutViaManifest()
    {
        // With a typed receiver the manifest resolves List`1.Add = mut: the
        // caller must declare mut (and gets Calor0410, not Calor0411, without it).
        var missing = @"
§M{m001:Test}
  §F{f001:AddItem:pub}
      §O{void}
      §B{List<i32>:items} §NEW{List<i32>} §/NEW
      §C{items.Add}
        §A INT:1
      §/C
";
        var failing = TestHarness.Compile(missing);
        Assert.True(failing.HasErrors, "Typed list Add without §E{mut} must fail");
        Assert.Contains(failing.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("mut"));
        Assert.DoesNotContain(failing.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("items.Add"));
    }

    [Fact]
    public void UntypedReceiver_PureLookingMethodName_FailsClosed()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Query:pub}
      §O{void}
      §E{}
      §C{items.Where}
      §/C
";
        var result = TestHarness.Compile(source);

        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
                && diagnostic.Message.Contains("items.Where"));
        Assert.Contains(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("unknown"));
    }

    [Fact]
    public void ResolvedImmutableApis_LinqAndStringOps_StillPure()
    {
        // Pure calls remain pure only through resolved receiver identities and
        // authoritative manifest entries — never through a global name list.
        var source = @"
§M{m001:Test}
  §F{f001:Query:pub}
      §I{List<i32>:items}
      §I{str:name}
      §O{void}
      §C{items.Where}
      §/C
      §C{items.Select}
      §/C
      §C{items.ToList}
      §/C
      §C{name.Trim}
      §/C
      §C{name.PadLeft}
        §A INT:10
      §/C
      §C{items.ConvertAll}
      §/C
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"Audited-pure names must remain pure. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    // ========================================================================
    // W2 adversarial review fixes (PR #842) — C2, C1, C3, C4, C5, M1
    // ========================================================================

    [Fact]
    public void C2_DecoyNamedDelegateParameter_ShadowsFunction_RowGoverns()
    {
        // Review C2 / v0.15 E4 (§13.1): a Func parameter named like a pure module
        // function is resolved as the VALUE, so the DECOY'S row governs — row-less
        // ⇒ Calor0425 + Calor0410 'unknown' — never the shadowed pure function's.
        var source = @"
§M{m001:Shadow}
  §F{f001:Helper:pub}
      §O{i32}
      §E{}
      §R INT:1
  §F{f002:Loud:pub}
      §O{i32}
      §E{cw}
      §P ""laundered""
      §R INT:2
  §F{f003:Go:pub}
      §I{Func<i32>:Helper}
      §O{i32}
      §E{}
      §R §C{Helper} §/C
  §F{f004:Main:pub}
      §O{void}
      §E{}
      §B{r:i32} §C{Go} §A Loud §/C
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Decoy-named delegate invocation must fail closed");
        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown && d.Message.Contains("'Helper'") && d.Message.Contains("'Go'"));
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("'Go'") && d.Message.Contains("'unknown'"));
        // C4 companion: Main passes the impure method group 'Loud' — charged at the passing site, so §E{} on Main is a Calor0410.
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("Main"));
    }

    [Fact]
    public void C1_PpBlock_StatementLeg_ChargesBranchEffects()
    {
        // Review C1 (statement leg): effects inside a §PP conditional block are
        // charged (union of branches) — a §PP body is never silently pure.
        var source = @"
§M{m001:PpHole}
  §F{f001:Sneaky:pub}
      §O{void}
      §E{}
      §PP{DEBUG}
      §C{Console.WriteLine} ""hidden effect""
      §/PP{DEBUG}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "§PP-wrapped effects must be charged");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect
                && d.Message.Contains("Sneaky") && d.Message.Contains("cw"));
    }

    [Fact]
    public void C1_PpBlock_MemberLeg_WrappedMethodIsEnforced()
    {
        // Review C1 (member leg): a class method wrapped in a class-level §PP
        // block must not escape enforcement.
        var source = @"
§M{m001:PPM}
  §CL{c001:Logger:pub}
      §PP{DEBUG}
      §MT{mt001:Sneak:pub}
          §O{void}
          §E{}
          §C{Console.WriteLine} §A ""pp-wrapped method effect"" §/C
      §/PP{DEBUG}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "§PP-wrapped methods must be enforced");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect
                && d.Message.Contains("Sneak") && d.Message.Contains("cw"));
    }

    [Fact]
    public void C3_InheritedImplementation_BroaderEffects_IsError()
    {
        // Review C3: an interface implementation satisfied by an INHERITED
        // in-module method is variance-checked (Calor0421) — inheritance must
        // not launder through interface dispatch.
        var source = @"
§M{m001:InheritLaunder}
  §IFACE{i001:IQuiet}
      §MT{m001:Run}
          §O{void}
          §E{}
  §CL{c001:Loud:pub}
      §MT{mt001:Run:pub}
          §O{void}
          §E{cw}
          §P ""runs loud""
  §CL{c002:Sneaky:pub}
      §EXT{Loud}
      §IMPL{IQuiet}
      §MT{mt002:Noop:pub}
          §O{void}
          §E{}
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Inherited implementation broadening interface effects must fail");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.InterfaceEffectVariance
                && d.Message.Contains("Sneaky") && d.Message.Contains("inherited"));
    }

    [Fact]
    public void C3_ExternalInheritedImplementation_RoutesToAssumed()
    {
        // Review C3 (external arm): §IMPL satisfied only by a member inherited
        // from an external base is surfaced as Calor0425 (§13.1's `:607`).
        var source = @"
§M{m001:ExtImpl}
  §IFACE{i001:IQuiet}
      §MT{m001:Run}
          §O{void}
          §E{}
  §CL{c001:Bridge:pub}
      §EXT{SomeExternalBase}
      §IMPL{IQuiet}
      §MT{mt001:Other:pub}
          §O{void}
          §E{}
";
        var result = TestHarness.Compile(source);

        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown
                && d.Message.Contains("SomeExternalBase") && d.Message.Contains("IQuiet.Run"));
    }

    [Fact]
    public void C4_MethodGroupArgument_ChargesCalleeDeclaredEffects()
    {
        // Review C4: a method-group argument (bare reference to an internal
        // function) charges that function's declared effects at the passing
        // site — ConvertAll and friends can no longer launder it.
        var source = @"
§M{m001:HigherOrder}
  §F{f001:LoudMap:pub}
      §I{i32:x}
      §O{i32}
      §E{cw}
      §P ""mapping""
      §R x
  §F{f002:Go:pub}
      §I{List<i32>:items}
      §O{void}
      §E{}
      §B{r} §C{items.ConvertAll} §A LoudMap §/C
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Method-group argument effects must be charged");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect
                && d.Message.Contains("Go") && d.Message.Contains("cw"));
    }

    [Fact]
    public void C4_DelegateValueArgument_ToKnownHigherOrderName_SurfacesAssumption()
    {
        // Review C4 (value arm): a function-typed VALUE passed to a
        // manifest-resolved higher-order call (Select) is surfaced as a
        // Calor0419 assumption — the BCL callee may invoke it invisibly.
        var source = @"
§M{m001:HofVal}
  §F{f001:Go:pub}
      §I{Func<i32,i32>:f}
      §I{List<i32>:items}
      §O{void}
      §E{}
      §B{r} §C{items.Select} §A f §/C
";
        var result = TestHarness.Compile(source);

        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.AssumedEffects
                && d.Message.Contains("'f'") && d.Message.Contains("items.Select"));
    }

    [Fact]
    public void C5_ExternalTypedReceiver_CollidingMethodName_FailsLoud()
    {
        // Review C5: a receiver whose static type is KNOWN and external must not
        // be captured by an in-module method-name collision — it goes to the
        // unknown chain (fail loud), for both single and chained receivers.
        var source = @"
§M{m001:RC}
  §CL{c001:Helper:pub}
      §MT{mt001:Refresh:pub}
          §O{i32}
          §E{}
          §R INT:1
  §F{f001:GoSingle:pub}
      §I{SomeExternal:svc}
      §O{void}
      §E{}
      §C{svc.Refresh}
      §/C
  §F{f002:GoChained:pub}
      §I{SomeExternal:svc}
      §O{void}
      §E{}
      §C{svc.conn.Refresh}
      §/C
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "External-typed receiver collisions must fail loud");
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("svc.Refresh"));
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("svc.conn.Refresh"));
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("GoSingle"));
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("GoChained"));
    }

    [Fact]
    public void C5_InModuleTypedReceiver_StillResolves()
    {
        // Companion: a receiver statically typed as the in-module declaring
        // class still resolves (declared-§E charge) — no unknown-call noise.
        var source = @"
§M{m001:RC2}
  §CL{c001:Helper:pub}
      §MT{mt001:Refresh:pub}
          §O{i32}
          §E{}
          §R INT:1
  §F{f001:Go:pub}
      §O{void}
      §E{alloc}
      §B{h:Helper} §NEW{Helper} §/NEW
      §C{h.Refresh}
      §/C
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"In-module typed receiver must still resolve. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void M1_ExpressionCallSpelling_DelegateValue_ChargesTheRow()
    {
        // Review M1 / v0.15 E4 (§13.1): `§C f §A x §/C` is the same invocation as
        // `§C{f}` — the row of `f` is charged; row-less ⇒ Calor0425 + 0410 'unknown'.
        var source = @"
§M{m001:Wrap}
  §F{f001:Apply:pub}
      §I{Func<i32,i32>:f}
      §I{i32:x}
      §O{i32}
      §E{}
      §R §C f §A x §/C
";
        var result = TestHarness.Compile(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DelegateInvocation);
        Assert.True(result.HasErrors, "Expression-call invocation of an Unknown row must fail closed");
        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown && d.Message.Contains("'f'"));
    }

    [Fact]
    public void M1_ReturnedDelegateInvocation_ChargesTheReturnRow()
    {
        // Review M1 / v0.15 E4 (§13.1): invoking the RESULT of a call (`GetF()()`)
        // charges the callee's declared RETURN row; `§O` with no row ⇒ Calor0425.
        var source = @"
§M{m001:E}
  §F{f001:GetF:pub}
      §O{Func<i32>}
      §E{}
      §R §LAM{l1} §R INT:1 §/LAM{l1}
  §F{f002:Go:pub}
      §O{i32}
      §E{}
      §R §C §C{GetF} §/C §/C
";
        var result = TestHarness.Compile(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DelegateInvocation);
        Assert.True(result.HasErrors, "Invoking a returned value whose return row is Unknown must fail closed");
        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown && d.Message.Contains("returned by 'GetF'") && d.Message.Contains("'Go'"));
    }

    // ========================================================================
    // D-W2.6 — Unknown constructs are never silently pure
    // ========================================================================

    private sealed class MysteryStatementNode : StatementNode
    {
        public MysteryStatementNode() : base(new TextSpan(0, 1, 1, 1)) { }
        public override void Accept(IAstVisitor visitor) { }
        public override T Accept<T>(IAstVisitor<T> visitor) => default!;
    }

    [Fact]
    public void UnknownStatementConstruct_RoutesToAssumedChannel()
    {
        // Build a module whose function body contains a statement kind the
        // enforcement pass has never seen: the catch-all must surface a
        // Calor0419 assumption, never silently return pure.
        var span = new TextSpan(0, 1, 1, 1);
        var function = new FunctionNode(
            span, "f001", "Mystery", Visibility.Public,
            Array.Empty<ParameterNode>(),
            output: null,
            effects: null,
            body: new StatementNode[] { new MysteryStatementNode() },
            attributes: new AttributeCollection());
        var module = new ModuleNode(
            span, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(),
            new[] { function },
            new AttributeCollection());

        var diagnostics = new DiagnosticBag();
        var pass = new EffectEnforcementPass(diagnostics);
        pass.Enforce(module);

        Assert.Contains(diagnostics,
            d => d.Code == DiagnosticCode.AssumedEffects
                && d.Message.Contains("MysteryStatementNode"));
    }

    [Fact]
    public void UnknownStatementConstruct_UnderStrictEffects_IsError()
    {
        var span = new TextSpan(0, 1, 1, 1);
        var function = new FunctionNode(
            span, "f001", "Mystery", Visibility.Public,
            Array.Empty<ParameterNode>(),
            output: null,
            effects: null,
            body: new StatementNode[] { new MysteryStatementNode() },
            attributes: new AttributeCollection());
        var module = new ModuleNode(
            span, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(),
            new[] { function },
            new AttributeCollection());

        var diagnostics = new DiagnosticBag();
        var pass = new EffectEnforcementPass(diagnostics, strictEffects: true);
        pass.Enforce(module);

        Assert.Contains(diagnostics.Errors,
            d => d.Code == DiagnosticCode.AssumedEffects);
    }

    // ========================================================================
    // v0.15 E3 slice a — design-doc §6.2's compatibility sites.
    //
    // APPENDED, deliberately. `experiments/facts.py` probes this file by LINE
    // NUMBER (`sed -n '{n}p'` for eighteen lines, the highest being 745), and
    // §13.5(a) permits E3 exactly one transcript regeneration. Everything below
    // sits past the last probed line so no probe moves.
    //
    // P15 is gate 1's frozen denominator: one `_IsError` / `_Compiles` pair per
    // laundering class, plus a `_CannotTell` arm each, identified by CODE and
    // POLARITY. Five of the six classes are closed here; the sixth
    // (rank-1 generic instantiation) is slice b's and says so out loud rather
    // than being quietly absent.
    // ========================================================================

    // ------------------------------------------------- P15 site 1: assignment

    [Fact]
    public void RowMismatch_AtAssignment_IsError()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Main:pub} (Func<i32,i32>:src §E{cw}) -> i32
                §E{cw}
                §B{f:Func<i32,i32>} §E{} src
                §R INT:1
            """);

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              && d.Message.Contains("binding 'f'", StringComparison.Ordinal));
    }

    [Fact]
    public void RowMismatch_AtAssignment_Compiles()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Main:pub} (Func<i32,i32>:src §E{cw}) -> i32
                §E{cw}
                §B{f:Func<i32,i32>} §E{cw} src
                §R INT:1
            """);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              || d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void RowMismatch_AtAssignment_CannotTell()
    {
        // The source carries no row, so nothing is known about it. Not a
        // mismatch — an undecidable hop.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Main:pub} (Func<i32,i32>:src) -> i32
                §E{cw}
                §B{f:Func<i32,i32>} §E{} src
                §R INT:1
            """);

        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown
              && d.Message.Contains("binding 'f'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMismatch);
    }

    [Fact]
    public void RowMismatch_AtReassignment_IsError()
    {
        // §6.2 site 1's SECOND half — "§B init, AND re-assignment to a
        // function-typed mutable" — which review round 1 (F10) found
        // unimplemented. Without it a mutable is a laundering hole: the binding
        // reports once and every later §ASSIGN through the same name is free.
        //
        // Two Calor0424s here, and both are wanted: one for the initializer
        // (site 1's first half) and one for the re-assignment.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Main:pub} (Func<i32,i32>:src §E{cw}) -> i32
                §E{cw}
                §B{~f:Func<i32,i32>} §E{} src
                §ASSIGN f src
                §R INT:1
            """);

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              && d.Message.Contains("Value assigned to 'f'", StringComparison.Ordinal));
    }

    [Fact]
    public void RowMismatch_AtReassignment_Compiles()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Main:pub} (Func<i32,i32>:src §E{cw}) -> i32
                §E{cw}
                §B{~f:Func<i32,i32>} §E{cw} src
                §ASSIGN f src
                §R INT:1
            """);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              || d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void Reassignment_ToAFunctionTypedField_IsAdjudicated()
    {
        // A field of the owning class is in scope by bare name, so §ASSIGN
        // through it is a site. What is NOT a site is a target that needs the
        // RECEIVER typed (`this.cb`, `xs[i]`); RowSiteChecker declines those,
        // and that limit is recorded in the method's doc comment rather than
        // silently relied on.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §CL{c001:Holder:pub}
                §FLD{Func<i32,i32>:cb:pri} §E{}
                §MT{mt001:Set:pub} (Func<i32,i32>:src §E{cw}) -> void
                  §E{}
                  §ASSIGN cb src
            """);

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              && d.Message.Contains("Value assigned to 'cb'", StringComparison.Ordinal));
    }

    // --------------------------------------------------- P15 site 2: argument

    [Fact]
    public void RowMismatch_AtArgument_IsError()
    {
        var result = TestHarness.Compile(ArgumentSite("§E{}"));

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              && d.Message.Contains("Argument 'Shout'", StringComparison.Ordinal));
    }

    [Fact]
    public void RowMismatch_AtArgument_Compiles()
    {
        var result = TestHarness.Compile(ArgumentSite("§E{cw}"));

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              || d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void RowMismatch_AtArgument_CannotTell()
    {
        // No row on the parameter at all — §6.4's second message sample.
        var result = TestHarness.Compile(ArgumentSite(""));

        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown
              && d.Message.Contains("Parameter 'transform' of 'Apply'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMismatch);
    }

    /// <summary>§6.4's own example, parameterised on the destination row.</summary>
    private static string ArgumentSite(string parameterRow) => $$"""
        §M{m001:M}
          §F{f001:Shout:pub} (i32:x) -> i32
            §E{cw}
            §P x
            §R x
          §F{f002:Apply:pub} (Func<i32,i32>:transform {{parameterRow}}, i32:value) -> i32
            §E{}
            §R value
          §F{f003:Main:pub} () -> i32
            §E{cw}
            §R §C{Apply} §A Shout §A INT:1 §/C
        """;

    // ----------------------------------------------------- P15 site 3: return

    [Fact]
    public void RowMismatch_AtReturn_IsError()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Pick:pub} (Func<i32,i32>:src §E{cw}) -> Func<i32,i32> §E{}
                §E{}
                §R src
            """);

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              && d.Message.Contains("the return of 'Pick'", StringComparison.Ordinal));
    }

    [Fact]
    public void RowMismatch_AtReturn_Compiles()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Pick:pub} (Func<i32,i32>:src §E{cw}) -> Func<i32,i32> §E{cw}
                §E{}
                §R src
            """);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMismatch
              || d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void RowMismatch_AtReturn_CannotTell()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Pick:pub} (Func<i32,i32>:src) -> Func<i32,i32> §E{cw}
                §E{}
                §R src
            """);

        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown
              && d.Message.Contains("the return of 'Pick'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowMismatch);
    }

    // --------------------------------------------------- P15 site 4: override
    // Calor0420 keeps its own code (§6.3) and is now DERIVED from
    // EffectRow.Fits. These are the rows-shaped RE-PINS the four historical
    // assertions above are re-stated as: same polarity, plus the row clause the
    // message gained.

    [Fact]
    public void RowMismatch_AtOverride_IsError()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §CL{c001:Base:pub}
                §MT{mt001:Render:pub:virt} () -> void
                  §E{}
              §CL{c002:Derived:Base:pub}
                §MT{mt002:Render:pub:over} () -> void
                  §E{cw}
                  §P "laundered"
            """);

        var error = Assert.Single(result.Diagnostics.Errors
            .Where(d => d.Code == DiagnosticCode.OverrideEffectVariance));
        Assert.Contains("Effect row cw does not fit the base method's row [pure]",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RowMismatch_AtOverride_Compiles()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §CL{c001:Base:pub}
                §MT{mt001:Render:pub:virt} () -> void
                  §E{cw,fs:w}
                  §P "base"
              §CL{c002:Derived:Base:pub}
                §MT{mt002:Render:pub:over} () -> void
                  §E{cw}
                  §P "derived"
            """);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.OverrideEffectVariance
              || d.Code == DiagnosticCode.EffectRowUnknown);
    }

    // ------------------------------------------ P15 site 5: interface impl

    [Fact]
    public void RowMismatch_AtInterfaceImpl_IsError()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §IFACE{i001:IRenderer}
                §MT{m002:Render} () -> void
                  §E{}
              §CL{c001:ConsoleRenderer:pub}
                §IMPL{IRenderer}
                §MT{mt001:Render:pub} () -> void
                  §E{cw}
                  §P "rendering"
            """);

        var error = Assert.Single(result.Diagnostics.Errors
            .Where(d => d.Code == DiagnosticCode.InterfaceEffectVariance));
        Assert.Contains("Effect row cw does not fit the interface's row [pure]",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RowMismatch_AtInterfaceImpl_Compiles()
    {
        var result = TestHarness.Compile("""
            §M{m001:M}
              §IFACE{i001:IRenderer}
                §MT{m002:Render} () -> void
                  §E{cw}
              §CL{c001:ConsoleRenderer:pub}
                §IMPL{IRenderer}
                §MT{mt001:Render:pub} () -> void
                  §E{cw}
                  §P "rendering"
            """);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.InterfaceEffectVariance
              || d.Code == DiagnosticCode.EffectRowUnknown);
    }

    // ------------------------------- P15 site 6: rank-1 generic instantiation

    [Fact]
    public void RowMismatch_AtGenericInstantiation_IsError()
    {
        // Gate 1's SIXTH class, CLOSED by E3 slice b. This test was
        // `..._IsSliceBs_AndTheGapIsObserved` and asserted that NOTHING fired;
        // it is flipped here, which is what §13.2 said slice b would do.
        //
        // **The code is Calor0410, not Calor0424, and that is a divergence from
        // §6.2's table this PR reports rather than hides.** §6.2 row 6 writes
        // Calor0424 for site 6's DoesNotFit; §7.4's solve makes that cell
        // UNREACHABLE, because `e := ⊔ (ρ(argⱼ) ⊖ ρ_declⱼ)` defines the solution
        // as the join of the residuals — so the substituted parameter row
        // contains every argument row BY CONSTRUCTION and no argument can fail
        // `fits` at a variable-mentioning position. What site 6 can catch is the
        // CALLER under-declaring the instantiated row, and §10.3's own worked
        // example spells exactly that as Calor0410 with a new provenance clause.
        // The class is closed; the code that closes it is 0410.
        //
        // Discriminating revert: delete the InstantiateAndCharge call in
        // CheckArgumentSite and `UsePure` is silently charged nothing.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Map:pub}<eff e> (i32:x, Func<i32,i32>:f §E{e}) -> i32
                §E{e}
                §R x
              §F{f002:Announce:pub} (i32:x) -> i32
                §E{cw}
                §P x
                §R x
              §F{f003:UsePure:pub} () -> i32
                §E{}
                §R §C{Map} §A INT:1 §A Announce §/C
            """);

        var reported = Assert.Single(result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect);
        // P22 — §10.3's FIRST string, by full equality.
        Assert.Equal(
            "Function 'UsePure' uses effect 'cw' but does not declare it\n"
            + "  Effect row: effect variable 'e' of 'Map' instantiated to cw at this call site",
            reported.Message);
    }

    [Fact]
    public void RowMismatch_AtGenericInstantiation_Compiles()
    {
        // The `_Compiles` half the gap pin promised: `Double` in place of
        // `Announce`, so `e := Concrete(∅)` and `Map`'s instantiated row is pure.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Map:pub}<eff e> (i32:x, Func<i32,i32>:f §E{e}) -> i32
                §E{e}
                §R x
              §F{f002:Double:pub} (i32:x) -> i32
                §E{}
                §R (* x INT:2)
              §F{f003:UsePure:pub} () -> i32
                §E{}
                §R §C{Map} §A INT:1 §A Double §/C
            """);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
              || d.Code == DiagnosticCode.EffectRowMismatch
              || d.Code == DiagnosticCode.EffectRowUnknown
              || d.Code == DiagnosticCode.EffectVariableScope);
    }

    [Fact]
    public void RowMismatch_AtGenericInstantiation_CannotTell_IsCalor0425()
    {
        // §7.4 — "Any Unknown contributor makes e := Unknown and the site reports
        // Calor0425." Here `cb` is a function-typed parameter with NO row, so its
        // row is Unknown (§3.5) and the variable cannot be solved.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Map:pub}<eff e> (i32:x, Func<i32,i32>:f §E{e}) -> i32
                §E{e}
                §R x
              §F{f003:Use:pub} (Func<i32,i32>:cb) -> i32
                §E{}
                §R §C{Map} §A INT:1 §A cb §/C
            """);

        // P22 — §10.3's SECOND string. The doc's sample ends "'UseImpure' is
        // charged Unknown effects"; the shipped tail says what actually happens
        // instead, because charging an `unknown` effect would raise a Calor0410
        // the author cannot declare away. The divergence is recorded in the PR
        // body and in docs/plans/2026-08-26-v0.15-e3b-notes.md.
        var reported = Assert.Single(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowUnknown
              && d.Message.StartsWith("Effect variable", StringComparison.Ordinal));
        Assert.Equal(
            "Effect variable 'e' of 'Map' instantiates to Unknown at this call site: the row of "
            + "argument 'cb' could not be determined. The instantiated row of 'Map' is Unknown "
            + "here, so nothing is charged to 'Use' for it. State a row on the argument's "
            + "declaration, or compile with --permissive-effects.",
            reported.Message);
    }

    [Fact]
    public void GenericInstantiation_AlphaEquivalentBinders_Unify()
    {
        // §7.5's R2 at the level a caller can see: `Outer` binds `eff a` and
        // passes its own rowed parameter into `Inner`, which binds `eff b`. The
        // two are identified by ORDINAL, so the instantiated row is `a` again and
        // `Outer`'s own declaration covers it. Under slice a the polymorphic
        // position was declined outright and nothing was compared at all.
        //
        // Discriminating revert: compare binders by NAME and this reports that
        // 'Outer' uses effect variable 'b'.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Inner:pub}<eff b> (Func<i32>:g §E{b}) -> i32
                §E{b}
                §R INT:0
              §F{f002:Outer:pub}<eff a> (Func<i32>:h §E{a}) -> i32
                §E{a}
                §R §C{Inner} §A h §/C
            """);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
              || d.Code == DiagnosticCode.EffectRowMismatch
              || d.Code == DiagnosticCode.EffectRowUnknown
              || d.Code == DiagnosticCode.EffectVariableScope);
    }

    [Fact]
    public void GenericInstantiation_CallerMustDeclareTheVariableItPassesOn()
    {
        // The other polarity of the same rule: `Outer` does NOT declare its own
        // variable in its row, so the instantiated row mentions a variable the
        // caller has not promised. That is an undeclared effect, spelled in
        // today's Calor0410 shape.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Inner:pub}<eff b> (Func<i32>:g §E{b}) -> i32
                §E{b}
                §R INT:0
              §F{f002:Outer:pub}<eff a> (Func<i32>:h §E{a}) -> i32
                §E{}
                §R §C{Inner} §A h §/C
            """);

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
              && d.Message.Contains("uses effect variable 'a' but does not declare it"));
    }

    [Fact]
    public void ExternalBaseAssumptions_AreCalor0425_AndNoCalor0419Remains()
    {
        // The half of §13.1's `:260`/`:607` rewrites that could not live IN those
        // two tests without moving facts.py's line probes: both arms must have
        // stopped emitting Calor0419, not merely started emitting Calor0425.
        //
        // §6.2 requires them to move TOGETHER, and slice a declined to move
        // either for that reason: the override arm was an AddAssumption whose
        // reasons propagate through PropagateAssumptions into every caller's
        // Calor0419, while the interface arm was a direct report. Retiring one
        // alone would make sites 4 and 5 disagree about what an unresolvable base
        // means. Both are retired here, so this asserts BOTH polarities at once.
        //
        // Discriminating revert: restore either AddAssumption/Report and the
        // matching DoesNotContain fails.
        var overrideArm = TestHarness.Compile(@"
§M{m001:Test}
  §CL{c001:MyController:pub}
      §EXT{SomeExternalBase}
      §MT{mt001:Handle:pub:over}
          §O{void}
          §E{}
");
        Assert.DoesNotContain(overrideArm.Diagnostics,
            d => d.Code == DiagnosticCode.AssumedEffects);
        Assert.Contains(overrideArm.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowUnknown);

        var interfaceArm = TestHarness.Compile(@"
§M{m001:ExtImpl}
  §IFACE{i001:IQuiet}
      §MT{m001:Run}
          §O{void}
          §E{}
  §CL{c001:Bridge:pub}
      §EXT{SomeExternalBase}
      §IMPL{IQuiet}
      §MT{mt001:Other:pub}
          §O{void}
          §E{}
");
        Assert.DoesNotContain(interfaceArm.Diagnostics,
            d => d.Code == DiagnosticCode.AssumedEffects);

        // §6.4's THIRD message sample, by full equality — the string P22's own
        // enumeration says ships with these two retirements. It RE-WORDS the old
        // Calor0419 text rather than merely re-coding it: it names the row.
        var reported = Assert.Single(interfaceArm.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.Equal(
            "Class 'Bridge' implements 'IQuiet.Run' through a member not visible in this module "
            + "(inherited from external base 'SomeExternalBase'), so its effect row is Unknown. "
            + "The interface's declared row [pure] is assumed here, not verified.",
            reported.Message);
    }

    [Fact]
    public void GenericInstantiation_ChargePropagatesToTransitiveCallers()
    {
        // Review round 1, finding 1 — the soundness hole, closed. `Run` is
        // rank-1; `Outer` instantiates its variable to {cw} by passing a printing
        // callback; `Top` calls `Outer` and declares NOTHING.
        //
        // The site-6 solve runs in phase 3d, AFTER the SCC fixpoint, and an
        // in-module call charges its caller the callee's COMPUTED set — so before
        // PropagateInstantiatedCharges, `Outer` gained `cw` and `Top` saw the
        // pre-instantiation ∅ and compiled clean. Calor0418 masked it in the
        // default mode; under --permissive-effects the whole program printed
        // "Compilation successful", which is precisely the laundering rows exist
        // to close.
        //
        // Asserted on the `Top` diagnostic SPECIFICALLY, not on the whole
        // multiset (post-E4 `Run`'s `§C{g}` charges `e`, which `Run` declares).
        //
        // Discriminating revert: delete the PropagateInstantiatedCharges call and
        // `Top` goes silent.
        const string source = """
            §M{m001:M}
              §F{f001:Run:pub}<eff e> (Func<i32>:g §E{e}) -> i32
                §E{e}
                §R §C{g}
              §F{f002:Outer:pub} (Func<i32>:h §E{cw}) -> i32
                §E{cw}
                §R §C{Run} §A h §/C
              §F{f003:Top:pub} (Func<i32>:q §E{cw}) -> i32
                §E{}
                §R §C{Outer} §A q §/C
            """;

        var strict = TestHarness.Compile(source);
        Assert.Contains(strict.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
              && d.Message.Contains("Function 'Top' uses effect 'cw' but does not declare it"));

        // And under the flag, where the hole was loudest: --permissive-effects
        // demotes Calor0410 to a warning (that is 0410's own long-standing
        // policy), but the diagnostic must still FIRE. Before the fix this
        // compiled clean.
        var permissive = TestHarness.CompileWithEffects(
            source, policy: Calor.Compiler.Effects.UnknownCallPolicy.Permissive);
        Assert.Contains(permissive.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
              && d.Message.Contains("Function 'Top' uses effect 'cw' but does not declare it"));
    }

    [Fact]
    public void GenericInstantiation_ChargePropagatesThroughThreeHops()
    {
        // The same hole one level deeper, so what is pinned is the FIXPOINT and
        // not merely one extra propagation pass: Run → Outer → Top → Top2. A
        // single-pass fix reaches `Top` and leaves `Top2` silent.
        //
        // Discriminating revert: replace the worklist in
        // PropagateInstantiatedCharges with one sweep over the seed set and this
        // fails while the two-hop pin above still passes.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Run:pub}<eff e> (Func<i32>:g §E{e}) -> i32
                §E{e}
                §R §C{g}
              §F{f002:Outer:pub} (Func<i32>:h §E{cw}) -> i32
                §E{cw}
                §R §C{Run} §A h §/C
              §F{f003:Top:pub} (Func<i32>:q §E{cw}) -> i32
                §E{cw}
                §R §C{Outer} §A q §/C
              §F{f004:Top2:pub} (Func<i32>:r §E{cw}) -> i32
                §E{}
                §R §C{Top} §A r §/C
            """);

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
              && d.Message.Contains("Function 'Top2' uses effect 'cw' but does not declare it"));
    }

    [Fact]
    public void GenericInstantiation_AssumedRow_ReportsOnceAtTheHop()
    {
        // Review round 1, finding 2. §4.4: an Assumed source produces an Assumed
        // destination and every hop that carries an assumption reports it ONCE.
        // Site 6 was silent — a callee whose own effects could only be ASSUMED
        // (§CS interop) flowed through the solve and the reasons were charged but
        // never surfaced, so the caller inherited an assumption with no Calor0425
        // naming it. The Calor0419 on `Wrapped` is a different statement: it says
        // *that function* is assumed, not that this HOP carries the assumption.
        //
        // Discriminating revert: drop the IsAssumed arm in InstantiateAndCharge
        // and only the Calor0419 remains.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Run:pub}<eff e> (Func<i32>:g §E{e}) -> i32
                §E{e}
                §R INT:0
              §F{f002:Wrapped:pub} () -> i32
                §E{}
                §R §CS{ 1 + 1 }
              §F{f003:Caller:pub} () -> i32
                §E{}
                §R §C{Run} §A Wrapped §/C
            """);

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowUnknown
              && d.Message.Contains("instantiated effect row of 'Run' at this call site rests on an assumption")
              && d.Message.Contains("raw C# interop expression"));
    }

    [Fact]
    public void GenericInstantiation_BinderNoParameterMentions_IsCalor0425_EvenWithZeroArguments()
    {
        // Review round 1, finding 3. `CheckArgumentSite` returned early on an
        // empty argument list, which made InstantiateAndCharge's "no parameter of
        // 'X' binds it" arm unreachable from source: the ONE shape that reaches it
        // is a declaration binding a variable no parameter mentions, and such a
        // declaration is typically called with no arguments at all.
        //
        // Discriminating revert: move `if (arguments.Count == 0) return;` back
        // above the binder-count check and this goes silent.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:NoBinder:pub}<eff e> () -> i32
                §E{e}
                §R INT:0
              §F{f002:Use:pub} () -> i32
                §E{}
                §R §C{NoBinder} §/C
            """);

        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowUnknown
              && d.Message.Contains("Effect variable 'e' of 'NoBinder' instantiates to Unknown")
              && d.Message.Contains("no parameter of 'NoBinder' binds it"));
    }

    [Fact]
    public void InterfaceVariance_OrdinalMismatch_NamesTheBinderByPosition()
    {
        // Review round 1, finding 4. The interface binds <eff e, eff f> and uses
        // `f` (ordinal 1); the implementation binds <eff f, eff e> and uses `f`
        // (ordinal 0). `fits` correctly rejects it — position is the identity —
        // but the message was computed by NAME, so the extras list came out EMPTY
        // and the text read "row f does not fit ... row f", which tells the author
        // nothing. The position must appear.
        //
        // Discriminating revert: compute the extras from the names again and both
        // assertions below fail.
        var result = TestHarness.Compile("""
            §M{m001:M}
              §IFACE{i001:IThing}
                §MT{mt001:Handle}<eff e, eff f> (Func<i32>:next §E{f}) -> i32
                  §E{f}

              §CL{c001:Impl:pub}
                §IMPL{IThing}
                §MT{mt002:Handle:pub}<eff f, eff e> (Func<i32>:next §E{f}) -> i32
                  §E{f}
                  §R INT:0
            """);

        var reported = Assert.Single(result.Diagnostics,
            d => d.Code == DiagnosticCode.InterfaceEffectVariance);
        Assert.Contains("[f (binder #0)]", reported.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Effect variables are matched BY POSITION in the declaration's 'eff' list, not by name.",
            reported.Message,
            StringComparison.Ordinal);
    }

    // ================================================================ P11 ====

    [Fact]
    public void NeverWaived_DoesNotFit_AtEveryMonomorphicSite()
    {
        // §4.5 — "A DoesNotFit verdict is never waived, at any of the six sites,
        // by any flag." Every one of the three codes that spells DoesNotFit is
        // still an ERROR under --permissive-effects. Discriminating revert:
        // route Calor0424 through the policy check, or restore the
        // varianceSeverity demotion at EffectEnforcementPass.cs:517-519.
        var assignment = Permissive("""
            §M{m001:M}
              §F{f001:Main:pub} (Func<i32,i32>:src §E{cw}) -> i32
                §E{cw}
                §B{f:Func<i32,i32>} §E{} src
                §R INT:1
            """);
        Assert.Contains(assignment.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.EffectRowMismatch);

        var argument = Permissive(ArgumentSite("§E{}"));
        Assert.Contains(argument.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.EffectRowMismatch);

        var @return = Permissive("""
            §M{m001:M}
              §F{f001:Pick:pub} (Func<i32,i32>:src §E{cw}) -> Func<i32,i32> §E{}
                §E{}
                §R src
            """);
        Assert.Contains(@return.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.EffectRowMismatch);

        // Y8a's flip. On 0.14 this compiled with a WARNING under the flag; §4.5
        // makes --permissive-effects strictly less powerful and it is now an
        // error. No test asserted the demotion, and no committed .calr depends
        // on it.
        var @override = Permissive("""
            §M{m001:M}
              §CL{c001:Base:pub}
                §MT{mt001:Render:pub:virt} () -> void
                  §E{}
              §CL{c002:Derived:Base:pub}
                §MT{mt002:Render:pub:over} () -> void
                  §E{cw}
                  §P "laundered"
            """);
        Assert.Contains(@override.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(@override.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.OverrideEffectVariance);

        var impl = Permissive("""
            §M{m001:M}
              §IFACE{i001:IRenderer}
                §MT{m002:Render} () -> void
                  §E{}
              §CL{c001:ConsoleRenderer:pub}
                §IMPL{IRenderer}
                §MT{mt001:Render:pub} () -> void
                  §E{cw}
                  §P "rendering"
            """);
        Assert.Contains(impl.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.InterfaceEffectVariance);
        Assert.DoesNotContain(impl.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.InterfaceEffectVariance);
    }

    [Fact]
    public void PermissiveWaivesUnknown_BothPolarities()
    {
        // The one job --permissive-effects keeps in 0.15, asserted on BOTH
        // sides: the diagnostic is there without the flag and gone with it. A
        // waiver for "we do not know" is honest; a waiver for "we know it is
        // wrong" is not, and the test above is that half.
        const string source = """
            §M{m001:M}
              §F{f001:Main:pub} (Func<i32,i32>:src) -> i32
                §E{cw}
                §B{f:Func<i32,i32>} §E{} src
                §R INT:1
            """;

        Assert.Contains(TestHarness.Compile(source).Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(Permissive(source).Diagnostics,
            d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void StrictEffectsRaisesCalor0425ToAnError()
    {
        // §6.1's severity line, the other end of the same dial.
        var options = new CompilationOptions
        {
            EnforceEffects = true,
            StrictEffects = true,
            UnsafeTranspileOnly = true,
        };
        var result = TestHarness.Compile("""
            §M{m001:M}
              §F{f001:Main:pub} (Func<i32,i32>:src) -> i32
                §E{cw}
                §B{f:Func<i32,i32>} §E{} src
                §R INT:1
            """, options);

        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    // ================================================================ P22 ====

    [Fact]
    public void MessageTexts_Calor0424_IsTheDesignDocSampleVerbatim()
    {
        // §6.4's first sample, asserted as the FULL clause rather than a
        // Contains("transform"). Discriminating revert: reword any part of it.
        var result = TestHarness.Compile(ArgumentSite("§E{}"));

        var diagnostic = Assert.Single(result.Diagnostics.Errors
            .Where(d => d.Code == DiagnosticCode.EffectRowMismatch));

        Assert.Equal(
            "Argument 'Shout' has effect row cw, which does not fit parameter 'transform' of "
            + "'Apply' (declared row: [pure]). Extra effect(s): cw. Widen 'transform' to §E{cw}, "
            + "or pass a function whose row fits. An effect row that does not fit is never waived.",
            diagnostic.Message);
    }

    [Fact]
    public void MessageTexts_Calor0425_NamesTheRowlessPositionAndTheWaiver()
    {
        // §6.4's second sample. The design doc's wording says "Add §E{…} on the
        // same line as the type … or compile with --permissive-effects", and
        // names what an Unknown row costs the enclosing declaration.
        var result = TestHarness.Compile(ArgumentSite(""));

        var diagnostic = Assert.Single(result.Diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.EffectRowUnknown));

        Assert.Equal(
            "Parameter 'transform' of 'Apply' is function-typed with no effect row, so its effects "
            + "are Unknown. Add §E{…} on the same line as the type to state what callers may pass, "
            + "or compile with --permissive-effects. Invoking a value whose row is Unknown charges "
            + "Unknown to 'Apply'.",
            diagnostic.Message);
    }

    private static Calor.Compiler.CompilationResult Permissive(string source) =>
        TestHarness.CompileWithEffects(source, enforceEffects: true,
            policy: UnknownCallPolicy.Permissive);

    // ======================================================================
    // v0.15 E4 — Calor0418 replaced by fits-at-invocation (roadmap §4.2 E4,
    // design-doc §10.1, §13.1). Everything below sits past every line
    // facts.py probes, on purpose.
    // ======================================================================

    /// <summary>The A3-callback shape (§10.1's worked example, X13) with the
    /// invoking method's own row as <paramref name="bumpRow"/>.</summary>
    private static string CallbackField(string bumpRow) => $$"""
        §M{m001:CallbackAfter}
          §CL{c001:Counter:pub}
            §FLD{Action<i32>:onChange:pri} §E{cw}
            §MT{mt001:Bump:pub} (i32:n) -> void
              {{bumpRow}}
              §C{onChange} §A n §/C
        """;

    [Fact]
    public void Invocation_RowedValue_FitsAndChargesTheRow_PositiveControl()
    {
        // The positive control every DelegateInvocation_* rewrite needs: a
        // function-typed value WITH a row, invoked by a function that declares
        // that row, compiles with zero effect-family diagnostics. Pre-E4 this was
        // Calor0418 (A-1.11.1's A3-callback baseline: 1x at (6,7)).
        var result = TestHarness.Compile(CallbackField("§E{cw}"));

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == DiagnosticCode.DelegateInvocation
            || d.Code == DiagnosticCode.EffectRowUnknown
            || d.Code == DiagnosticCode.EffectRowMismatch
            || d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void MessageTexts_Calor0410_InvocationProvenance_IsTheDesignDocSample()
    {
        // P22, §10.1's string, by FULL equality. The doc's first draft wrote
        // `(row: [cw])`; the emitter spells a concrete row bare (§8.3 / §6.4's F6
        // correction), and the doc was corrected to the emitter, not the reverse.
        // The row IS charged and the caller's under-declaration is Calor0410 —
        // never Calor0424, which has no invocation cell in §6.2.
        var result = TestHarness.Compile(CallbackField("§E{}"));

        var diagnostic = Assert.Single(result.Diagnostics.Errors
            .Where(d => d.Code == DiagnosticCode.ForbiddenEffect));
        Assert.Equal(
            "Function 'Bump' uses effect 'cw' but does not declare it\n"
            + "  Effect row: charged by invoking 'onChange' (row: cw)",
            diagnostic.Message);
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == DiagnosticCode.EffectRowMismatch
            || d.Code == DiagnosticCode.EffectRowUnknown
            || d.Code == DiagnosticCode.DelegateInvocation);
    }

    [Fact]
    public void MessageTexts_Calor0425_AtInvocation_NamesTheValueTheCauseAndTheWaiver()
    {
        // E4's new string (recorded in design-doc §10.1 alongside the provenance
        // clause), by FULL equality. X9c's shape with the row deleted — PP-E1's
        // L7 class.
        var result = TestHarness.Compile("""
            §M{m001:X9}
              §F{f001:Apply:pub} (Func<i32,i32>:transform, i32:value) -> i32
                §E{cw}
                §R §C{transform} §A value §/C
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Warnings
            .Where(d => d.Code == DiagnosticCode.EffectRowUnknown));
        Assert.Equal(
            "Invocation of 'transform' in 'Apply' cannot be charged: its effect row is Unknown "
            + "(parameter 'transform' of 'Apply' (type 'Func<i32,i32>') carries no effect row). "
            + "Add §E{…} on the same line as the type to state what 'transform' may do, or "
            + "compile with --permissive-effects. 'Apply' is charged Unknown.",
            diagnostic.Message);
        // Fail-closed: the Unknown charge is the same one an unknown external
        // call gets today, so the declaration draws Calor0410 'unknown'.
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("'unknown'"));
    }

    [Fact]
    public void Invocation_LambdaBoundLocal_NarrowedDeclaration_IsCalor0410()
    {
        // §13.1's second half of the Y9a rewrite: the inferred row of a
        // lambda-bound local is CHARGED, so a declaration narrower than it is
        // Calor0410 — with §10.1's provenance naming the local.
        var result = TestHarness.Compile("""
            §M{m001:Y9}
              §F{f001:Narrow:pub} () -> void
                §E{}
                §B{g} §LAM{lam2} §P "hi" §/LAM{lam2}
                §C{g} §/C
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Errors
            .Where(d => d.Code == DiagnosticCode.ForbiddenEffect));
        Assert.Contains("uses effect 'cw'", diagnostic.Message);
        Assert.Contains("charged by invoking 'g' (row: cw)", diagnostic.Message);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void Invocation_AssumedRow_ChargesTheSetAndReportsTheAssumptionOnce()
    {
        // §4.3's Assumed cell read at an invocation: ρ_body of the lambda is
        // Assumed (its body is raw interop), so the invocation charges the set
        // and reports ONE Calor0425 carrying the reason.
        var result = TestHarness.Compile("""
            §M{m001:Asm}
              §F{f001:Go:pub} () -> i32
                §E{}
                §B{h} §LAM{l1} §R §CS{Foo.Bar()} §/LAM{l1}
                §R §C{h} §/C
            """);

        var reported = Assert.Single(result.Diagnostics
            .Where(d => d.Code == DiagnosticCode.EffectRowUnknown));
        Assert.Equal(
            "Invocation of 'h' in 'Go' is charged [assumed: pure] under an assumption: contains a "
            + "raw C# interop expression (§CS). The row is charged as an assumption, not a proof.",
            reported.Message);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DelegateInvocation);
    }

    [Fact]
    public void Invocation_PolymorphicRow_ChargesTheVariable_WhichTheDeclarationBinds()
    {
        // A3-middleware's `RunTwice`: `g §E{e}` invoked inside a declaration that
        // binds `e`. The row's variable part is the caller's own binder, its
        // concrete part is empty, and nothing is reported — this is what makes
        // the four A3 fixtures exit 0 with zero diagnostics (A-1.11).
        var result = TestHarness.Compile("""
            §M{m001:MiddlewareAfter}
              §F{f001:RunTwice:pub}<eff e> (Func<i32>:g §E{e}) -> i32
                §E{e}
                §B{first:i32} §C{g}
                §B{second:i32} §C{g}
                §R (+ first second)
            """);

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == DiagnosticCode.DelegateInvocation
            || d.Code == DiagnosticCode.EffectRowUnknown
            || d.Code == DiagnosticCode.EffectRowMismatch);
    }

    [Fact]
    public void Invocation_ProvablyNonFunctionValue_IsTheResidualCalor0418_NeverWaived()
    {
        // The ONE shape Calor0418 is kept for: the invoked value's type is
        // provably not a function type, so there is no row to read. An error
        // under the default policy AND under --permissive-effects — this is "we
        // know it is wrong", not "we cannot tell" (§4.5), and the binder does
        // not catch it (measured: `§C{x}` on an i32 parameter binds clean).
        const string source = """
            §M{m001:P1}
              §F{f001:Go:pub} (i32:x) -> i32
                §E{}
                §R §C{x} §/C
            """;

        foreach (var result in new[] { TestHarness.Compile(source), Permissive(source) })
        {
            var diagnostic = Assert.Single(result.Diagnostics.Errors
                .Where(d => d.Code == DiagnosticCode.DelegateInvocation));
            Assert.Equal(
                "'x' has type 'i32', which is not a function type, so invoking it cannot be "
                + "charged to any effect row. Calor0418 is reported only for a value that is "
                + "provably not a function; a function-typed value is charged through its "
                + "effect row.",
                diagnostic.Message);
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        }
    }

    [Fact]
    public void Invocation_NotProvablyFunctionTyped_IsUnknown_NotTheResidual()
    {
        // The other side of the residual's boundary: an external nominal type
        // the binder does not know is not provably EITHER way, so the value is
        // Unknown (Calor0425, fails closed) rather than Calor0418. Pre-E4 this
        // drew Calor0418 "value 'cb' (declared type 'MyExternalDelegate')".
        var result = TestHarness.Compile("""
            §M{m001:P2}
              §F{f001:Go:pub} (MyExternalDelegate:cb) -> void
                §E{}
                §C{cb} §/C
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DelegateInvocation);
        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.EffectRowUnknown && d.Message.Contains("'cb'"));
    }

    [Fact]
    public void AuthorMayAssertARowOverAnUnknown_OneCalor0425AtTheHop_TheDeclaredRowIsCharged()
    {
        // §4.5's residual (E3a review F15), answered by E4 in the design doc:
        // YES — an author may assert a row over an Unknown source. The
        // assertion costs exactly one Calor0425 at the hop; from there the
        // DECLARED row is the value's row (§4.4), and invoking it charges that
        // row, with provenance, and reports nothing further.
        const string source = """
            §M{m001:F15}
              §F{f001:Main:pub} (Func<i32,i32>:opaque) -> i32
                §E{}
                §B{g:Func<i32,i32>} §E{cw} opaque
                §R §C{g} §A INT:1 §/C
            """;

        var strict = TestHarness.Compile(source);
        var hop = Assert.Single(strict.Diagnostics
            .Where(d => d.Code == DiagnosticCode.EffectRowUnknown));
        Assert.StartsWith("Initializer of binding 'g' has effect row [unknown]", hop.Message);
        var charged = Assert.Single(strict.Diagnostics.Errors
            .Where(d => d.Code == DiagnosticCode.ForbiddenEffect));
        Assert.Equal(
            "Function 'Main' uses effect 'cw' but does not declare it\n"
            + "  Effect row: charged by invoking 'g' (row: cw)",
            charged.Message);

        // Under the waiver the single Calor0425 is silenced — §4.5's consequence
        // 2, accepted with the answer — but the CHARGE is not a "cannot tell"
        // and survives: Calor0410 still fires (demoted, as every 0410 is under
        // the flag). The assertion is waivable; the row asserted is not.
        var permissive = Permissive(source);
        Assert.DoesNotContain(permissive.Diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.Contains(permissive.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("charged by invoking 'g'"));
    }

    // ---------------------------------------------------- E4 review round 1 ----

    /// <summary>F1's two shapes: a mutable bound to a pure lambda and re-bound to
    /// an impure value, invoked under <c>§E{}</c>. <paramref name="declaredType"/>
    /// is empty for the UNTYPED mutable, which pre-fix was silently accepted
    /// (site 1 never put it in scope, and the invocation charged the initializer's
    /// row); site 1 now fires on it because a lambda initializer is function-valued
    /// by construction, so the re-binding is Calor0424 exactly as on the typed one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(":Func<i32,i32>")]
    public void Invocation_MutableReboundToImpure_IsCaughtAtSite1_TypedAndUntyped(string declaredType)
    {
        var result = TestHarness.Compile($$"""
            §M{m001:F1}
              §F{f001:Impure:pub} (i32:x) -> i32
                §E{cw}
                §P x
                §R x
              §F{f002:Go:pub} () -> i32
                §E{}
                §B{~f{{declaredType}}} §LAM{l1:x:i32} (+ x 1) §/LAM{l1}
                §ASSIGN f Impure
                §R §C{f} §A INT:1 §/C
            """);

        Assert.True(result.HasErrors, "re-binding a function-valued mutable to an impure value under §E{} must fail");
        var mismatch = Assert.Single(result.Diagnostics.Errors
            .Where(d => d.Code == DiagnosticCode.EffectRowMismatch));
        Assert.Contains("Value assigned to 'f' has effect row cw", mismatch.Message);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DelegateInvocation);
    }

    /// <summary>F2: two sibling branches each bind <c>f</c>; the invocation must
    /// charge the row of the <c>§B</c> the binder resolved it to, not the first
    /// of that name in lexical order. Both directions — the impure one invoked
    /// (Calor0410, no false negative) and the pure one invoked (clean, no false
    /// positive). The sources are PARAMETERS so that no lambda-creation charge
    /// (the enclosing function is charged a lambda's body at creation, a
    /// pre-existing rule) can mask the answer.</summary>
    [Theory]
    [InlineData("§E{} pure", "§E{cw} impure", true)]
    [InlineData("§E{cw} impure", "§E{} pure", false)]
    public void Invocation_SiblingBranchSameName_ChargesTheBoundDeclaration(
        string firstBinding, string secondBinding, bool expectCalor0410)
    {
        var result = TestHarness.Compile($$"""
            §M{m001:F2}
              §F{f003:Go:pub} (bool:b, Func<i32,i32>:pure §E{}, Func<i32,i32>:impure §E{cw}) -> i32
                §E{}
                §IF{if1} b
                  §B{f:Func<i32,i32>} {{firstBinding}}
                  §R INT:0
                §EL
                  §B{f:Func<i32,i32>} {{secondBinding}}
                  §R §C{f} §A INT:1 §/C
            """);

        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == DiagnosticCode.DelegateInvocation
            || d.Code == DiagnosticCode.EffectRowUnknown
            || d.Code == DiagnosticCode.EffectRowMismatch);
        if (expectCalor0410)
        {
            var charged = Assert.Single(result.Diagnostics.Errors
                .Where(d => d.Code == DiagnosticCode.ForbiddenEffect));
            Assert.Contains("charged by invoking 'f' (row: cw)", charged.Message);
        }
        else
        {
            Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        }
    }

    [Fact]
    public void Invocation_ShadowingInANestedScope_IsRejectedByTheBinder()
    {
        // F2's third shape. A `§B{f}` shadowing a parameter `f` in a nested
        // scope never reaches the effect pass: the binder rejects it (Calor0255)
        // and Program.Compile stops on binder errors, which is fail-closed by
        // construction. Pinned so the day the binder admits shadowing, this
        // test says the invocation charge must be re-checked for it.
        var result = TestHarness.Compile("""
            §M{m001:F2c}
              §F{f003:Go:pub} (Func<i32,i32>:f §E{}) -> i32
                §E{}
                §IF{if1} true
                  §B{f:Func<i32,i32>} §E{cw} §LAM{l2:x:i32} §P x §R x §/LAM{l2}
                  §R §C{f} §A INT:1 §/C
                §R §C{f} §A INT:2 §/C
            """);

        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.BindShadowsEnclosingScope);
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == DiagnosticCode.ForbiddenEffect
            || d.Code == DiagnosticCode.EffectRowUnknown
            || d.Code == DiagnosticCode.DelegateInvocation);
    }

    [Fact]
    public void PolymorphicSource_IntoRowlessDestination_IsCannotTell_NotDoesNotFit()
    {
        // F6 (pre-existing since E3b, made visible by PP-E1's L7-MID mutant):
        // `next §E{e}` passed to a parameter with NO row. §4.3 says Unknown never
        // yields DoesNotFit on either side, so this is Calor0425 (the row-less
        // destination message) and never a Calor0424 reading "declared row:
        // [unknown]" with a leaked "(binder #0)".
        var result = TestHarness.Compile("""
            §M{m001:F6}
              §F{f001:RunTwice:pub} (Func<i32>:g) -> i32
                §E{}
                §R INT:0
              §F{f002:Handle:pub}<eff e> (Func<i32>:next §E{e}) -> i32
                §E{e}
                §R §C{RunTwice} §A next §/C
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.EffectRowMismatch);
        Assert.Contains(result.Diagnostics.Warnings, d =>
            d.Code == DiagnosticCode.EffectRowUnknown
            && d.Message.StartsWith("Parameter 'g' of 'RunTwice' is function-typed with no effect row", StringComparison.Ordinal));
    }
}
