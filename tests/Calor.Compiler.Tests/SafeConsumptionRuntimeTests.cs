using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;
using Diagnostic = Calor.Compiler.Diagnostics.Diagnostic;
using NullableAnnotation = Calor.Compiler.Binding.BoundTypes.NullableAnnotation;

namespace Calor.Compiler.Tests;

public sealed class SafeConsumptionRuntimeTests
{
    [Theory]
    [InlineData("bind", false)]
    [InlineData("return", false)]
    [InlineData("argument", false)]
    [InlineData("bind", true)]
    [InlineData("return", true)]
    [InlineData("argument", true)]
    public void Coalesce_AllConsumersAgreeWithActualRuntimeAndCli(string consumer, bool throwing)
    {
        var expression = throwing ? "(?? input §TH \"missing\")" : "(?? input \"fallback\")";
        var source = Source(consumer, expression);
        foreach (var text in RoundTrip(source))
        {
            var (bound, diagnostics) = Bind(text);
            Assert.DoesNotContain(diagnostics, IsNullableDiagnostic);
            var result = Program.Compile(text, "consumption.calr");
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
            Assert.Equal("value", method.Invoke(null, ["value"]));
            if (throwing)
            {
                var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [null]));
                Assert.Equal("missing", Assert.IsType<Exception>(exception.InnerException).Message);
            }
            else
                Assert.Equal("fallback", method.Invoke(null, [null]));
            Assert.Equal(2, bound.Functions.Count);
        }
        WithCli(source, (exit, _, error) => Assert.True(exit == 0, error));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Coalesce_SelectedNamedArgumentsAgreeAcrossBothCallForms(bool statement, bool throwing)
    {
        var expression = throwing ? "(?? input §TH \"missing\")" : "(?? input \"fallback\")";
        var call = $"§C{{Take}} §A[number] INT:7 §A[value] {expression} §/C";
        var source = $$"""
            §M{m1:Consumption}
              §F{f1:Take:pub} (str:value, i32:number) -> str
                §E{}
                §R value
              §F{f2:Probe:pub} (?str:input) -> str
                §E{throw}
                {{(statement ? call + "\n    §R \"done\"" : "§R " + call)}}
            """;
        var (bound, diagnostics) = Bind(source);
        Assert.Empty(diagnostics.Errors);
        var matches = statement
            ? Assert.IsType<BoundCallStatement>(bound.Functions[1].Body[0]).SelectedOverloadMatches
            : Assert.IsType<BoundCallExpression>(
                Assert.IsType<BoundReturnStatement>(bound.Functions[1].Body[0]).Expression).SelectedOverloadMatches;
        var match = Assert.Single(matches);
        Assert.Equal(new[] { 1, 0 }, match.Arguments.Select(argument => argument.ParameterIndex));
        var result = Program.Compile(source, "named-consumption.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
        Assert.Equal(statement ? "done" : "value", method.Invoke(null, ["value"]));
        if (throwing)
        {
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [null]));
            Assert.Equal("missing", Assert.IsType<Exception>(error.InnerException).Message);
        }
        else
            Assert.Equal(statement ? "done" : "fallback", method.Invoke(null, [null]));
        WithCli(source, (exit, _, error) => Assert.True(exit == 0, error));

        var unsafeSource = source.Replace(expression, "input", StringComparison.Ordinal);
        var (_, unsafeDiagnostics) = Bind(unsafeSource);
        Assert.Contains(unsafeDiagnostics, diagnostic =>
            IsNullableDiagnostic(diagnostic) || diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        Assert.True(Program.Compile(unsafeSource, "unsafe-named-consumption.calr").HasErrors);
        WithCli(unsafeSource, (exit, _, _) => Assert.NotEqual(0, exit));
    }

    [Theory]
    [InlineData("bind")]
    [InlineData("return")]
    [InlineData("argument")]
    public void NullableFallback_RemainsVisibleWithoutActivatingPolicy(string consumer)
    {
        var source = Source(consumer, "(?? input fallback)", "?str:fallback");
        var (_, diagnostics) = Bind(source);
        Assert.Contains(diagnostics, diagnostic => IsNullableDiagnostic(diagnostic)
            || diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        foreach (var diagnostic in diagnostics.Where(IsNullableDiagnostic))
            Assert.True(BindingDiagnosticPolicy.IsAnalysisOnly(diagnostic));
        WithCli(source, (exit, _, error) =>
        {
            if (consumer == "argument")
                Assert.NotEqual(0, exit);
            else
                Assert.True(exit == 0, error);
        });
        if (consumer != "argument")
        {
            var result = Program.Compile(source, "consumption.calr");
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
            Assert.Null(method.Invoke(null, [null, null]));
            Assert.Equal("backup", method.Invoke(null, [null, "backup"]));
        }
    }

    [Fact]
    public void ConvertedDefaultString_FallbackRetainsNull()
    {
        var conversion = new CSharpToCalorConverter().Convert("""
            #nullable enable
            public static class DefaultFallback {
                public static string? Probe(string? input) => input ?? default(string);
            }
            """);
        Assert.True(conversion.Success);
        Assert.NotNull(conversion.CalorSource);
        var result = Program.Compile(conversion.CalorSource, "default-fallback.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetTypes().Single(type => type.Name == "DefaultFallback").GetMethod("Probe")!;
        Assert.Null(method.Invoke(null, [null]));
        Assert.Equal("value", method.Invoke(null, ["value"]));
        var (_, diagnostics) = Bind(Source("return", "(?? input null)"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulTypedPattern_IsNonNullAndDoesNotNarrowTheInput(bool match)
    {
        var source = match ? """
            §M{m1:Consumption}
              §F{f1:Probe:pub} (?str:input) -> str
                §E{}
                §R §W{w1:expr} input
                  §K §PTYPE{str:text} → text
                  §K _ → "fallback"
            """ : """
            §M{m1:Consumption}
              §F{f1:Probe:pub} (?str:input) -> str
                §E{}
                §IF{if1} (is input str text)
                  §B{value:str} text
                  §R value
                §R "fallback"
            """;
        foreach (var text in RoundTrip(source))
        {
            var (_, diagnostics) = Bind(text);
            Assert.DoesNotContain(diagnostics, IsNullableDiagnostic);
            var result = Program.Compile(text, "pattern.calr");
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
            Assert.Equal("value", method.Invoke(null, ["value"]));
            Assert.Equal("fallback", method.Invoke(null, [null]));
        }
        var (_, unsafeDiagnostics) = Bind(source.Replace("§R \"fallback\"", "§R input", StringComparison.Ordinal)
            .Replace("§K _ → \"fallback\"", "§K _ → input", StringComparison.Ordinal));
        Assert.Contains(unsafeDiagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Theory]
    [InlineData("§EL\n      §R text")]
    [InlineData("§R text")]
    public void PatternBinding_DoesNotEscapeSuccessfulBranch(string tail)
    {
        var source = """
            §M{m1:Consumption}
              §F{f1:Probe:pub} (?str:input) -> str
                §E{}
                §IF{if1} (is input str text)
                  §R text
            """ + "\n    " + tail;
        var (_, diagnostics) = Bind(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference
            && diagnostic.Message.Contains("text", StringComparison.Ordinal));
        Assert.True(Program.Compile(source, "pattern-leak.calr").HasErrors);
        WithCli(source, (exit, _, _) => Assert.NotEqual(0, exit));
    }

    [Fact]
    public void BothThrowingArms_AreNeverAndExecuteTheSelectedException()
    {
        const string source = """
            §M{m1:Consumption}
              §F{f1:Probe:pub} (bool:flag) -> str
                §E{throw}
                §B{value:str} (? flag §TH "left" §TH "right")
                §R value
            """;
        var (bound, diagnostics) = Bind(source);
        Assert.DoesNotContain(diagnostics, IsNullableDiagnostic);
        var initializer = Assert.IsType<BoundBindStatement>(bound.Functions[0].Body[0]).Initializer!;
        Assert.Equal("NEVER", initializer.Type.DisplayString);
        var ast = Parse(source);
        var conditional = Assert.IsType<ConditionalExpressionNode>(
            Assert.IsType<BindStatementNode>(ast.Functions[0].Body[0]).Initializer);
        var checker = new TypeChecker(new DiagnosticBag());
        var infer = typeof(TypeChecker).GetMethod("InferExpressionType", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Literal condition avoids manufacturing a scope solely to inspect the bottom type.
        var literalCondition = new ConditionalExpressionNode(conditional.Span,
            new BoolLiteralNode(conditional.Span, true), conditional.WhenTrue, conditional.WhenFalse);
        Assert.IsType<NeverType>(infer.Invoke(checker, [literalCondition]));
        var result = Program.Compile(source, "never.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
        foreach (var flag in new[] { false, true })
        {
            var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [flag]));
            Assert.Equal(flag ? "left" : "right", Assert.IsType<Exception>(exception.InnerException).Message);
        }
    }

    [Fact]
    public void UnknownFallback_IsNotPromotedToNonNull()
    {
        var left = new NominalBoundType("STRING", NullableAnnotation.Annotated);
        var result = ExpressionResultTypes.Coalesce("STRING", left, new UnresolvedBoundType("unknown"));
        Assert.Equal(NullableAnnotation.Oblivious, Assert.IsType<NominalBoundType>(result).NullableAnnotation);
    }

    [Theory]
    [InlineData("bind")]
    [InlineData("return")]
    [InlineData("argument")]
    public void LiteralNullWithNonNullFallback_HasTheFallbackType(string consumer)
    {
        var source = Source(consumer, "(?? null \"fallback\")");
        var (_, diagnostics) = Bind(source);
        Assert.DoesNotContain(diagnostics, IsNullableDiagnostic);
        var result = Program.Compile(source, "literal-null.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
        Assert.Equal("fallback", method.Invoke(null, [null]));
    }

    [Fact]
    public void NominalCoalesce_PreservesIdentityAndNullableFallbackEvidence()
    {
        const string source = """
            §M{m1:Consumption}
              §CL{c1:Foo:pub}
                §FLD{i32:value:pub}
              §F{f1:Probe:pub} (?Foo:input, ?Foo:fallback) -> Foo
                §E{throw}
                §R (?? input fallback)
            """;
        var (_, unsafeDiagnostics) = Bind(source);
        Assert.Contains(unsafeDiagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
        var safeSource = source.Replace("(?? input fallback)", "(?? input §TH \"missing\")", StringComparison.Ordinal);
        var (_, safeDiagnostics) = Bind(safeSource);
        Assert.DoesNotContain(safeDiagnostics, IsNullableDiagnostic);
        var result = Program.Compile(safeSource, "nominal-consumption.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var assembly = Emit(result.GeneratedCode);
        var method = assembly.GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
        var value = Activator.CreateInstance(assembly.GetType("Consumption.Foo")!);
        Assert.Same(value, method.Invoke(null, [value, null]));
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [null, null]));
        Assert.Equal("missing", error.InnerException!.Message);
    }

    [Theory]
    [InlineData("(&& (is input str text) (== text \"value\"))")]
    [InlineData("(&& true (is input str text))")]
    [InlineData("(&& (&& true (is input str text)) (== text \"value\"))")]
    public void Conjunction_PreservesTheSuccessfulPatternInRhsAndBody(string condition)
    {
        var source = $$"""
            §M{m1:Consumption}
              §F{f1:Probe:pub} (?str:input) -> str
                §E{}
                §IF{if1} {{condition}}
                  §R text
                §R "fallback"
            """;
        var (_, diagnostics) = Bind(source);
        Assert.Empty(diagnostics.Errors);
        var result = Program.Compile(source, "and-pattern.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
        Assert.Equal("value", method.Invoke(null, ["value"]));
        Assert.Equal("fallback", method.Invoke(null, [null]));
    }

    [Theory]
    [InlineData("bind", "if")]
    [InlineData("return", "if")]
    [InlineData("argument", "if")]
    [InlineData("bind", "while")]
    [InlineData("return", "while")]
    [InlineData("argument", "while")]
    [InlineData("bind", "while-conjunction")]
    [InlineData("return", "while-conjunction")]
    [InlineData("argument", "while-conjunction")]
    public void TypedPattern_AllConsumersHandleNullAndUnmatchedInput(string consumer, string branch)
    {
        var body = consumer switch
        {
            "bind" => "§B{value:str} text\n      §R value",
            "return" => "§R text",
            "argument" => "§R §C{Take} §A text §/C",
            _ => throw new ArgumentOutOfRangeException(nameof(consumer))
        };
        var condition = branch switch
        {
            "if" => "§IF{if1} (is input str text)",
            "while" => "§WH{wh1} (is input str text)",
            "while-conjunction" => "§WH{wh1} (&& (is input str text) (== text \"value\"))",
            _ => throw new ArgumentOutOfRangeException(nameof(branch))
        };
        var source = $$"""
            §M{m1:Consumption}
              §F{f1:Take:pub} (str:value) -> str
                §E{}
                §R value
              §F{f2:Probe:pub} (?object:input) -> str
                §E{}
                {{condition}}
                  {{body}}
                §R "fallback"
            """;
        var (_, diagnostics) = Bind(source);
        Assert.Empty(diagnostics.Errors);
        var result = Program.Compile(source, "pattern-consumers.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
        Assert.Equal("value", method.Invoke(null, ["value"]));
        Assert.Equal("fallback", method.Invoke(null, [null]));
        Assert.Equal("fallback", method.Invoke(null, [42]));
        WithCli(source, (exit, _, error) => Assert.True(exit == 0, error));
    }

    [Fact]
    public void WhilePattern_DoesNotSupplyBindingsAfterTheLoopOrWithoutGuaranteedSuccess()
    {
        foreach (var condition in new[] { "(is input str text)", "(! (is input str text))", "(|| (is input str text) true)" })
        {
            var source = $$"""
                §M{m1:Consumption}
                  §F{f1:Probe:pub} (?object:input) -> str
                    §E{}
                    §WH{wh1} {{condition}}
                      §R text
                    §R text
                """;
            var (_, diagnostics) = Bind(source);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference
                && diagnostic.Span.Line == 6 && diagnostic.Message.Contains("text", StringComparison.Ordinal));
            if (condition != "(is input str text)")
                Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference
                    && diagnostic.Span.Line == 5 && diagnostic.Message.Contains("text", StringComparison.Ordinal));
            Assert.True(Program.Compile(source, "while-pattern-leak.calr").HasErrors);
            WithCli(source, (exit, _, _) => Assert.NotEqual(0, exit));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchGuardPattern_IsAvailableOnlyInItsSuccessfulCase(bool expression)
    {
        var match = expression ? """
            §R §W{w1:expr} input
                  §K _ §WHEN (is input str text) → §C{Take} §A text §/C
                  §K _ → "fallback"
            """ : """
            §W{w1} input
                  §K §VAR{candidate} §WHEN (is input str text)
                    §B{value:str} text
                    §R §C{Take} §A value §/C
                  §K _
                    §R "fallback"
            """;
        var source = $$"""
            §M{m1:Consumption}
              §F{f1:Take:pub} (str:value) -> str
                §E{}
                §R value
              §F{f2:Probe:pub} (?object:input) -> str
                §E{}
                {{match}}
            """;
        foreach (var text in RoundTrip(source))
        {
            var (_, diagnostics) = Bind(text);
            Assert.Empty(diagnostics.Errors);
            var result = Program.Compile(text, "match-guard-consumption.calr");
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
            Assert.Equal("value", method.Invoke(null, ["value"]));
            Assert.Equal("fallback", method.Invoke(null, [null]));
            Assert.Equal("fallback", method.Invoke(null, [42]));
        }
        WithCli(source, (exit, _, error) => Assert.True(exit == 0, error));
        foreach (var unsafeSource in new[]
        {
            source.Replace("\"fallback\"", "text", StringComparison.Ordinal),
            source.Replace("(is input str text)", "(! (is input str text))", StringComparison.Ordinal),
            source.Replace("(is input str text)", "(|| (is input str text) true)", StringComparison.Ordinal)
        })
        {
            var (_, diagnostics) = Bind(unsafeSource);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference
                && diagnostic.Message.Contains("text", StringComparison.Ordinal));
            Assert.True(Program.Compile(unsafeSource, "match-guard-leak.calr").HasErrors);
        }
    }

    [Fact]
    public void ExplicitOptionUnwrap_RetainsItsActualRuntimeContract()
    {
        const string source = """
            §M{m1:Consumption}
              §F{f1:Probe:pub} (bool:present) -> str
                §E{alloc,throw}
                §B{opt:Option<str>} (? present §SM "value" §NN{str})
                §R §C{opt.Unwrap} §/C
            """;
        foreach (var text in RoundTrip(source))
        {
            var result = Program.Compile(text, "option-consumption.calr");
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Contains("Option", result.GeneratedCode);
            Assert.Contains("opt.Unwrap()", result.GeneratedCode);
            var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
            Assert.Equal("value", method.Invoke(null, [true]));
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [false]));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        WithCli(source, (exit, _, error) => Assert.True(exit == 0, error));
    }

    [Fact]
    public void NegatedAndDisjunctiveVarPatterns_DoNotSupplySuccessfulBindings()
    {
        foreach (var pattern in new[] { "(not §VAR{text})", "(or §VAR{text} _)" })
        {
            var source = $$"""
                §M{m1:Consumption}
                  §F{f1:Probe:pub} (?str:input) -> str
                    §E{}
                    §R §W{w1:expr} input
                      §K {{pattern}} → text
                      §K _ → "fallback"
                """;
            var (_, diagnostics) = Bind(source);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference
                && diagnostic.Message.Contains("text", StringComparison.Ordinal));
            Assert.True(Program.Compile(source, "invalid-pattern-binding.calr").HasErrors);
        }
    }

    [Fact]
    public void VarPattern_PreservesNullableOperandWithoutUnknownTypeWarnings()
    {
        const string source = """
            §M{m1:Consumption}
              §F{f1:Probe:pub} (?str:input) -> str
                §E{}
                §R (? (is input var text) text "fallback")
            """;
        var (_, diagnostics) = Bind(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
        var result = Program.Compile(source, "var-consumption.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
        Assert.Null(method.Invoke(null, [null]));
        Assert.Equal("value", method.Invoke(null, ["value"]));
    }

    [Fact]
    public void ThrowNull_RetainsItsNonReturningRuntimeSemantics()
    {
        var source = Source("return", "(?? input §TH null)");
        var result = Program.Compile(source, "throw-null.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
        Assert.Equal("value", method.Invoke(null, ["value"]));
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [null]));
        Assert.IsType<NullReferenceException>(error.InnerException);
    }

    [Fact]
    public void OverloadedExceptionFactory_DoesNotUseAnUnselectedReturnType()
    {
        const string source = """
            §M{m1:Consumption}
              §F{f1:Make:pub} (i32:value) -> System.Exception
                §E{alloc}
                §B{exception:System.Exception} §NEW{System.Exception}
                §R exception
              §F{f2:Make:pub} (str:value) -> str
                §E{}
                §R value
              §F{f3:Probe:pub} (?str:input) -> str
                §E{alloc,throw}
                §R (?? input §TH §C{Make} §A INT:1 §/C)
            """;
        var result = Program.Compile(source, "overloaded-exception.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
        Assert.Equal("value", method.Invoke(null, ["value"]));
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [null]));
        Assert.IsType<Exception>(error.InnerException);
    }

    [Fact]
    public void NominalConditional_ThroughInferredLocalsRetainsTheActualSelectedValue()
    {
        foreach (var rightType in new[] { "Foo", "?Foo" })
        {
            var source = $$"""
                §M{m1:Consumption}
                  §CL{c1:Foo:pub}
                    §FLD{i32:value:pub}
                  §F{f1:Probe:pub} (bool:choose, Foo:left, {{rightType}}:right) -> {{rightType}}
                    §E{}
                    §B{first} (? choose left right)
                    §B{second} first
                    §R second
                """;
            var result = Program.Compile(source, "conditional-local-consumption.calr");
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            var assembly = Emit(result.GeneratedCode);
            var value = Activator.CreateInstance(assembly.GetType("Consumption.Foo")!);
            var right = rightType == "?Foo" ? null : value;
            var method = assembly.GetType("Consumption.ConsumptionModule")!.GetMethod("Probe")!;
            Assert.Same(value, method.Invoke(null, [true, value, right]));
            Assert.Same(right, method.Invoke(null, [false, value, right]));
        }
    }

    [Fact]
    public void ConvertedTypedPattern_PreservesNonNullCallAndReturnConsumers()
    {
        var conversion = new CSharpToCalorConverter().Convert("""
            #nullable enable
            public static class ConvertedPattern {
                public static string Take(string value) => value;
                public static string Probe(object? input) =>
                    input switch { string text => Take(text), _ => "fallback" };
            }
            """);
        Assert.True(conversion.Success);
        Assert.NotNull(conversion.CalorSource);
        var (_, diagnostics) = Bind(conversion.CalorSource);
        Assert.DoesNotContain(diagnostics, IsNullableDiagnostic);
        var result = Program.Compile(conversion.CalorSource, "converted-pattern.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetTypes().Single(type => type.Name == "ConvertedPattern").GetMethod("Probe")!;
        Assert.Equal("value", method.Invoke(null, ["value"]));
        Assert.Equal("fallback", method.Invoke(null, [null]));
        Assert.Equal("fallback", method.Invoke(null, [42]));
    }

    private static string Source(string consumer, string expression, string? extraParameter = null)
    {
        var body = consumer switch
        {
            "bind" => $"§B{{value:str}} {expression}\n    §R value",
            "return" => $"§R {expression}",
            "argument" => $"§R §C{{Take}} §A {expression} §/C",
            _ => throw new ArgumentOutOfRangeException(nameof(consumer))
        };
        return $$"""
            §M{m1:Consumption}
              §F{f1:Take:pub} (str:value) -> str
                §E{}
                §R value
              §F{f2:Probe:pub} (?str:input{{(extraParameter == null ? "" : ", " + extraParameter)}}) -> str
                §E{throw}
                {{body}}
            """;
    }

    private static bool IsNullableDiagnostic(Diagnostic diagnostic) =>
        diagnostic.Code is DiagnosticCode.NullableToNonNullableBinding
            or DiagnosticCode.NullableReturnFromNonNullable or DiagnosticCode.NullableArgumentToNonNullableParameter;

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        return module;
    }

    private static (BoundModule, DiagnosticBag) Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        return (new Binder(diagnostics).Bind(Parse(source)), diagnostics);
    }

    private static IEnumerable<string> RoundTrip(string source)
    {
        yield return source;
        yield return new CalorEmitter().Emit(Parse(source));
    }

    private static Assembly Emit(string source)
    {
        var compilation = CSharpCompilation.Create("Consumption_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static void WithCli(string source, Action<int, string, string> assert)
    {
        var directory = Path.Combine(Path.GetTempPath(), "calor-consumption-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "input.calr");
            File.WriteAllText(input, source);
            var result = CliTestHarness.RunCli(directory, "-i", input, "--no-cache", "--no-telemetry");
            assert(result.ExitCode, result.StdOut, result.StdErr);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
