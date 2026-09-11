using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class BindingDiagnosticPolicyTests
{
    public static IEnumerable<object[]> NullableBoundaries()
    {
        yield return new object[] { DiagnosticCode.NullableToNonNullableBinding,
            BindingReceivingBoundary.Initializer,
            "§B{x:str} §C{System.Environment.GetEnvironmentVariable} §A \"CALOR_ROUTING_UNSET\" §/C", "void" };
        yield return new object[] { DiagnosticCode.NullableReturnFromNonNullable,
            BindingReceivingBoundary.NativeReturn,
            "§R §C{System.Environment.GetEnvironmentVariable} §A \"CALOR_ROUTING_UNSET\" §/C", "str" };
        yield return new object[] { DiagnosticCode.NullableArgumentToNonNullableParameter,
            BindingReceivingBoundary.MethodArgument,
            "§R §C{System.Int32.Parse} §A §C{System.Environment.GetEnvironmentVariable} §A \"CALOR_ROUTING_UNSET\" §/C §/C", "i32" };
    }

    [Theory]
    [MemberData(nameof(NullableBoundaries))]
    public void NullableErrors_CarryStructuredContext_WithoutActivation(
        string code, BindingReceivingBoundary boundary, string body, string returnType)
    {
        var source = $"§M{{m1:Routing}}\n  §F{{f1:Probe:pub}} () -> {returnType}\n    §E{{env}}\n    {body}\n";
        var bag = Bind(source);
        var diagnostic = Assert.Single(bag.Errors);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(new BindingDiagnosticContext(boundary, BindingReceivingShape.ScalarString), diagnostic.BindingContext);
        Assert.True(BindingDiagnosticPolicy.IsAnalysisOnly(diagnostic));
        Assert.False(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
        foreach (var typeChecking in new[] { true, false })
        foreach (var transpile in new[] { true, false })
        {
            var result = Program.Compile(source, "routing.calr", new CompilationOptions
            {
                EnableTypeChecking = typeChecking,
                UnsafeTranspileOnly = transpile
            });
            Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics));
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == code);
        }
    }

    [Fact]
    public void EveryCatalogEntry_HasAnExplicitDispositionReasonAndOwner()
    {
        Assert.Equal(19, BindingDiagnosticPolicy.Catalog.Count);
        foreach (var (code, rule) in BindingDiagnosticPolicy.Catalog)
        {
            Assert.Equal(code, rule.Code);
            Assert.False(string.IsNullOrWhiteSpace(rule.Justification));
            Assert.True(rule.OwningIssue > 0);
        }
        foreach (var rule in BindingDiagnosticPolicy.ReceivingRules)
        {
            Assert.Equal(BindingDiagnosticDisposition.AnalysisOnly, rule.Policy.Disposition);
            Assert.False(string.IsNullOrWhiteSpace(rule.Policy.Justification));
            Assert.True(rule.Policy.OwningIssue > 0);
        }
        Assert.Throws<InvalidOperationException>(() => BindingDiagnosticPolicy.GetRule("CalorUnclassified"));
    }

    [Theory]
    [InlineData("str")]
    [InlineData("string")]
    [InlineData("STRING")]
    public void ReceivingShape_UsesTypeIdentity_NotDiagnosticProse(string alias)
    {
        var context = BindingDiagnosticContext.For(
            BindingReceivingBoundary.Initializer, new NominalBoundType(alias));
        Assert.Equal(BindingReceivingShape.ScalarString, context.Shape);
        var diagnostic = new Diagnostic(DiagnosticCode.NullableToNonNullableBinding,
            "This message intentionally says array and Foo, not the receiving type.",
            new TextSpan(0, 1, 1, 1)) { BindingContext = context };
        Assert.Equal(1385, BindingDiagnosticPolicy.GetRule(diagnostic.Code, context).OwningIssue);
        var nominal = BindingDiagnosticContext.For(BindingReceivingBoundary.Initializer, new NominalBoundType("Foo"));
        Assert.Equal(BindingReceivingShape.Nominal, nominal.Shape);
        Assert.Equal(1402, BindingDiagnosticPolicy.GetRule(diagnostic.Code, nominal).OwningIssue);
    }

    [Fact]
    public void SharedApiContext_DoesNotReuseSuccessOrTurnEarlyTypingIntoBinderOwnership()
    {
        using var context = new CompilationContext { SharedEffectResolver = new Effects.EffectResolver() };
        const string safe = "§M{m1:Routing}\n  §F{f1:Probe:pub} () -> str\n    §R \"safe\"\n";
        var duplicate = safe + "  §F{f2:Probe:pub} () -> str\n    §R \"safe\"\n";
        foreach (var source in new[] { safe, duplicate, safe })
        {
            var ordinary = Program.Compile(source, "routing.calr");
            var shared = Program.Compile(source, "routing.calr", new CompilationOptions { Context = context });
            Assert.Equal(ordinary.Diagnostics.Select(d => (d.Code, d.Span, d.Severity)),
                shared.Diagnostics.Select(d => (d.Code, d.Span, d.Severity)));
            Assert.Equal(source == duplicate, shared.HasErrors);
        }
        // Nullable-to-nonnullable assignment retains its transitional typing rejection
        // until #1385. The old nullable-literal false positive was repaired by #1397.
        const string early = "§M{m1:Routing}\n  §F{f1:Probe:pub} () -> void\n    §B{x:?str} null\n    §B{y:str} x\n";
        var rejected = Program.Compile(early, "routing.calr", new CompilationOptions { Context = context });
        Assert.Contains(rejected.Diagnostics, d => d.Code == DiagnosticCode.TypeMismatch && d.IsError);
        Assert.All(rejected.Diagnostics, d => Assert.Null(d.BindingContext));
        var optOut = Program.Compile(early, "routing.calr", new CompilationOptions
        {
            Context = context, EnableTypeChecking = false
        });
        Assert.False(optOut.HasErrors);
    }

    [Fact]
    public void BindingScope_DoesNotRelabelOtherPasses_AndPreservesFixes()
    {
        var bag = new DiagnosticBag();
        var span = new TextSpan(0, 1, 1, 1);
        bag.ReportError(span, DiagnosticCode.UndefinedReference, "before binding");
        Bind("§M{m1:Routing}\n  §F{f1:Probe:pub} () -> i32\n    §R absent\n", bag);
        bag.ReportError(span, DiagnosticCode.UndefinedReference, "after binding");
        Assert.Null(bag.First().BindingContext);
        Assert.Null(bag.Last().BindingContext);
        Assert.NotNull(bag.ElementAt(1).BindingContext);
        Assert.True(bag.HasErrors);

        var duplicates = Bind("§M{m1:Routing}\n  §F{f1:Probe:pub} (i32:x,i32:x) -> i32\n    §R x\n");
        Assert.Contains(duplicates.DiagnosticsWithFixes, d => d.BindingContext is not null);
        Assert.All(duplicates.Errors, d => Assert.True(BindingDiagnosticPolicy.IsCompilationError(d)));
    }

    [Fact]
    public void ActiveErrors_PropagateOnce_WithoutChangingTheirSpanSeverityOrMessage()
    {
        var bag = Bind("§M{m1:Routing}\n  §F{f1:Probe:pub} () -> i32\n    §R 1\n  §F{f2:Probe:pub} () -> i32\n    §R 2\n");
        var original = Assert.Single(bag.Errors);
        Assert.Equal(DiagnosticCode.DuplicateFunctionSignature, original.Code);
        foreach (var rule in BindingDiagnosticPolicy.Catalog.Values.Where(rule =>
                     rule.Disposition == BindingDiagnosticDisposition.AnalysisOnly))
            bag.Add(new Diagnostic(rule.Code, "analysis, never forwarded", original.Span)
                { BindingContext = BindingDiagnosticContext.General });
        var destination = new DiagnosticBag();
        BindingDiagnosticPolicy.PropagateCompilationErrors(bag, destination);
        BindingDiagnosticPolicy.PropagateCompilationErrors(bag, destination);
        Assert.Same(original, Assert.Single(destination));
    }

    private static DiagnosticBag Bind(string source, DiagnosticBag? diagnostics = null)
    {
        diagnostics ??= new DiagnosticBag();
        var parse = new DiagnosticBag();
        var module = new Parser(new Lexer(source, parse).TokenizeAllForParser(), parse).Parse();
        Assert.False(parse.HasErrors, string.Join("; ", parse));
        new Binder(diagnostics, "routing.calr").Bind(module);
        return diagnostics;
    }
}
