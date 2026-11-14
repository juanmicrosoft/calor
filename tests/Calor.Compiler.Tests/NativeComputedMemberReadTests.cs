using System.Reflection;
using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;
using NominalBoundType = Calor.Compiler.Binding.BoundTypes.NominalBoundType;
using NullableAnnotation = Calor.Compiler.Binding.BoundTypes.NullableAnnotation;

namespace Calor.Compiler.Tests;

public sealed class NativeComputedMemberReadTests
{
    private const string ExactSource = """
        module CoalescedMember
          func Length (string?:value) → int
            !
            return (value ?? "none").Length
        """;

    [Fact]
    public void CoalescedStringLength_OrdinaryCliProducesExecutableOutput()
    {
        var directory = Path.Combine(Path.GetTempPath(), "calor-computed-member-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "CoalescedMember.calr");
            var output = Path.Combine(directory, "CoalescedMember.g.cs");
            File.WriteAllText(input, ExactSource);
            var cli = CliTestHarness.RunCli(directory, "--no-telemetry", "--input", input, "--output", output);
            Assert.True(cli.ExitCode == 0, cli.StdOut + cli.StdErr);
            Assert.DoesNotContain("Calor0411", cli.StdOut + cli.StdErr);
            Assert.DoesNotContain("Calor0410", cli.StdOut + cli.StdErr);
            Assert.True(File.Exists(output), cli.StdOut + cli.StdErr);
            var method = Method(CompileAssembly(File.ReadAllText(output)), "Length");
            Assert.Equal(4, method.Invoke(null, [null]));
            Assert.Equal(3, method.Invoke(null, ["abc"]));
            Assert.Equal(0, method.Invoke(null, [""]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CoalescedStringLength_RetainsSelectedPropertyReceiverResultAndSourceSpans()
    {
        foreach (var source in new[] { ExactSource, new CalorEmitter().Emit(Parse(ExactSource)) })
        {
            var (module, bound) = Bind(source);
            var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
            var receiver = Assert.IsType<BoundStructuralExpression>(member.Target);
            Assert.Equal(nameof(NullCoalesceNode), receiver.NodeTypeName);
            Assert.Equal(2, receiver.Children.Count);
            Assert.Same(receiver.Children[1], Assert.Single(receiver.DeferredChildren));
            AssertStringIdentity(receiver, NullableAnnotation.NotAnnotated);
            AssertStringIdentity(receiver.Children[0], NullableAnnotation.Annotated);
            AssertStringIdentity(receiver.Children[1], NullableAnnotation.NotAnnotated);
            AssertLength(member);
            Assert.Equal("Length", source.Substring(member.FieldNameSpan.Start, member.FieldNameSpan.Length));
            var astMember = Assert.IsType<FieldAccessNode>(
                Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions).Body[0]).Expression);
            Assert.Equal(astMember.Span, member.Span);
            Assert.Equal(astMember.Target.Span, receiver.Span);
            Assert.Equal(astMember.FieldNameSpan, member.FieldNameSpan);
            var graph = CallGraphAnalysis.Build(module);
            var caller = Assert.Single(graph.Functions).Key;
            Assert.True(SymbolEqualityComparer.Default.Equals(
                Assert.IsType<NominalBoundType>(receiver.Type).RoslynSymbol,
                graph.MetadataValueType(caller, receiver.Span)));
            Assert.Equal(SpecialType.System_String, graph.MetadataMemberReceiverType(caller, member.Span)!.RoslynSymbol!.SpecialType);
            AssertSuccessful(source);
        }
    }

    [Theory]
    [InlineData("(value ?? \"none\").Length", 3)]
    [InlineData("(gate ? value : \"none\").Length", 3)]
    [InlineData("(gate ? \"first\" : \"none\").Length", 5)]
    [InlineData("(Text(value)).Length", 3)]
    [InlineData("(Text(value) ?? \"none\").Length", 3)]
    [InlineData("(System.Convert.ToString(12) ?? \"none\").Length", 2)]
    [InlineData("(\"abc\").Length", 3)]
    [InlineData("(nameof(value)).Length", 5)]
    public void SupportedStringReceivers_UseActualStringProperty(string expression, int expected)
    {
        var source = $$"""
            module ComputedStrings
              func Text (string?:value) → string?
                !
                return value
              func Read (string?:value, bool:gate) → int
                !
                return {{expression}}
            """;
        var (_, bound) = Bind(source);
        var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        AssertLength(member);
        Assert.Equal(SpecialType.System_String, Assert.IsType<NominalBoundType>(member.Target.Type).RoslynSymbol!.SpecialType);
        var result = AssertSuccessful(source);
        Assert.Equal(expected, Method(CompileAssembly(result.GeneratedCode), "Read").Invoke(null, ["abc", true]));
    }

    [Theory]
    [InlineData("(value ?? other).Length", NullableAnnotation.Annotated)]
    [InlineData("(value ?? null).Length", NullableAnnotation.Annotated)]
    [InlineData("(gate ? value : other).Length", NullableAnnotation.Annotated)]
    [InlineData("(null ?? \"none\").Length", NullableAnnotation.NotAnnotated)]
    public void NullableResiduals_KeepTheirAnnotationRatherThanClaimingNonNull(
        string expression, NullableAnnotation annotation)
    {
        var source = $$"""
            module NullableMembers
              func Read (string?:value, string?:other, bool:gate) → int
                !
                return {{expression}}
            """;
        var (_, bound) = Bind(source);
        var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        AssertLength(member);
        AssertStringIdentity(member.Target, annotation);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReceiverAndFallbackEffects_AreIndependentlyCharged(bool receiverWrites, bool fallbackWrites)
    {
        var source = $$"""
            module ComputedEffects
              func Receiver (string?:value) → string?
                !
                {{(receiverWrites ? "! cw" : "!")}}
                {{(receiverWrites ? "System.Console.WriteLine(\"receiver\")" : "")}}
                return value
              func Fallback () → string
                !
                {{(fallbackWrites ? "! cw" : "!")}}
                {{(fallbackWrites ? "System.Console.WriteLine(\"fallback\")" : "")}}
                return "none"
              func Read (string?:value) → int
                !
                return (Receiver(value) ?? Fallback()).Length
            """;
        var rejected = Program.Compile(source);
        Assert.Contains(rejected.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect && diagnostic.Message.Contains("'cw'"));
        Assert.DoesNotContain(rejected.Diagnostics, diagnostic => diagnostic.Code == "Calor0411");
        AssertSuccessful(source.Replace("func Read (string?:value) → int\n    !",
            "func Read (string?:value) → int\n    ! cw", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, 4, new[] { 1, 2 })]
    [InlineData("abc", 3, new[] { 1 })]
    public void Coalesce_EvaluatesReceiverOnceAndFallbackLazilyInOrder(string? value, int expected, int[] events)
    {
        const string authored = """
            module LazyMember
              func Receiver (List<int>:trace, string?:value) → string?
                ! mut
                trace.Add(1)
                return value
              func Fallback (List<int>:trace) → string
                ! mut
                trace.Add(2)
                return "none"
              func Read (List<int>:trace, string?:value) → int
                ! mut
                return (Receiver(trace, value) ?? Fallback(trace)).Length
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        {
            var result = AssertSuccessful(source);
            var trace = new List<int>();
            Assert.Equal(expected, Method(CompileAssembly(result.GeneratedCode), "Read").Invoke(null, [trace, value]));
            Assert.Equal(events, trace);
        }
    }

    [Theory]
    [InlineData(true, 3, new[] { 0, 1 })]
    [InlineData(false, 4, new[] { 0, 2 })]
    public void Conditional_EvaluatesConditionOnceAndOnlyTheSelectedReceiver(bool take, int expected, int[] events)
    {
        const string source = """
            module ConditionalMember
              func Gate (List<int>:trace, bool:take) → bool
                ! mut
                trace.Add(0)
                return take
              func First (List<int>:trace) → string
                ! mut
                trace.Add(1)
                return "abc"
              func Second (List<int>:trace) → string
                ! mut
                trace.Add(2)
                return "none"
              func Read (List<int>:trace, bool:take) → int
                ! mut
                return (Gate(trace, take) ? First(trace) : Second(trace)).Length
            """;
        var result = AssertSuccessful(source);
        var trace = new List<int>();
        Assert.Equal(expected, Method(CompileAssembly(result.GeneratedCode), "Read").Invoke(null, [trace, take]));
        Assert.Equal(events, trace);
        var rejected = Program.Compile(source.Replace("func Read (List<int>:trace, bool:take) → int\n    ! mut",
            "func Read (List<int>:trace, bool:take) → int\n    !", StringComparison.Ordinal));
        Assert.Contains(rejected.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("(value ?? \"none\").Missing", "string?")]
    [InlineData("(value ?? value).Length", "Missing")]
    [InlineData("(value ?? value).Length", "T")]
    [InlineData("(value ?? value).Length", "object")]
    [InlineData("(value ?? value).Length", "List<int>")]
    [InlineData("(value ?? \"none\").Length", "T")]
    public void UnsupportedOrForeignReceivers_DoNotAcquireStringLength(
        string expression, string type)
    {
        var source = $$"""
            module ConservativeMembers
              func Read{{(type == "T" ? "<T>" : "")}} ({{type}}:value) → int
                !
                return {{expression}}
            """;
        var (_, bound) = Bind(source);
        var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        Assert.False(member.ResolvedMetadataMember is IPropertySymbol
            { ContainingType.SpecialType: SpecialType.System_String, Name: "Length" });
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void SameNamedSourceReceiver_DoesNotAcquireStringMetadata()
    {
        const string source = """
            module SourceString
              class String
              func Read (@String:value) → int
                !
                return (value ?? value).Length
            """;
        var (_, bound) = Bind(source);
        var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        Assert.Null(member.ResolvedMetadataMember);
        Assert.True(member.HasUnknownInferredType);
        Assert.Null(Assert.IsType<NominalBoundType>(member.Target.Type).RoslynSymbol);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("System.Text", "StringBuilder")]
    [InlineData("String = System.Text.StringBuilder", "@String")]
    public void ForeignLengthProperty_RetainsItsOwnSelectedMetadataRatherThanStringPurity(string import, string type)
    {
        var source = $$"""
            module ForeignLength
              using {{import}}
              func Read ({{type}}:value) → int
                !
                return (value ?? value).Length
            """;
        var (_, bound) = Bind(source);
        var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        var property = Assert.IsAssignableFrom<IPropertySymbol>(member.ResolvedMetadataMember);
        Assert.Equal("System.Text.StringBuilder", property.ContainingType.ToDisplayString());
        Assert.Equal(SpecialType.System_Int32, property.Type.SpecialType);
        var result = AssertSuccessful(source);
        Assert.Equal(7, Method(CompileAssembly(result.GeneratedCode), "Read").Invoke(null, [new System.Text.StringBuilder("example")]));
    }

    [Fact]
    public void ShadowedQualifiedString_DoesNotAcquirePredefinedStringIdentity()
    {
        const string source = """
            module QualifiedShadow
              class System
                class String
              func Read (System.String:value) → int
                !
                return (value ?? "none").Length
            """;
        var (_, bound) = Bind(source);
        var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        Assert.Null(member.ResolvedMetadataMember);
        Assert.Null(Assert.IsType<NominalBoundType>(member.Target.Type).RoslynSymbol);
        Assert.True(member.HasUnknownInferredType);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Theory]
    [InlineData("  class String\n", "@String")]
    [InlineData("  class String\n  using Text = @String\n", "Text")]
    [InlineData("  module Domain\n    class String\n  using Text = Domain.String\n", "Text")]
    public void SourceTypesAndAliases_DoNotResolveThroughAMetadataOnlyLookup(string declarations, string type)
    {
        var source = $$"""
            module SourceAlias
            {{declarations}}  func Read ({{type}}:value) → int
                !
                return (value ?? "none").Length
            """;
        var (_, bound) = Bind(source);
        var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        var receiver = Assert.IsType<BoundStructuralExpression>(member.Target);
        var left = Assert.IsType<NominalBoundType>(receiver.Children[0].Type);
        Assert.NotNull(left.Declaration);
        Assert.Null(left.RoslynSymbol);
        Assert.Null(Assert.IsType<NominalBoundType>(receiver.Type).RoslynSymbol);
        Assert.Null(member.ResolvedMetadataMember);
        Assert.True(member.HasUnknownInferredType);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void OpaqueReceiverCall_DoesNotBorrowStringMetadataOrPurity()
    {
        const string opaque = "public static string Text() { System.Console.WriteLine(\"opaque\"); return \"abc\"; }";
        var source = $$"""
            module OpaqueString
              raw member {{System.Text.Encoding.UTF8.GetByteCount(opaque)}}
            {{opaque}}
              func Read () → int
                !
                return (Text() ?? "none").Length
            """;
        var (_, bound) = Bind(source);
        var member = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundFieldAccessExpression>());
        Assert.Null(member.ResolvedMetadataMember);
        Assert.True(member.HasUnknownInferredType);
        Assert.True(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void Join_DoesNotBorrowAStringIdentityFromOneUnresolvedOrDifferentAlternative()
    {
        var (_, bound) = Bind(ExactSource);
        var genuine = Assert.IsType<BoundFieldAccessExpression>(
            Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression).Target.Children[1];
        var missingIdentity = new BoundVariableExpression(default, new VariableSymbol(
            "value", "STRING", false, default));
        var (_, foreign) = Bind("""
            module ForeignIdentity
              using System.Text
              func Read (StringBuilder:value) → int
                !
                return (value ?? value).Length
            """);
        var foreignMember = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(foreign).OfType<BoundFieldAccessExpression>());
        var foreignType = Assert.IsType<NominalBoundType>(foreignMember.Target.Type).RoslynSymbol;
        var differentIdentity = new BoundVariableExpression(default, new VariableSymbol(
            "foreign", "STRING", false, default), typeOverride:
            new NominalBoundType("STRING", NullableAnnotation.NotAnnotated, roslynSymbol: foreignType));
        foreach (var alternative in new BoundExpression[] { missingIdentity, differentIdentity })
        {
            var joined = new BoundConditionalExpression(default, new BoundBoolLiteral(default, true),
                genuine, alternative, "STRING");
            Assert.Null(Assert.IsType<NominalBoundType>(joined.Type).RoslynSymbol);
        }
    }

    private static void AssertStringIdentity(BoundExpression expression, NullableAnnotation annotation)
    {
        Assert.False(expression.HasUnknownInferredType);
        var type = Assert.IsType<NominalBoundType>(expression.Type);
        Assert.Equal(annotation, type.NullableAnnotation);
        Assert.Null(type.Declaration);
        Assert.Equal(SpecialType.System_String, type.RoslynSymbol!.SpecialType);
    }

    private static void AssertLength(BoundFieldAccessExpression member)
    {
        Assert.False(member.HasUnknownInferredType);
        var property = Assert.IsAssignableFrom<IPropertySymbol>(member.ResolvedMetadataMember);
        Assert.Equal("Length", property.Name);
        Assert.False(property.IsStatic);
        Assert.Equal(SpecialType.System_String, property.ContainingType.SpecialType);
        Assert.Equal(SpecialType.System_Int32, property.Type.SpecialType);
        Assert.Equal("INT", member.Type.DisplayString);
        Assert.Equal(SpecialType.System_Int32, Assert.IsType<NominalBoundType>(member.Type).RoslynSymbol!.SpecialType);
    }

    private static CompilationResult AssertSuccessful(string source)
    {
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return result;
    }

    private static (ModuleNode Module, BoundModule Bound) Bind(string source)
    {
        var module = Parse(source);
        return (module, new Binder(new DiagnosticBag()).Bind(module));
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        return module;
    }

    private static MethodInfo Method(Assembly assembly, string name) =>
        assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(method => method.Name == name);

    private static Assembly CompileAssembly(string source)
    {
        var compilation = CSharpCompilation.Create("ComputedMembers_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, source + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
