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
///   D-W2.4 KnownPureMethodNames mutator purge (fail-closed unknown-call path)
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
    public void DelegateInvocation_FunctionTypedParameter_IsError()
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

        Assert.True(result.HasErrors, "Invoking a function-typed parameter must fail under enforcement");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.DelegateInvocation && d.Message.Contains("transform"));
    }

    [Fact]
    public void DelegateInvocation_LambdaBoundLocal_IsError()
    {
        var source = @"
§M{m001:Test}
  §F{f001:UseLambda:pub}
      §O{i32}
      §B{f} §LAM{lam1:x:i32} (+ x 1) §/LAM{lam1}
      §R §C{f} §A INT:1 §/C
";
        var result = TestHarness.Compile(source);

        Assert.True(result.HasErrors, "Invoking a lambda-bound local must fail under enforcement");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.DelegateInvocation && d.Message.Contains("'f'"));
    }

    [Fact]
    public void DelegateInvocation_UnderPermissiveEffects_IsWaivedToWarning()
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

        Assert.DoesNotContain(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.DelegateInvocation);
        Assert.Contains(result.Diagnostics.Warnings,
            d => d.Code == DiagnosticCode.DelegateInvocation && d.Message.Contains("transform"));
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
        // checked, so the override is surfaced through the assumption channel.
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
            d => d.Code == DiagnosticCode.AssumedEffects && d.Message.Contains("external base"));
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
    // D-W2.4 — KnownPureMethodNames mutator purge
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
    public void KeptPureNames_LinqAndStringOps_StillPure()
    {
        // Names that survived the audit (genuinely pure across common BCL
        // receivers) still resolve as pure through the name fallback.
        var source = @"
§M{m001:Test}
  §F{f001:Query:pub}
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
    public void C2_DecoyNamedDelegateParameter_ShadowsFunction_IsError()
    {
        // Review C2: a Func parameter named like a pure module function must be
        // resolved as the VALUE (matching C# scoping and emission), yielding
        // Calor0418 — not silently charged as the shadowed pure function.
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

        Assert.True(result.HasErrors, "Decoy-named delegate invocation must fail");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.DelegateInvocation && d.Message.Contains("'Helper'"));
        // C4 companion: Main passes the impure method group 'Loud' — charged at
        // the passing site, so §E{} on Main is a Calor0410.
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
        // from an external base is surfaced via the Calor0419 assumption channel.
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
            d => d.Code == DiagnosticCode.AssumedEffects
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
        // Review C4 (value arm): a function-typed VALUE passed to a known-pure
        // higher-order name (Select) is surfaced as a Calor0419 assumption —
        // the BCL callee may invoke it invisibly.
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
      §E{}
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
    public void M1_ExpressionCallSpelling_DelegateValue_IsError()
    {
        // Review M1: `§C f §A x §/C` (expression-call spelling) is the same
        // delegate invocation as `§C{f}` and gets the same Calor0418 error.
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

        Assert.True(result.HasErrors, "Expression-call delegate invocation must be an error");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.DelegateInvocation && d.Message.Contains("'f'"));
    }

    [Fact]
    public void M1_ReturnedDelegateInvocation_IsError()
    {
        // Review M1: invoking the RESULT of a call (`GetF()()`) invokes a
        // delegate value — Calor0418.
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

        Assert.True(result.HasErrors, "Returned-delegate invocation must be an error");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.DelegateInvocation && d.Message.Contains("returned delegate"));
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
}
