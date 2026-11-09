using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class FinalExpressionGrammarTests
{
    [Theory]
    [InlineData("upper(x)")]
    [InlineData("trim(lower(x))")]
    [InlineData("cast(x)")]
    [InlineData("@new(x)")]
    [InlineData("forall(x)")]
    [InlineData("exists(x)")]
    public void NamesAndBuiltinCollisions_AreOrdinaryCalls(string source)
    {
        var call = Assert.IsType<CallExpressionNode>(Parse(source));
        Assert.Equal(source.Split('(')[0].TrimStart('@'), call.Target);
    }

    [Theory]
    [InlineData("(+ a b)")]
    [InlineData("(upper x)")]
    [InlineData("(cast int x)")]
    [InlineData("(-> p q)")]
    [InlineData("()")]
    [InlineData("f(, x)")]
    [InlineData("f(x,)")]
    [InlineData("f(x)\ng(y)")]
    [InlineData("a{0}")]
    [InlineData("() => { return 1 }")]
    [InlineData("() =>\nNext()")]
    public void RetiredAndMalformedForms_DoNotBecomeNativePositive(string source)
    {
        var diagnostics = new DiagnosticBag();
        Parse(source, diagnostics);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void GroupingAndUnary_HaveNoGreedyPrefixException()
    {
        var binary = Assert.IsType<BinaryOperationNode>(Parse("(- x -1)"));
        Assert.Equal(BinaryOperator.Subtract, binary.Operator);
        Assert.IsType<UnaryOperationNode>(binary.Left);
        Assert.Equal(1, Assert.IsType<IntLiteralNode>(binary.Right).Value);
        Assert.IsType<ReferenceNode>(Parse("(upper)"));
        Assert.IsType<TupleLiteralNode>(Parse("(upper, other)"));
    }

    [Fact]
    public void ComputedGenericCallee_PreservesOrderedChannels()
    {
        var call = Assert.IsType<ExpressionCallNode>(Parse("Make().M<int>(second: Arg(2), first: Arg(1))"));
        Assert.Equal(["int"], call.TypeArguments);
        Assert.Equal(["second", "first"], call.ArgumentNames);
        var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal("M", member.FieldName);
        Assert.IsType<CallExpressionNode>(member.Target);
        Assert.All(call.Arguments, argument => Assert.IsType<CallExpressionNode>(argument));
    }

    [Theory]
    [InlineData("(int)x", typeof(TypeOperationNode))]
    [InlineData("x as string", typeof(TypeOperationNode))]
    [InlineData("x is string text", typeof(IsPatternNode))]
    [InlineData("typeof(List<int>)", typeof(TypeOfExpressionNode))]
    [InlineData("(x, y) => x + y", typeof(LambdaExpressionNode))]
    [InlineData("() => 1", typeof(LambdaExpressionNode))]
    [InlineData("new Box(x)", typeof(NewExpressionNode))]
    [InlineData("new Box(first: x) { Value = 2 }", typeof(NewExpressionNode))]
    [InlineData("static (x) => x", typeof(LambdaExpressionNode))]
    [InlineData("async () => Work()", typeof(LambdaExpressionNode))]
    public void NativeTypeAndLambdaForms_UseExistingAst(string source, Type type)
    {
        Assert.IsType(type, Parse(source));
    }

    [Theory]
    [InlineData("a + b * c")]
    [InlineData("a ?? b ?? c")]
    [InlineData("flag ? Next(1) : Next(2)")]
    [InlineData("Make().M<int>(Next(1), Next(2))")]
    [InlineData("(int)x")]
    [InlineData("x is string text")]
    [InlineData("(x, y) => x + y")]
    [InlineData("new Box(x)")]
    [InlineData("@new(x)")]
    [InlineData("obj.@new(@in: x)")]
    [InlineData("@true")]
    [InlineData("(int:x) =>\n  return x + 1")]
    [InlineData("Use((int:x) =>\n  return x + 1, 40)")]
    [InlineData("new int[Next()]")]
    [InlineData("new int[] { Next(1), Next(2) }")]
    [InlineData("new int[] { }")]
    [InlineData("new int[,] { { 1, 2 }, { 3, 4 } }")]
    [InlineData("new int[Next(1), Next(2)]")]
    [InlineData("new int[][] { new int[] { 1 } }")]
    public void NativeEmission_ReparsesWithoutExpressionClosers(string source)
    {
        var expression = Parse(source);
        var emitted = expression.Accept(new CalorEmitter());
        Assert.DoesNotContain('§', emitted);
        Assert.IsType(expression.GetType(), Parse(emitted));
    }

    [Fact]
    public void NativeGeneratedClr_PreservesCalleeArgumentOrderAndShortCircuit()
    {
        var ordered = Parse("Make().M(Arg(1), Arg(2))").Accept(new CSharpEmitter());
        var lazy = Parse("false && Fail() || true").Accept(new CSharpEmitter());
        var source = $$"""
            using System;
            using System.Collections.Generic;
            public class Probe
            {
                public static List<int> Trace = new();
                public static Probe Make() { Trace.Add(0); return new Probe(); }
                public static int Arg(int x) { Trace.Add(x); return x; }
                public int M(int x, int y) { Trace.Add(3); return x + y; }
                public static bool Fail() => throw new Exception("must not run");
                public static int Ordered() => {{ordered}};
                public static bool Lazy() => {{lazy}};
            }
            """;
        var compilation = CSharpCompilation.Create("FinalExpr_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var type = Assembly.Load(stream.ToArray()).GetType("Probe")!;
        Assert.Equal(3, type.GetMethod("Ordered")!.Invoke(null, null));
        Assert.Equal([0, 1, 2, 3], Assert.IsType<List<int>>(type.GetField("Trace")!.GetValue(null)));
        Assert.Equal(true, type.GetMethod("Lazy")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("f(, x)")]
    [InlineData("(upper x)")]
    [InlineData("§C{f} §A x §/C")]
    public void PostConversionFixer_DoesNotNormalizeInvalidNativeCandidates(string source)
    {
        var fixedSource = new PostConversionFixer().Fix(source);
        Assert.False(fixedSource.WasModified);
        Assert.Empty(fixedSource.AppliedFixes);
        Assert.Equal(source, fixedSource.FixedSource);
        var diagnostics = new DiagnosticBag();
        Parse(fixedSource.FixedSource, diagnostics);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Interpolation_HasNativeCallAstAndLiteralEscape()
    {
        const string source = "\"v=${Next(1) + Next(2)} literal=\\${0}\"";
        var interpolation = Assert.IsType<InterpolatedStringNode>(Parse(source));
        var hole = Assert.Single(interpolation.Parts.OfType<InterpolatedStringExpressionNode>());
        var binary = Assert.IsType<BinaryOperationNode>(hole.Expression);
        Assert.IsType<CallExpressionNode>(binary.Left);
        Assert.IsType<CallExpressionNode>(binary.Right);
        Assert.Equal("Next(1) + Next(2)", source.Substring(binary.Span.Start, binary.Span.Length));
        var emitted = interpolation.Accept(new CalorEmitter());
        Assert.Contains("\\${0}", emitted);
        Assert.IsType<InterpolatedStringNode>(Parse(emitted));
    }

    [Theory]
    [InlineData("\"v=${f(, x)}\"")]
    [InlineData("\"v=${upper x}\"")]
    public void InvalidInterpolation_IsNotRescuedAsOpaqueCSharp(string source)
    {
        var diagnostics = new DiagnosticBag();
        Parse(source, diagnostics);
        Assert.True(diagnostics.HasErrors);
    }

    [Theory]
    [InlineData("(result >= 0)")]
    [InlineData("(true)")]
    [InlineData("((result >= 0))")]
    public void GroupedPostconditions_DoNotCaptureFollowingClauseAsLambda(string condition)
    {
        var source = $"module P\n  func F (int:x) → int\n    => {condition}\n    => result == x\n    => result <= 100\n    return x";
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        var function = Assert.Single(module.Functions);
        Assert.Equal(3, function.Postconditions.Count);
        Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body));
    }

    [Fact]
    public void UnmeasuredQuantifierSpelling_IsNotEmittedAsRetiredPrefixOrOpaqueClr()
    {
        var variable = new QuantifierVariableNode(default, "i", "int");
        var body = new BoolLiteralNode(default, true);
        ExpressionNode[] quantifiers =
        [
            new ForallExpressionNode(default, [variable], body),
            new ExistsExpressionNode(default, [variable], body)
        ];
        foreach (var quantifier in quantifiers)
        {
            var error = Assert.Throws<NotSupportedException>(() => quantifier.Accept(new CalorEmitter()));
            Assert.Contains("supplemental syntax decision", error.Message);
        }
    }

    [Fact]
    public void NativeBlockLambda_KeepsCallArgumentsOutsideItsBody()
    {
        var call = Assert.IsType<CallExpressionNode>(Parse("Use((int:x) =>\n  return x + 1, 40)"));
        Assert.Equal(2, call.Arguments.Count);
        var lambda = Assert.IsType<LambdaExpressionNode>(call.Arguments[0]);
        var statement = Assert.IsType<ReturnStatementNode>(Assert.Single(lambda.StatementBody!));
        Assert.IsType<BinaryOperationNode>(statement.Expression);
        Assert.Equal(40, Assert.IsType<IntLiteralNode>(call.Arguments[1]).Value);
    }

    [Fact]
    public void NativeArrays_PreserveFreshEmptyAllocationAndElementOrderAtRuntime()
    {
        var empty = Parse("new int[] { }").Accept(new CSharpEmitter());
        var initialized = Parse("new int[] { Next(1), Next(2) }").Accept(new CSharpEmitter());
        var sized = Parse("new int[Next(1), Next(2)]").Accept(new CSharpEmitter());
        var source = $$"""
            using System;
            using System.Collections.Generic;
            public static class Probe
            {
                public static List<int> Trace = new();
                public static int Next(int value) { Trace.Add(value); return value; }
                public static bool Fresh() => !Object.ReferenceEquals({{empty}}, {{empty}});
                public static int[] Initialized() => {{initialized}};
                public static int[,] Sized() => {{sized}};
            }
            """;
        var compilation = CSharpCompilation.Create("NativeArrays_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var type = Assembly.Load(stream.ToArray()).GetType("Probe")!;
        Assert.Equal(true, type.GetMethod("Fresh")!.Invoke(null, null));
        Assert.Equal(new[] { 1, 2 }, Assert.IsType<int[]>(type.GetMethod("Initialized")!.Invoke(null, null)));
        var trace = Assert.IsType<List<int>>(type.GetField("Trace")!.GetValue(null));
        Assert.Equal([1, 2], trace);
        trace.Clear();
        var array = Assert.IsType<int[,]>(type.GetMethod("Sized")!.Invoke(null, null));
        Assert.Equal(1, array.GetLength(0));
        Assert.Equal(2, array.GetLength(1));
        Assert.Equal([1, 2], trace);
    }

    [Fact]
    public void ConvertedComputedExpressionFragments_StayInsideLambdaAndLazyOperandRegions()
    {
        var sourceTree = CSharpSyntaxTree.ParseText("""
            public static class Probe
            {
                public static System.Func<int> Build(System.Func<System.Func<int>> factory)
                    => () => factory()();
                public static int Select(bool take, System.Func<System.Func<int>> factory)
                    => take ? factory()() : 7;
            }
            """);
        var originalCompilation = CSharpCompilation.Create("ComputedOriginal",
            [sourceTree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var context = new ConversionContext();
        var module = new RoslynSyntaxVisitor(context, originalCompilation.GetSemanticModel(sourceTree))
            .Convert(sourceTree.GetCompilationUnitRoot(), "NativeComputed");
        Assert.Empty(context.Losses);
        var methods = Assert.Single(module.Classes).Methods;
        var build = methods.Single(method => method.Name == "Build");
        var lambda = Assert.IsType<LambdaExpressionNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(build.Body)).Expression);
        Assert.Null(lambda.StatementBody);
        Assert.IsType<ExpressionCallNode>(lambda.ExpressionBody);
        foreach (var method in methods)
        {
            var expression = Assert.IsType<ReturnStatementNode>(Assert.Single(method.Body)).Expression!;
            var native = expression.Accept(new CalorEmitter());
            Assert.DoesNotContain("_chain", native);
            Assert.DoesNotContain("_expr", native);
            Assert.IsType(expression.GetType(), Parse(native));
        }
        var generated = new CSharpEmitter().Emit(module);
        var compilation = CSharpCompilation.Create("NativeComputed_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(generated)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, generated + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
        var type = Assembly.Load(stream.ToArray()).GetTypes().Single(type => type.Name == "Probe");
        var trace = new List<int>();
        Func<Func<int>> factory = () =>
        {
            trace.Add(1);
            return () => { trace.Add(2); return 42; };
        };
        var delayed = Assert.IsType<Func<int>>(type.GetMethod("Build")!.Invoke(null, [factory]));
        Assert.Empty(trace);
        Assert.Equal(42, delayed());
        Assert.Equal([1, 2], trace);
        trace.Clear();
        Assert.Equal(7, type.GetMethod("Select")!.Invoke(null, [false, factory]));
        Assert.Empty(trace);
        Assert.Equal(42, type.GetMethod("Select")!.Invoke(null, [true, factory]));
        Assert.Equal([1, 2], trace);
    }

    [Fact]
    public void MeasuredBlockLambda_ParsesEmitsAndRunsWithoutHoistsOrClosers()
    {
        const string source = """
            module NativeLambda
              func Run (int:x) → int
                let Func<int,int>:f =
                  (int:y) =>
                    let int:z = y + 1
                    return z
                return f(x)
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        var function = Assert.Single(module.Functions);
        Assert.Equal(2, function.Body.Count);
        var binding = Assert.IsType<BindStatementNode>(function.Body[0]);
        var lambda = Assert.IsType<LambdaExpressionNode>(binding.Initializer);
        Assert.Equal(2, lambda.StatementBody!.Count);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        Assert.DoesNotContain("_hoist", emitted);
        Assert.DoesNotContain('{', emitted);
        diagnostics = new DiagnosticBag();
        var reparsed = new Parser(new Lexer(emitted, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, emitted + Environment.NewLine + string.Join(Environment.NewLine, diagnostics.Errors));
        Assert.Equal(2, Assert.Single(reparsed.Functions).Body.Count);
        var generated = new CSharpEmitter().Emit(reparsed);
        var compilation = CSharpCompilation.Create("NativeLambda_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(generated)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var method = Assembly.Load(stream.ToArray()).GetTypes().SelectMany(type => type.GetMethods())
            .Single(method => method.Name == "Run");
        Assert.Equal(42, method.Invoke(null, [41]));
    }

    private static ExpressionNode Parse(string source, DiagnosticBag? diagnostics = null)
    {
        var requireSuccess = diagnostics is null;
        diagnostics ??= new DiagnosticBag();
        var parser = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics);
        var expression = parser.ParseStandaloneExpression();
        if (requireSuccess)
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return expression;
    }
}
