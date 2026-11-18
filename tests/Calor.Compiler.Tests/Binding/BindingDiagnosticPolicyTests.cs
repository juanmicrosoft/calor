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
        // Keep native copies of the shared cases while other legacy consumers migrate separately.
        yield return [DiagnosticCode.NullableToNonNullableBinding, BindingReceivingBoundary.Initializer,
            "let string:x = System.Environment.GetEnvironmentVariable(\"CALOR_ROUTING_UNSET\")", "void"];
        yield return [DiagnosticCode.NullableReturnFromNonNullable, BindingReceivingBoundary.NativeReturn,
            "return System.Environment.GetEnvironmentVariable(\"CALOR_ROUTING_UNSET\")", "string"];
        yield return [DiagnosticCode.NullableArgumentToNonNullableParameter, BindingReceivingBoundary.MethodArgument,
            "return System.Int32.Parse(System.Environment.GetEnvironmentVariable(\"CALOR_ROUTING_UNSET\"))", "int"];
    }

    [Theory]
    [MemberData(nameof(NullableBoundaries))]
    public void ScalarNullableErrors_ActivateAcrossCompilerModes(
        string code, BindingReceivingBoundary boundary, string body, string returnType)
    {
        var source = $"module Routing\n  func Probe () → {returnType}\n    ! env\n    {body}\n";
        var bag = Bind(source);
        var diagnostic = Assert.Single(bag.Errors);
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(new BindingDiagnosticContext(boundary, BindingReceivingShape.ScalarString), diagnostic.BindingContext);
        Assert.False(BindingDiagnosticPolicy.IsAnalysisOnly(diagnostic));
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
        foreach (var typeChecking in new[] { true, false })
        foreach (var transpile in new[] { true, false })
        {
            var result = Program.Compile(source, "routing.calr", new CompilationOptions
            {
                EnableTypeChecking = typeChecking,
                UnsafeTranspileOnly = transpile
            });
            Assert.True(result.HasErrors);
            var propagated = Assert.Single(result.Diagnostics.Where(d => d.Code == code));
            Assert.Equal(DiagnosticSeverity.Error, propagated.Severity);
            Assert.Equal(diagnostic.Span, propagated.Span);
        }
    }

    [Fact]
    public void EveryCatalogEntry_HasAnExplicitDispositionReasonAndOwner()
    {
        // Native metadata calls retain the array-to-collection error; 0.30 adds Calor0407 (D2).
        Assert.Equal(23, BindingDiagnosticPolicy.Catalog.Count);
        Assert.DoesNotContain(DiagnosticCode.ReferenceOptionMismatch, BindingDiagnosticPolicy.Catalog.Keys);
        foreach (var (code, rule) in BindingDiagnosticPolicy.Catalog)
        {
            Assert.Equal(code, rule.Code);
            Assert.False(string.IsNullOrWhiteSpace(rule.Justification));
            Assert.True(rule.OwningIssue > 0);
        }
        foreach (var rule in BindingDiagnosticPolicy.ReceivingRules)
        {
            Assert.Equal(rule.Context.ReplacesNativeOverloadError
                || rule.Context.Shape is BindingReceivingShape.ScalarString
                    or BindingReceivingShape.Array
                    or BindingReceivingShape.Generic
                    or BindingReceivingShape.Nominal
                ? BindingDiagnosticDisposition.CompilationError
                : BindingDiagnosticDisposition.AnalysisOnly, rule.Policy.Disposition);
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
        Assert.Equal(BindingDiagnosticDisposition.CompilationError,
            BindingDiagnosticPolicy.GetRule(diagnostic.Code, context).Disposition);
        var nominal = BindingDiagnosticContext.For(BindingReceivingBoundary.Initializer, new NominalBoundType("Foo"));
        Assert.Equal(BindingReceivingShape.Nominal, nominal.Shape);
        Assert.Equal(1402, BindingDiagnosticPolicy.GetRule(diagnostic.Code, nominal).OwningIssue);
        Assert.Equal(BindingDiagnosticDisposition.CompilationError,
            BindingDiagnosticPolicy.GetRule(diagnostic.Code, nominal).Disposition);
    }

    [Fact]
    public void SharedApiContext_DoesNotReuseSuccessOrTurnEarlyTypingIntoBinderOwnership()
    {
        using var context = new CompilationContext { SharedEffectResolver = new Effects.EffectResolver() };
        const string safe = "module Routing\n  func Probe () → string\n    !\n    return \"safe\"\n";
        var duplicate = safe + "  func Probe () → string\n    !\n    return \"safe\"\n";
        foreach (var source in new[] { safe, duplicate, safe })
        {
            var ordinary = Program.Compile(source, "routing.calr");
            var shared = Program.Compile(source, "routing.calr", new CompilationOptions { Context = context });
            Assert.Equal(ordinary.Diagnostics.Select(d => (d.Code, d.Span, d.Severity)),
                shared.Diagnostics.Select(d => (d.Code, d.Span, d.Severity)));
            Assert.Equal(source == duplicate, shared.HasErrors);
        }
        // Scalar nullable-to-nonnullable assignment is owned by the binder
        // even when the optional type checker is enabled.
        const string early = "module Routing\n  func Probe () → void\n    !\n    let string?:x = null\n    let string:y = x\n";
        var rejected = Program.Compile(early, "routing.calr", new CompilationOptions { Context = context });
        var active = Assert.Single(rejected.Diagnostics);
        Assert.Equal(DiagnosticCode.NullableToNonNullableBinding, active.Code);
        Assert.Equal(BindingReceivingShape.ScalarString, active.BindingContext?.Shape);
        var optOut = Program.Compile(early, "routing.calr", new CompilationOptions
        {
            Context = context, EnableTypeChecking = false
        });
        Assert.True(optOut.HasErrors);
        Assert.Contains(optOut.Diagnostics,
            d => d.Code == DiagnosticCode.NullableToNonNullableBinding
                && d.BindingContext?.Shape == BindingReceivingShape.ScalarString);
    }

    [Fact]
    public void BindingScope_DoesNotRelabelOtherPasses_AndPreservesFixes()
    {
        var bag = new DiagnosticBag();
        var span = new TextSpan(0, 1, 1, 1);
        bag.ReportError(span, DiagnosticCode.UndefinedReference, "before binding");
        Bind("module Routing\n  func Probe () → int\n    !\n    return absent\n", bag);
        bag.ReportError(span, DiagnosticCode.UndefinedReference, "after binding");
        Assert.Null(bag.First().BindingContext);
        Assert.Null(bag.Last().BindingContext);
        Assert.NotNull(bag.ElementAt(1).BindingContext);
        Assert.True(bag.HasErrors);

        var duplicates = Bind("module Routing\n  func Probe (int:x,int:x) → int\n    !\n    return x\n");
        Assert.Contains(duplicates.DiagnosticsWithFixes, d => d.BindingContext is not null);
        Assert.All(duplicates.Errors, d => Assert.True(BindingDiagnosticPolicy.IsCompilationError(d)));
    }

    [Fact]
    public void ActiveErrors_PropagateOnce_WithoutChangingTheirSpanSeverityOrMessage()
    {
        var bag = Bind("module Routing\n  func Probe () → int\n    !\n    return 1\n  func Probe () → int\n    !\n    return 2\n");
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

    [Fact]
    public void NativeReferenceSuggestions_SurviveRoutingWithoutBecomingSafeFixes()
    {
        const string source = "module Routing\n  func Probe (string:value) → string\n    !\n    return vlaue\n";
        var binding = Bind(source);
        var original = Assert.Single(binding.Errors);
        var originalFix = Assert.Single(binding.DiagnosticsWithFixes);
        Assert.False(originalFix.Fix.IsSafeToApply);
        Assert.Equal("value", Assert.Single(originalFix.Fix.Edits).NewText);

        var destination = new DiagnosticBag();
        BindingDiagnosticPolicy.PropagateCompilationErrors(binding, destination);
        BindingDiagnosticPolicy.PropagateCompilationErrors(binding, destination);
        Assert.Empty(destination);
        Assert.Empty(destination.DiagnosticsWithFixes);

        var duplicate = Bind("module Routing\n  func Probe (int:x,int:x) → int\n    !\n    return x\n");
        BindingDiagnosticPolicy.PropagateCompilationErrors(duplicate, destination);
        BindingDiagnosticPolicy.PropagateCompilationErrors(duplicate, destination);
        Assert.Equal(duplicate.Where(BindingDiagnosticPolicy.IsCompilationError), destination);
        Assert.Same(Assert.Single(duplicate.DiagnosticsWithFixes).Fix,
            Assert.Single(destination.DiagnosticsWithFixes).Fix);
        foreach (var typeChecking in new[] { false, true })
        {
            var compiled = Program.Compile(source, "routing.calr", new CompilationOptions
            {
                EnableTypeChecking = typeChecking
            });
            Assert.True(compiled.HasErrors);
            var diagnostic = Assert.Single(compiled.Diagnostics.Errors);
            Assert.Equal(typeChecking ? DiagnosticCode.UndefinedReference : DiagnosticCode.CodeGenCompilationError,
                diagnostic.Code);
            if (typeChecking)
            {
                Assert.Empty(compiled.GeneratedCode);
                Assert.Equal(original.Span, diagnostic.Span);
            }
            else
            {
                Assert.NotEmpty(compiled.GeneratedCode);
                Assert.Empty(compiled.Diagnostics.DiagnosticsWithFixes);
            }
            var envelope = Assert.Single(DiagnosticEnvelope.Build(compiled.Diagnostics));
            if (typeChecking)
            {
                Assert.NotNull(envelope.Fix);
                Assert.False(envelope.Fix.IsSafeToApply);
                Assert.Equal("value", Assert.Single(envelope.Fix.Edits).NewText);
            }
            else
            {
                Assert.Null(envelope.Fix);
            }
            var fixedSource = DiagnosticFixes.Repair(source, "routing.calr", new CompilationOptions
            {
                EnableTypeChecking = typeChecking
            }, out var repaired, out var applied);
            Assert.Equal(source, fixedSource);
            Assert.Equal(0, applied);
            Assert.Equal(compiled.Diagnostics.Select(d => (d.Code, d.Span, d.Message, d.Severity)),
                repaired.Diagnostics.Select(d => (d.Code, d.Span, d.Message, d.Severity)));
        }
    }

    [Fact]
    public void RoutedFixes_KeepReportedWordingAndRespectFileSeverityAndPolicy()
    {
        var source = new DiagnosticBag();
        source.SetFilePath("a.calr");
        var span = new TextSpan(10, 5, 2, 4);
        var fix = new SuggestedFix("Use value",
            TextEdit.Replace("a.calr", 2, 4, 2, 9, "value"));
        source.ReportErrorWithFix(span, DiagnosticCode.DuplicateDefinition, "binding wording", fix);
        source.ReportErrorWithFix(span, DiagnosticCode.NullableToNonNullableBinding, "analysis only", fix);
        var destination = new DiagnosticBag();
        destination.SetFilePath("a.calr");
        destination.ReportError(span, DiagnosticCode.DuplicateDefinition, "reported wording");
        destination.ReportWarning(span, DiagnosticCode.DuplicateDefinition, "warning wording");
        destination.ReportError(span, DiagnosticCode.NullableToNonNullableBinding, "analysis only");
        destination.SetFilePath("b.calr");
        destination.ReportError(span, DiagnosticCode.DuplicateDefinition, "other file");
        var original = destination.ToArray();
        BindingDiagnosticPolicy.PropagateFixesToExistingDiagnostics(source, destination);
        BindingDiagnosticPolicy.PropagateFixesToExistingDiagnostics(source, destination);
        Assert.Equal(original, destination.ToArray());
        var attached = Assert.Single(destination.DiagnosticsWithFixes);
        Assert.Equal("reported wording", attached.Message);
        Assert.Same(fix, attached.Fix);
        var envelopes = DiagnosticEnvelope.Build(destination);
        Assert.NotNull(envelopes[0].Fix);
        Assert.All(envelopes.Skip(1), diagnostic => Assert.Null(diagnostic.Fix));
    }

    private static DiagnosticBag Bind(string source, DiagnosticBag? diagnostics = null)
    {
        Assert.StartsWith("module ", source);
        diagnostics ??= new DiagnosticBag();
        var parse = new DiagnosticBag();
        var module = new Parser(new Lexer(source, parse).TokenizeAllForParser(), parse).Parse();
        Assert.False(parse.HasErrors, string.Join("; ", parse));
        new Binder(diagnostics, "routing.calr").Bind(module);
        return diagnostics;
    }
}
