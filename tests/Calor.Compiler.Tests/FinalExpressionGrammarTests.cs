using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class FinalExpressionGrammarTests
{
    [Theory]
    [InlineData("(values[i]) - 1")]
    [InlineData("(values[i, j]) - 1")]
    [InlineData("(values[i][j]) - 1")]
    [InlineData("(int)(text[i]) - 56320")]
    [InlineData("((int)(text[i]) - 56320)")]
    public void IndexedOperandsBeforeSubtraction_AreNotSizedArrayCasts(string source)
    {
        var binary = Assert.IsType<BinaryOperationNode>(Parse(source));
        Assert.Equal(BinaryOperator.Subtract, binary.Operator);
        var reparsed = Assert.IsType<BinaryOperationNode>(Parse(binary.Accept(new CalorEmitter())));
        Assert.Equal(BinaryOperator.Subtract, reparsed.Operator);
    }

    [Theory]
    [InlineData("(int[])value")]
    [InlineData("(int[,])value")]
    [InlineData("(int[][])value")]
    [InlineData("(List<int[]>)value")]
    public void ArrayCasts_RetainOmittedRankSizes(string source)
        => Assert.IsType<TypeOperationNode>(Parse(source));

    [Theory]
    [InlineData("(int[3])value")]
    [InlineData("(int[i])value")]
    [InlineData("(List<int[3]>)value")]
    public void SizedArrayTypes_AreNotAcceptedAsCasts(string source)
    {
        var diagnostics = new DiagnosticBag();
        Parse(source, diagnostics);
        Assert.True(diagnostics.HasErrors);
    }

    [Theory]
    [InlineData("sbyte", typeof(sbyte))]
    [InlineData("byte", typeof(byte))]
    [InlineData("short", typeof(short))]
    [InlineData("ushort", typeof(ushort))]
    [InlineData("int", typeof(int))]
    [InlineData("uint", typeof(uint))]
    [InlineData("long", typeof(long))]
    [InlineData("ulong", typeof(ulong))]
    [InlineData("nint", typeof(nint))]
    [InlineData("nuint", typeof(nuint))]
    [InlineData("char", typeof(char))]
    [InlineData("float", typeof(float))]
    [InlineData("double", typeof(double))]
    [InlineData("decimal", typeof(decimal))]
    [InlineData("bool", typeof(bool))]
    [InlineData("string", typeof(string))]
    [InlineData("object", typeof(object))]
    public void NativePrimitiveDefaults_RetainTheirClrValueTypeAndAuthoredSpan(string spelling, Type expectedType)
    {
        var authored = $"default({spelling})";
        var expression = Parse(authored);
        Assert.Equal(authored.Length, expression.Span.Length);
        Assert.IsNotType<CallExpressionNode>(expression);
        foreach (var node in new[] { expression, Parse(expression.Accept(new CalorEmitter())) })
        {
            var generated = node.Accept(new CSharpEmitter());
            var source = $"public static class Probe {{ public static {spelling} Run() => {generated}; }}";
            var compilation = CSharpCompilation.Create("Default_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, source + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
            var method = Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!;
            Assert.Equal(expectedType, method.ReturnType);
            var value = method.Invoke(null, null);
            Assert.Equal(expectedType.IsValueType ? Activator.CreateInstance(expectedType) : null, value);
            if (value is float single)
                Assert.Equal(0, BitConverter.SingleToInt32Bits(single));
            else if (value is double number)
                Assert.Equal(0L, BitConverter.DoubleToInt64Bits(number));
            else if (value is decimal fraction)
                Assert.Equal(new[] {0, 0, 0, 0}, decimal.GetBits(fraction));
        }
    }

    [Theory]
    [InlineData("sbyte", typeof(sbyte))]
    [InlineData("byte", typeof(byte))]
    [InlineData("short", typeof(short))]
    [InlineData("ushort", typeof(ushort))]
    [InlineData("int", typeof(int))]
    [InlineData("uint", typeof(uint))]
    [InlineData("long", typeof(long))]
    [InlineData("ulong", typeof(ulong))]
    [InlineData("nint", typeof(nint))]
    [InlineData("nuint", typeof(nuint))]
    [InlineData("char", typeof(char))]
    [InlineData("float", typeof(float))]
    [InlineData("double", typeof(double))]
    [InlineData("decimal", typeof(decimal))]
    [InlineData("bool", typeof(bool))]
    public void ConvertedPrimitiveDefaultLiterals_RetainClrValueWidthAndOverload(string spelling, Type expectedType)
    {
        var source = $$"""
            public static class Probe
            {
                public static {{spelling}} Read() => default;
                private static string Select({{spelling}} value) => "typed";
                private static string Select(object value) => "object";
                public static string Run() => Select(default);
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join(Environment.NewLine, converted.Issues));
        Assert.Empty(converted.Losses);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: false);
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(converted.CalorSource!, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        var generated = new List<string> { source };
        foreach (var calor in new[] { converted.CalorSource!, new CalorEmitter().Emit(module) })
        {
            var compiled = Program.Compile(calor, "contextual-default.calr",
                new CompilationOptions { StatusWriter = TextWriter.Null });
            Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
            generated.Add(compiled.GeneratedCode);
        }
        foreach (var csharp in generated)
        {
            var compilation = CSharpCompilation.Create("ContextualDefault_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(csharp)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            var probe = Assembly.Load(stream.ToArray()).GetType("Probe")!;
            var read = probe.GetMethod("Read")!;
            Assert.Equal(expectedType, read.ReturnType);
            Assert.Equal(Activator.CreateInstance(expectedType), read.Invoke(null, null));
            Assert.Equal("typed", probe.GetMethod("Run")!.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("value")]
    [InlineData("enum")]
    [InlineData("generic")]
    [InlineData("collision")]
    public void ConvertedDefaultLiterals_KeepUnsupportedTypesAndCollisionsPreserved(string kind)
    {
        var declaration = kind switch
        {
            "value" => "public struct Value { public int N; public Value() { N = 9; } }",
            "enum" => "public enum Value { One = 1 }",
            "collision" => "public class Base { protected int @default = 3; }",
            _ => ""
        };
        var type = kind is "value" or "enum" ? "Value" : kind == "generic" ? "T" : "int";
        var parameters = kind == "generic" ? "<T>" : "";
        var baseType = kind == "collision" ? " : Base" : "";
        var observation = kind switch
        {
            "value" => "Read().N",
            "enum" => "(int)Read()",
            "generic" => "Read<int>()",
            _ => "Read()"
        };
        var source = $$"""
            {{declaration}}
            public class Probe{{baseType}}
            {
                public static {{type}} Read{{parameters}}() => default;
                public static int Run() => {{observation}};
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.True(converted.Success, string.Join(Environment.NewLine, converted.Issues));
        Assert.Contains(converted.Losses, loss => loss.Kind == ConversionLossKind.InteropPreserved);
        RawFrameAssertions.AssertFramed(converted.CalorSource!, present: true);
        var compiled = Program.Compile(converted.CalorSource!, "preserved-default.calr",
            new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
        foreach (var csharp in new[] { source, compiled.GeneratedCode })
        {
            var compilation = CSharpCompilation.Create("PreservedDefault_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(csharp)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            var run = Assembly.Load(stream.ToArray()).GetType("Probe")!.GetMethod("Run")!;
            Assert.Equal(0, run.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("default(T)")]
    [InlineData("default(int?)")]
    [InlineData("default(System.Int32)")]
    public void NativeDefaults_UnsupportedTypedFormsDoNotBecomeFakeCalls(string source)
    {
        var diagnostics = new DiagnosticBag();
        Parse(source, diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains(
            "requires a built-in primitive type", StringComparison.Ordinal));
        Assert.IsType<CallExpressionNode>(Parse("@default(1)"));
    }

    [Theory]
    [InlineData("1..3")]
    [InlineData("..3")]
    [InlineData("1..")]
    [InlineData("..")]
    [InlineData("^3..^1")]
    [InlineData("(start + 1)..(end - 1)")]
    public void NativeRanges_RetainEndpointsSpansAndRoundtrip(string source)
    {
        var range = Assert.IsType<RangeExpressionNode>(Parse(source));
        Assert.Equal(source, source.Substring(range.Span.Start, range.Span.Length));
        var emitted = range.Accept(new CalorEmitter());
        Assert.DoesNotContain('§', emitted);
        var reparsed = Assert.IsType<RangeExpressionNode>(Parse(emitted));
        Assert.Equal(range.Accept(new CSharpEmitter()), reparsed.Accept(new CSharpEmitter()));
    }

    [Fact]
    public void NativeRanges_UseCSharpPrecedenceAndKeepXorBinary()
    {
        var sum = Assert.IsType<BinaryOperationNode>(Parse("a + b..c * d"));
        Assert.IsType<BinaryOperationNode>(sum.Right);
        Assert.IsType<RangeExpressionNode>(Assert.IsType<BinaryOperationNode>(sum.Right).Left);
        var range = Assert.IsType<RangeExpressionNode>(Parse("-a..^b"));
        Assert.IsType<UnaryOperationNode>(range.Start);
        Assert.IsType<IndexFromEndNode>(range.End);
        var xor = Assert.IsType<BinaryOperationNode>(Parse("a ^ b"));
        Assert.Equal(BinaryOperator.BitwiseXor, xor.Operator);
        var indexSum = Assert.IsType<BinaryOperationNode>(Parse("^a + b"));
        Assert.IsType<IndexFromEndNode>(indexSum.Left);
    }

    [Theory]
    [InlineData("1..2..3")]
    [InlineData("....")]
    [InlineData("^")]
    public void NativeRanges_RejectMissingOffsetsAndUnparenthesizedChains(string source)
    {
        var diagnostics = new DiagnosticBag();
        Parse(source, diagnostics);
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void NativeRanges_KeepArgumentInterpolationAndPhysicalLineBoundaries()
    {
        var call = Assert.IsType<CallExpressionNode>(Parse("Use(1.., ..^1)"));
        Assert.Null(Assert.IsType<RangeExpressionNode>(call.Arguments[0]).End);
        Assert.Null(Assert.IsType<RangeExpressionNode>(call.Arguments[1]).Start);
        var interpolation = Assert.IsType<InterpolatedStringNode>(Parse("\"slice=${text[1..^1]}\""));
        var hole = Assert.Single(interpolation.Parts.OfType<InterpolatedStringExpressionNode>());
        Assert.IsType<RangeExpressionNode>(Assert.IsType<ArrayAccessNode>(hole.Expression).Index);

        const string source = """
            module Ranges
              func Run () → int
                let range = 1..
                return 42
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        var statements = Assert.Single(module.Functions).Body;
        Assert.Equal(2, statements.Count);
        var binding = Assert.IsType<BindStatementNode>(statements[0]);
        Assert.Null(Assert.IsType<RangeExpressionNode>(binding.Initializer).End);
        Assert.IsType<ReturnStatementNode>(statements[1]);
    }

    [Fact]
    public void NativeRanges_RetainLazySingleEndpointEvaluationAndSliceResults()
    {
        foreach (var authored in new[]
        {
            "take ? Numbers()[Start()..^End()] : new int[] { 9 }",
            "take ? Text()[Start()..^End()] : \"no\""
        })
        {
            var expression = Parse(authored);
            foreach (var node in new[] { expression, Parse(expression.Accept(new CalorEmitter())) })
            {
                var resultType = authored.Contains("Numbers") ? "int[]" : "string";
                var generated = node.Accept(new CSharpEmitter());
                var source = $$"""
                    public static class Probe
                    {
                        public static string Trace = "";
                        public static int[] Numbers() { Trace += "T"; return new[] {10, 20, 30, 40}; }
                        public static string Text() { Trace += "T"; return "abcd"; }
                        public static int Start() { Trace += "S"; return 1; }
                        public static int End() { Trace += "E"; return 1; }
                        public static {{resultType}} Run(bool take) => {{generated}};
                    }
                    """;
                var compilation = CSharpCompilation.Create("RangeOrder_" + Guid.NewGuid().ToString("N"),
                    [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using var stream = new MemoryStream();
                var result = compilation.Emit(stream);
                Assert.True(result.Success, source + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
                var type = Assembly.Load(stream.ToArray()).GetType("Probe")!;
                type.GetMethod("Run")!.Invoke(null, [false]);
                Assert.Equal("", type.GetField("Trace")!.GetValue(null));
                var slice = type.GetMethod("Run")!.Invoke(null, [true]);
                Assert.Equal("TSE", type.GetField("Trace")!.GetValue(null));
                if (slice is int[] numbers)
                    Assert.Equal(new[] {20, 30}, numbers);
                else
                    Assert.Equal("bc", slice);
            }
        }
    }

    [Theory]
    [InlineData("item with { X = 1, Y = next() }")]
    [InlineData("item with { }")]
    [InlineData("item with { @class = 1, }")]
    public void NativeRecordClone_UsesExistingOrderedAstAndRoundtrips(string source)
    {
        var clone = Assert.IsType<WithExpressionNode>(Parse(source));
        Assert.Equal("item", Assert.IsType<ReferenceNode>(clone.Target).Name);
        Assert.Equal(source, source.Substring(clone.Span.Start, clone.Span.Length));
        var emitted = clone.Accept(new CalorEmitter());
        Assert.DoesNotContain('§', emitted);
        var reparsed = Assert.IsType<WithExpressionNode>(Parse(emitted));
        Assert.Equal(clone.Assignments.Select(assignment => assignment.PropertyName),
            reparsed.Assignments.Select(assignment => assignment.PropertyName));
    }

    [Fact]
    public void NativeRecordClone_UsesCSharpPrecedence()
    {
        var multiply = Assert.IsType<BinaryOperationNode>(Parse("a * b with { X = 1 }"));
        Assert.IsType<WithExpressionNode>(multiply.Right);
        var clone = Assert.IsType<WithExpressionNode>(Parse("-a with { X = 1 }"));
        Assert.IsType<UnaryOperationNode>(clone.Target);
        var castClone = Assert.IsType<WithExpressionNode>(Parse("(Item)value with { X = 1 }"));
        Assert.IsType<TypeOperationNode>(castClone.Target);
        var reparsedCastClone = Assert.IsType<WithExpressionNode>(Parse(castClone.Accept(new CalorEmitter())));
        Assert.IsType<TypeOperationNode>(reparsedCastClone.Target);
        Assert.IsType<WithExpressionSyntax>(SyntaxFactory.ParseExpression(castClone.Accept(new CSharpEmitter())));
        var conditional = Assert.IsType<ConditionalExpressionNode>(Parse("flag ? a with { X = 1 } : b"));
        Assert.IsType<WithExpressionNode>(conditional.WhenTrue);
    }

    [Fact]
    public void NativeRecordClone_RetainsLazyOrderedSingleEvaluation()
    {
        var expression = Parse("take ? Target() with { X = Next(), Y = Next() } : Original");
        foreach (var node in new[] { expression, Parse(expression.Accept(new CalorEmitter())) })
        {
            var emitted = node.Accept(new CSharpEmitter());
            var source = $$"""
                public record Item(int X, int Y);
                public static class Probe
                {
                    public static string Trace = "";
                    public static Item Original = new(7, 8);
                    public static Item Target() { Trace += "T"; return Original; }
                    public static int Next() { Trace += "N"; return Trace.Length; }
                    public static Item Run(bool take) => {{emitted}};
                }
                """;
            var compilation = CSharpCompilation.Create("CloneOrder_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, source + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
            var type = Assembly.Load(stream.ToArray()).GetType("Probe")!;
            var original = type.GetField("Original")!.GetValue(null);
            Assert.Same(original, type.GetMethod("Run")!.Invoke(null, [false]));
            Assert.Equal("", type.GetField("Trace")!.GetValue(null));
            var clone = type.GetMethod("Run")!.Invoke(null, [true])!;
            Assert.NotSame(original, clone);
            Assert.Equal("TNN", type.GetField("Trace")!.GetValue(null));
            Assert.Equal(2, clone.GetType().GetProperty("X")!.GetValue(clone));
            Assert.Equal(3, clone.GetType().GetProperty("Y")!.GetValue(clone));
        }
    }

    [Theory]
    [InlineData("Call(ref first, out second, in third)")]
    [InlineData("Call(a: ref first, b: out second, c: in third)")]
    public void NativeCallModifiers_RetainKeywordTokensAndOrderedChannels(string source)
    {
        var call = Assert.IsType<CallExpressionNode>(Parse(source));
        Assert.Equal(new[] { "ref", "out", "in" }, call.ArgumentModifiers);
        Assert.Equal(new[] { "first", "second", "third" },
            call.Arguments.Select(argument => Assert.IsType<ReferenceNode>(argument).Name));
        var emitted = call.Accept(new CalorEmitter());
        var reparsed = Assert.IsType<CallExpressionNode>(Parse(emitted));
        Assert.Equal(call.ArgumentModifiers, reparsed.ArgumentModifiers);
        Assert.Equal(call.ArgumentNames, reparsed.ArgumentNames);
    }

    [Theory]
    [InlineData("await next() + 1")]
    [InlineData("await next() * 2")]
    [InlineData("await next() ?? 1")]
    public void Await_UsesUnaryRatherThanWholeExpressionPrecedence(string source)
    {
        var expression = Parse(source);
        var left = expression switch
        {
            BinaryOperationNode binary => binary.Left,
            NullCoalesceNode coalesce => coalesce.Left,
            _ => throw new InvalidOperationException(expression.GetType().Name)
        };
        var awaited = Assert.IsType<AwaitExpressionNode>(left);
        Assert.IsType<CallExpressionNode>(awaited.Awaited);
        Assert.Equal("await next()", source[..awaited.Span.Length]);
        Assert.True(awaited.Span.End < expression.Span.End);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Await_EmitsExplicitConfigurationAsOrdinaryNativeMemberCall(bool configure)
    {
        var node = new AwaitExpressionNode(default, Parse("next()"), configure);
        var emitted = node.Accept(new CalorEmitter());
        Assert.DoesNotContain('§', emitted);
        Assert.Contains($".ConfigureAwait({configure.ToString().ToLowerInvariant()})", emitted);
        var reparsed = Assert.IsType<AwaitExpressionNode>(Parse(emitted));
        var call = Assert.IsType<ExpressionCallNode>(reparsed.Awaited);
        Assert.Equal("ConfigureAwait", Assert.IsType<FieldAccessNode>(call.TargetExpression).FieldName);
        Assert.Equal(configure, Assert.IsType<BoolLiteralNode>(Assert.Single(call.Arguments)).Value);
    }

    [Fact]
    public async Task Await_NativeRoundtripPreservesOrderedSingleEvaluation()
    {
        const string authored = """
            module NativeAwait
              func Run async (Func<Task<int>>:next) → Task<int>
                return await next() + await next()
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(authored, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        foreach (var source in new[] { authored, new CalorEmitter().Emit(module) })
        {
            diagnostics = new DiagnosticBag();
            module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            var generated = new CSharpEmitter().Emit(module);
            var compilation = CSharpCompilation.Create("NativeAwait_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(generated)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, generated + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
            var method = Assembly.Load(stream.ToArray()).GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "Run");
            var observations = new List<int>();
            Func<Task<int>> next = () =>
            {
                observations.Add(observations.Count + 1);
                return Task.FromResult(observations.Count == 1 ? 20 : 22);
            };
            Assert.Equal(42, await Assert.IsAssignableFrom<Task<int>>(method.Invoke(null, [next])));
            Assert.Equal([1, 2], observations);
        }
    }

    [Theory]
    [InlineData("@true")]
    [InlineData("@false")]
    [InlineData("@null")]
    [InlineData("@this")]
    [InlineData("@base")]
    public void EscapedLiteralNames_RemainReferencesAcrossNativeEmissionAndClr(string name)
    {
        var reference = Assert.IsType<ReferenceNode>(Parse(name));
        Assert.True(reference.IsVerbatim);
        Assert.False(reference.IsNullLiteral);
        Assert.Equal(name[1..], reference.Name);
        var authored = $"module NativeIdentity\n  func Run (string:{name}) → string\n    return {name}";
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(authored, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        foreach (var source in new[] { authored, new CalorEmitter().Emit(module) })
        {
            diagnostics = new DiagnosticBag();
            module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            var checkedResult = Program.Compile(source, "native-identity.calr", new CompilationOptions
            {
                EnableTypeChecking = true,
                EnforceEffects = true,
                StatusWriter = TextWriter.Null
            });
            Assert.False(checkedResult.HasErrors, string.Join(Environment.NewLine, checkedResult.Diagnostics.Errors));
            var generated = new CSharpEmitter().Emit(module);
            var compilation = CSharpCompilation.Create("NativeIdentity_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(generated)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, generated + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
            var method = Assembly.Load(stream.ToArray()).GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "Run");
            Assert.Equal("value", method.Invoke(null, ["value"]));
        }
    }

    [Fact]
    public void UnescapedLiterals_RetainTheirNativeMeaning()
    {
        Assert.True(Assert.IsType<ReferenceNode>(Parse("null")).IsNullLiteral);
        Assert.Equal("null", Parse("null").Accept(new CSharpEmitter()));
        Assert.True(Assert.IsType<BoolLiteralNode>(Parse("true")).Value);
        Assert.False(Assert.IsType<BoolLiteralNode>(Parse("false")).Value);
        var context = new ConversionContext();
        var source = CSharpSyntaxTree.ParseText("class C { string M(string @null) => @null; }");
        var converted = new RoslynSyntaxVisitor(context).Convert(source.GetCompilationUnitRoot(), "Identity");
        var reference = Assert.IsType<ReferenceNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(Assert.Single(Assert.Single(converted.Classes).Methods).Body)).Expression);
        Assert.True(reference.IsVerbatim);
        Assert.False(reference.IsNullLiteral);
    }

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
    public void QuantifierBodies_EmitThroughTheSelectedGeneralGuardForm()
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
            var emitted = quantifier.Accept(new CalorEmitter());
            Assert.DoesNotContain('§', emitted);
            Assert.Contains("int:i where true", emitted);
            Assert.Equal(quantifier.GetType(), Parse(emitted).GetType());
        }
    }

    [Theory]
    [InlineData("forall int:i in 0..xs.Length : xs[i] >= 0", true)]
    [InlineData("exists int:i in 0..xs.Length : xs[i] < 0", false)]
    [InlineData("forall int:i in 0..n, int:j in i..n : i <= j", true)]
    [InlineData("exists int:i in 0..n, int:j in i..n : i == j", false)]
    [InlineData("forall int:i in xs[1..^1].Length + 1..n - 1 : i >= 0", true)]
    [InlineData("exists int:i in Limit(1..^1)..n : i >= 0", false)]
    [InlineData("forall int:i where i >= 0 && i < n : i < n", true)]
    [InlineData("exists int:i where i >= 0 && i < n : i == 1", false)]
    public void Quantifiers_PreserveNativeAstBindingsAndAuthoredSpans(string source, bool universal)
    {
        var node = Parse(source);
        Assert.Equal(source.Length, node.Span.Length);
        foreach (var expression in new[] { node, Parse(node.Accept(new CalorEmitter())) })
        {
            var variables = universal ? Assert.IsType<ForallExpressionNode>(expression).BoundVariables
                : Assert.IsType<ExistsExpressionNode>(expression).BoundVariables;
            Assert.Equal(source.Contains("int:j") ? 2 : 1, variables.Count);
            Assert.All(variables, variable => Assert.Equal("int", variable.TypeName));
            Assert.Equal("i", variables[0].Name);
        }
        var authored = universal ? ((ForallExpressionNode)node).BoundVariables
            : ((ExistsExpressionNode)node).BoundVariables;
        foreach (var variable in authored)
        {
            Assert.Equal(variable.Name, source.Substring(variable.IdentifierSpan.Start, variable.IdentifierSpan.Length));
            Assert.Equal("int", source.Substring(variable.TypeNameSpan.Start, variable.TypeNameSpan.Length));
        }
    }

    [Theory]
    [InlineData("forall")]
    [InlineData("exists")]
    public void QuantifierNames_RemainOrdinaryWhenCalledOrEscaped(string name)
    {
        Assert.IsType<CallExpressionNode>(Parse($"{name}(x)"));
        Assert.IsType<CallExpressionNode>(Parse($"@{name}(x)"));
        Assert.IsType<CallExpressionNode>(Parse($"{name}(x: 1)"));
        Assert.IsType<ReferenceNode>(Parse($"@{name}"));
    }

    [Theory]
    [InlineData("(int:x) ! cw => Log(x)", true)]
    [InlineData("(int:x) ! => x + 1", true)]
    [InlineData("(int:x) => x + 1", false)]
    [InlineData("(Func<int,int>:f) ! => f(1)", true)]
    [InlineData("() ! cw =>\n  Log(1)", true)]
    public void LambdaEffectRows_PreserveExplicitEmptyVersusAbsentAndNativePosition(string source, bool hasRow)
    {
        var lambda = Assert.IsType<LambdaExpressionNode>(Parse(source));
        Assert.Equal(hasRow, lambda.Effects is not null);
        var emitted = lambda.Accept(new CalorEmitter());
        Assert.DoesNotContain('§', emitted);
        var reparsed = Assert.IsType<LambdaExpressionNode>(Parse(emitted));
        Assert.Equal(hasRow, reparsed.Effects is not null);
        Assert.Equal(lambda.Parameters.Select(parameter => parameter.TypeName),
            reparsed.Parameters.Select(parameter => parameter.TypeName));
        if (hasRow)
            Assert.Equal(lambda.Effects!.Effects, reparsed.Effects!.Effects);
    }

    [Theory]
    [InlineData("(int:x) ! notAnEffect => x")]
    [InlineData("(int:x) ! cw,\n  => x")]
    [InlineData("(int:x)\n! cw => x")]
    public void LambdaEffectRows_RejectUnknownCodesAndCrossLineHeaders(string source)
    {
        var diagnostics = new DiagnosticBag();
        Parse(source, diagnostics);
        Assert.True(diagnostics.HasErrors);
    }

    [Theory]
    [InlineData("forall int:i in 0..n :")]
    [InlineData("exists int:i where i >= 0 :")]
    [InlineData("forall int:i in 0..")]
    [InlineData("forall int:i in 0..n,")]
    public void QuantifierRecovery_DoesNotConsumeTheFollowingReturn(string expression)
    {
        var source = $"module Q\n  func Run (int:n) → bool\n    requires {expression}\n    return true";
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
        Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
    }

    [Fact]
    public void NativeQuantifiers_ExecuteHalfOpenEmptyDependentAndArrayRanges()
    {
        const string authored = """
            module NativeQuantifiers
              func All (int:lo, int:hi) → bool
                return forall int:i in lo..hi : i != 2
              func Any (int:lo, int:hi) → bool
                return exists int:i in lo..hi : i == 2
              func Dependent (int:n) → bool
                return forall int:i in 0..n, int:j in i..n : i <= j
              func Array (int[]:xs) → bool
                return forall int:i in 0..xs.Length : xs[i] >= 0
              func Lazy (int:lo, int:hi) → bool
                return forall int:i in lo..hi : 10 / i > 0
              func Scoped (int:i) → bool
                return (exists int:i in 0..3 : i == 2) && i == 42
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(authored, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        foreach (var source in new[] { authored, new CalorEmitter().Emit(module) })
        {
            var checkedResult = Program.Compile(source, "native-quantifiers.calr",
                new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
            Assert.False(checkedResult.HasErrors, string.Join(Environment.NewLine, checkedResult.Diagnostics.Errors));
            diagnostics = new DiagnosticBag();
            module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            var generated = new CSharpEmitter().Emit(module);
            var compilation = CSharpCompilation.Create("NativeQuantifiers_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(generated)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, generated + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
            var methods = Assembly.Load(stream.ToArray()).GetTypes().SelectMany(type => type.GetMethods())
                .Where(method => method.IsStatic)
                .ToDictionary(method => method.Name);
            foreach (var (lo, hi) in new[] { (0, 0), (3, 1), (-2, 2), (0, 3), (2, 3) })
            {
                var containsTwo = lo <= 2 && 2 < hi;
                Assert.Equal(!containsTwo, methods["All"].Invoke(null, [lo, hi]));
                Assert.Equal(containsTwo, methods["Any"].Invoke(null, [lo, hi]));
            }
            Assert.Equal(true, methods["Dependent"].Invoke(null, [4]));
            Assert.Equal(true, methods["Array"].Invoke(null, [System.Array.Empty<int>()]));
            Assert.Equal(true, methods["Array"].Invoke(null, [new[] { 0, 1, 2 }]));
            Assert.Equal(false, methods["Array"].Invoke(null, [new[] { 0, -1, 2 }]));
            Assert.Equal(true, methods["Lazy"].Invoke(null, [0, 0]));
            Assert.IsType<DivideByZeroException>(
                Assert.Throws<TargetInvocationException>(() => methods["Lazy"].Invoke(null, [0, 1])).InnerException);
            Assert.Equal(true, methods["Scoped"].Invoke(null, [42]));
            Assert.Equal(false, methods["Scoped"].Invoke(null, [43]));
        }
    }

    [Fact]
    public void NativeQuantifierArrayLength_UsesTheExistingModeledSolverOperation()
    {
        var expression = Parse("forall int:i in 0..xs.Length : xs[i] == xs[i]");
        foreach (var node in new[] { expression, Parse(expression.Accept(new CalorEmitter())) })
        {
            using var context = Calor.Compiler.Verification.Z3.Z3ContextFactory.Create();
            var translator = new Calor.Compiler.Verification.Z3.ContractTranslator(context);
            Assert.True(translator.DeclareVariable("xs", "int[]"));
            var normalized = Assert.IsType<ForallExpressionNode>(translator.NormalizeBclExpression(node));
            var guard = Assert.IsType<BinaryOperationNode>(
                Assert.IsType<ImplicationExpressionNode>(normalized.Body).Antecedent);
            Assert.IsType<ArrayLengthNode>(Assert.IsType<BinaryOperationNode>(guard.Right).Right);
            var formula = Assert.IsAssignableFrom<Microsoft.Z3.BoolExpr>(translator.Translate(normalized));
            using var solver = context.MkSolver();
            solver.Assert(context.MkNot(formula));
            Assert.Equal(Microsoft.Z3.Status.UNSATISFIABLE, solver.Check());
        }
    }

    [Fact]
    public void EffectfulLambda_NativeRoundtripKeepsInvocationDelayed()
    {
        const string authored = """
            module NativeEffectfulLambda
              func Build (Action<int>:log ! cw) → Action<int>
                ! cw
                return (int:x) ! cw =>
                  log(x)
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(authored, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        foreach (var source in new[] { authored, new CalorEmitter().Emit(module) })
        {
            var checkedResult = Program.Compile(source, "native-effectful-lambda.calr",
                new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
            Assert.False(checkedResult.HasErrors, string.Join(Environment.NewLine, checkedResult.Diagnostics.Errors));
            diagnostics = new DiagnosticBag();
            module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
            var generated = new CSharpEmitter().Emit(module);
            var compilation = CSharpCompilation.Create("NativeEffectfulLambda_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(generated)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, generated + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
            var method = Assembly.Load(stream.ToArray()).GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "Build");
            var observations = new List<int>();
            Action<int> log = observations.Add;
            var action = Assert.IsType<Action<int>>(method.Invoke(null, [log]));
            Assert.Empty(observations);
            action(42);
            Assert.Equal([42], observations);
        }
    }

    [Theory]
    [InlineData("cw", false)]
    [InlineData("", true)]
    public void NativeLambdaRows_RetainDeclaredBodyEffectChecks(string row, bool forbidden)
    {
        var source = $"""
            module NativeLambdaRows
              func Build (Action<int>:log ! cw) → Action<int>
                ! cw
                return (int:x) ! {row} => log(x)
            """;
        var result = Program.Compile(source, "native-lambda-rows.calr",
            new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });
        Assert.Equal(forbidden, result.HasErrors);
        Assert.Equal(forbidden, result.Diagnostics.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.StartsWith("Lambda", StringComparison.Ordinal)));
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
    public void AssignedBlockLambda_PreservesComputedLvalueOrderAndDelayedBody()
    {
        const string authored = """
            module NativeAssignment
              func Run (Func<Func<int>[]>:factory, Func<int>:index, Func<int>:next) → int
                set factory()[index()] () =>
                  return next()
                return 0
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(authored, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        Assert.DoesNotContain("_hoist", emitted);
        foreach (var source in new[] { authored, emitted })
        {
            diagnostics = new DiagnosticBag();
            module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, source + Environment.NewLine + string.Join(Environment.NewLine, diagnostics.Errors));
            var generated = new CSharpEmitter().Emit(module);
            var compilation = CSharpCompilation.Create("AssignedLambda_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(generated)], GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(result.Success, generated + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
            var method = Assembly.Load(stream.ToArray()).GetTypes().SelectMany(type => type.GetMethods())
                .Single(method => method.Name == "Run");
            var trace = new List<string>();
            var slots = new Func<int>[1];
            Func<Func<int>[]> factory = () => { trace.Add("F"); return slots; };
            Func<int> index = () => { trace.Add("I"); return 0; };
            Func<int> next = () => { trace.Add("N"); return 42; };
            Assert.Equal(0, method.Invoke(null, [factory, index, next]));
            Assert.Equal(["F", "I"], trace);
            Assert.Equal(42, slots[0]());
            Assert.Equal(["F", "I", "N"], trace);
        }
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
