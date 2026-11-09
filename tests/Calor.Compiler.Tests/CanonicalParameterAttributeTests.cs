using System.Reflection;
using System.Runtime.Loader;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalParameterAttributeTests
{
    [Theory]
    [InlineData("func Read (int:value ATTR) → int\n    return value")]
    [InlineData("class Box\n    func Read (int:value ATTR) → int\n      return value")]
    [InlineData("module Box\n    func Read (int:value ATTR) → int\n      return value")]
    [InlineData("interface IBox\n    func Read (int:value ATTR) → int")]
    [InlineData("class Box\n    ctor (int:value ATTR)")]
    [InlineData("class Box\n    this[int:value ATTR] → int\n      get\n        return value")]
    [InlineData("delegate Read (int:value ATTR) → int")]
    [InlineData("record Box (int:Value ATTR)")]
    [InlineData("class Box\n    op + (Box:left ATTR, Box:right) → Box\n      return left")]
    public void EveryInlineCallablePreservesParameterAttributes(string declaration)
    {
        var source = "module Attributes\n  " + declaration.Replace(
            "ATTR", "[@System.ComponentModel.Description(\"native\")]", StringComparison.Ordinal);
        foreach (var input in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var module = Parse(input);
            var parameter = Assert.Single(Descendants(module).OfType<ParameterNode>()
                .Distinct<ParameterNode>(ReferenceEqualityComparer.Instance)
                .Where(parameter => parameter.CSharpAttributes.Count > 0));
            var attribute = Assert.Single(parameter.CSharpAttributes);
            Assert.Equal("System.ComponentModel.Description", attribute.Name);
            Assert.Equal("native", Assert.Single(attribute.Arguments).Value);
            Assert.Equal(parameter.Name,
                input.Substring(parameter.IdentifierSpan.Start, parameter.IdentifierSpan.Length));
            Assert.Equal(parameter.TypeName,
                input.Substring(parameter.TypeNameSpan.Start, parameter.TypeNameSpan.Length));
            var compiled = Compile(new CSharpEmitter().Emit(module));
            Assert.Contains(
                Types(compiled.Assembly.GlobalNamespace)
                    .SelectMany(type => type.GetMembers().OfType<IMethodSymbol>())
                    .SelectMany(method => method.Parameters)
                    .SelectMany(parameter => parameter.GetAttributes()),
                attribute => attribute.AttributeClass?.ToDisplayString()
                    == "System.ComponentModel.DescriptionAttribute"
                    && attribute.ConstructorArguments.Single().Value is "native");
        }
    }

    [Fact]
    public void ParameterMetadataPrecedesEffectRowsAndDefaultsWithoutChangingThem()
    {
        var module = Parse("""
            module Attributes
              using System
              func Apply (Func<int>:action [@System.ComponentModel.Description("callback")] ! cw, int:value [@System.ComponentModel.Description("default")] = 2) → int
                return value
            """);
        var emitted = new CalorEmitter().Emit(module);
        var reparsed = Parse(emitted);
        Assert.NotNull(reparsed.Functions.Single().Parameters[0].Row);
        Assert.NotNull(reparsed.Functions.Single().Parameters[1].DefaultValue);
        Assert.Equal(new[] { "callback", "default" }, reparsed.Functions.Single().Parameters
            .Select(parameter => Assert.Single(Assert.Single(parameter.CSharpAttributes).Arguments).Value));
        Compile(new CSharpEmitter().Emit(reparsed));
    }

    [Fact]
    public void MultipleParameterAttributesPreserveEscapedNamesAndLiteralArguments()
    {
        var source = """
            module Attributes
              func Read (int:@event [@System.ComponentModel.Description("quote \" slash \\ newline \n ${value}")][@System.Runtime.CompilerServices.CallerLineNumber] = 2) → int
                return @event
            """;
        foreach (var input in new[] { source, new CalorEmitter().Emit(Parse(source)) })
        {
            var module = Parse(input);
            var parameter = module.Functions.Single().Parameters.Single();
            Assert.Equal("event", parameter.Name);
            Assert.Equal("@event",
                input.Substring(parameter.IdentifierSpan.Start, parameter.IdentifierSpan.Length));
            Assert.Equal(2, parameter.CSharpAttributes.Count);
            Assert.Equal("quote \" slash \\ newline \n ${value}",
                Assert.Single(parameter.CSharpAttributes[0].Arguments).Value);
            Assert.Empty(parameter.CSharpAttributes[1].Arguments);
            Compile(new CSharpEmitter().Emit(module));
        }
    }

    [Theory]
    [InlineData("public class Box { public int Read(ATTR int value) => value; }", true)]
    [InlineData("public static class Box { public static int Read(ATTR int value) => value; }", true)]
    [InlineData("public interface IBox { int Read(ATTR int value); }", true)]
    [InlineData("public class Box { public Box(ATTR int value) { } }", true)]
    [InlineData("public class Box { public int this[ATTR int value] => value; }", true)]
    [InlineData("public delegate int Read(ATTR int value);", true)]
    [InlineData("public record Box(ATTR int Value);", false)]
    [InlineData("public class Box { public static Box operator +(ATTR Box left, Box right) => left; }", true)]
    public void ConverterRetainsAttributedParametersAndExistingPreservationPolicy(string declaration, bool native)
    {
        var source = "using System.ComponentModel;\n" + declaration.Replace(
            "ATTR", "[Description(\"native\")]", StringComparison.Ordinal);
        var original = Compile(source);
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Context.Issues));
        Assert.NotNull(conversion.CalorSource);
        var module = Parse(conversion.CalorSource);
        if (native)
            Assert.Empty(Descendants(module).OfType<CSharpInteropBlockNode>());
        else
        {
            var preserved = Assert.Single(Descendants(module).OfType<CSharpInteropBlockNode>());
            Assert.Equal("record-positional-parameter-metadata", preserved.FeatureName);
            Assert.Equal(declaration.Replace("ATTR", "[Description(\"native\")]", StringComparison.Ordinal),
                preserved.CSharpCode);
        }
        var result = Program.Compile(conversion.CalorSource);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var converted = Compile(result.GeneratedCode);
        Assert.Equal(DescriptionParameters(original), DescriptionParameters(converted));
        Assert.NotEmpty(DescriptionParameters(converted));
    }

    [Theory]
    [InlineData("param")]
    [InlineData("property")]
    public void RecordParameterMetadataKeepsItsExplicitClrTarget(string target)
    {
        var source = $"module Attributes\n  record Box (int:Value [@{target}:System.ComponentModel.Description(\"native\")])";
        var reparsed = Parse(new CalorEmitter().Emit(Parse(source)));
        var record = Assert.Single(reparsed.Classes);
        Assert.Equal(target, Assert.Single(Assert.Single(record.RecordParameters!).CSharpAttributes).Target);
        var type = Compile(new CSharpEmitter().Emit(reparsed)).GetTypeByMetadataName("Box")!;
        var attributes = target == "property"
            ? type.GetMembers("Value").OfType<IPropertySymbol>().Single().GetAttributes()
            : type.InstanceConstructors.Single(constructor => constructor.Parameters.Length == 1
                && constructor.Parameters[0].Name == "Value").Parameters[0].GetAttributes();
        Assert.Equal("native", Assert.Single(attributes.Where(attribute =>
            attribute.AttributeClass?.Name == "DescriptionAttribute")).ConstructorArguments.Single().Value);
    }

    [Fact]
    public void OriginalNotNullWhenFixturePreservesNativeMetadataAndActualOutStorage()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "Calor.sln")))
            root = Directory.GetParent(root)!.FullName;
        var original = File.ReadAllText(Path.Combine(root,
            "tests/TestData/InteropScope/W1/fixtures/W1-F1-notnullwhen.cs.txt"));
        var conversion = new CSharpToCalorConverter().Convert(original);
        Assert.True(conversion.Success, string.Join("\n", conversion.Context.Issues));
        Assert.NotNull(conversion.CalorSource);
        Assert.True(conversion.CalorSource.Contains("out string?:name [@NotNullWhen(true)]", StringComparison.Ordinal),
            string.Join("\n", conversion.Context.Issues));
        foreach (var input in new[]
                 {
                     conversion.CalorSource,
                     new CalorEmitter().Emit(Parse(conversion.CalorSource)),
                 })
        {
            var module = Parse(input);
            Assert.Empty(Descendants(module).OfType<CSharpInteropBlockNode>());
            var parameter = Assert.Single(Descendants(module).OfType<ParameterNode>()
                .Where(parameter => parameter.Name == "name"));
            Assert.Equal(Calor.Compiler.Ast.ParameterModifier.Out, parameter.Modifier);
            Assert.Equal("string?", parameter.TypeName);
            Assert.Equal(true, Assert.Single(Assert.Single(parameter.CSharpAttributes).Arguments).Value);
            var result = Program.Compile(input);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(RunNotNullWhen(original), RunNotNullWhen(result.GeneratedCode));
        }
    }

    private static string RunNotNullWhen(string source)
    {
        using var stream = new MemoryStream();
        var emitted = Compile(source).Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("ParameterAttribute", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(stream).GetType("Names", throwOnError: true)!;
            var method = type.GetMethod("TryName", BindingFlags.Public | BindingFlags.Static)!;
            var parameter = method.GetParameters()[1];
            Assert.True(parameter.IsOut);
            Assert.Equal(typeof(string).MakeByRefType(), parameter.ParameterType);
            var attribute = Assert.Single(parameter.CustomAttributes.Where(attribute =>
                attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute"));
            Assert.Equal(true, Assert.Single(attribute.ConstructorArguments).Value);
            Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(parameter).WriteState);
            object?[] found = [1, null];
            object?[] missing = [2, "stale"];
            Assert.Equal(true, method.Invoke(null, found));
            Assert.Equal("one", found[1]);
            Assert.Equal(false, method.Invoke(null, missing));
            Assert.Null(missing[1]);
            return $"{found[0]}:{found[1]}:{missing[0]}:{missing[1] ?? "<null>"}";
        }
        finally { context.Unload(); }
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static CSharpCompilation Compile(string source)
    {
        var compilation = CSharpCompilation.Create("ParameterAttributes",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return compilation;
    }

    private static IEnumerable<AstNode> Descendants(AstNode node)
    {
        yield return node;
        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns)
        => ns.GetTypeMembers().Concat(ns.GetNamespaceMembers().SelectMany(Types));

    private static string[] DescriptionParameters(CSharpCompilation compilation)
        => Types(compilation.Assembly.GlobalNamespace)
            .SelectMany(type => type.GetMembers().OfType<IMethodSymbol>())
            .SelectMany(method => method.Parameters)
            .Where(parameter => parameter.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.Name == "DescriptionAttribute"
                && attribute.ConstructorArguments.Single().Value is "native"))
            .Select(parameter => $"{parameter.ContainingSymbol.ContainingType.ToDisplayString()}:{parameter.ContainingSymbol.MetadataName}:{parameter.Name}:{parameter.Type.ToDisplayString()}")
            .Order(StringComparer.Ordinal)
            .ToArray();
}
