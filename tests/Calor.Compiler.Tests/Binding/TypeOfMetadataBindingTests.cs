using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

public sealed class TypeOfMetadataBindingTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("List<>")]
    [InlineData("Model")]
    public void TypeOfHasActualNonNullableMetadataTypeAndAuthoredSpan(string operand)
    {
        var source = $$"""
            module TypeOfBinding
              using System
              using System.Collections.Generic
              class Model
              class Type
              func Value () → System.Type
                !
                return typeof({{operand}})
            """;
        var (module, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var expression = Assert.Single(Nodes(bound).OfType<BoundStructuralExpression>()
            .Where(node => node.NodeTypeName == nameof(TypeOfExpressionNode)));
        var type = Assert.IsType<NominalBoundType>(expression.Type);
        Assert.Equal("System.Type", type.FullyQualifiedName);
        Assert.Equal(Calor.Compiler.Binding.BoundTypes.NullableAnnotation.NotAnnotated, type.NullableAnnotation);
        Assert.Equal("System.Type", type.RoslynSymbol!.ToDisplayString());
        Assert.Null(type.Declaration);
        Assert.Equal(operand, expression.Metadata["OperandType"]);
        Assert.Equal($"typeof({operand})", source[expression.Span.Start..expression.Span.End]);
        var assembly = Compile(module);
        var expected = operand switch
        {
            "int" => typeof(int),
            "string" => typeof(string),
            "List<>" => typeof(List<>),
            _ => assembly.GetType("Model")!
        };
        Assert.Same(expected, assembly.GetType("GlobalModule")!.GetMethod("Value")!.Invoke(null, null));
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeCollectionsSelectRealAddAndRetainRuntimeValues(bool nested)
    {
        var expression = nested
            ? "new List<List<Type>>() { new List<Type>() { typeof(int), typeof(string) }, new List<Type>() }"
            : "new List<System.Type>() { typeof(int), typeof(string) }";
        var source = $$"""
            module TypeOfBinding
              using System
              using System.Collections.Generic
              func Values () → {{(nested ? "List<List<Type>>" : "List<System.Type>")}}
                ! alloc
                return {{expression}}
            """;
        var (module, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = Nodes(bound).OfType<BoundNewExpression>().SelectMany(node => node.CollectionInitializers).ToArray();
        Assert.Equal(nested ? 4 : 2, calls.Length);
        foreach (var call in calls)
        {
            var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
            Assert.Equal("System.Collections.Generic.List<T>", selected.ContainingType.OriginalDefinition.ToDisplayString());
            Assert.Equal("Add", selected.Name);
            Assert.False(selected.IsStatic);
            var argument = Assert.Single(call.Arguments);
            Assert.Equal(argument.Span, call.Span);
            Assert.True(SymbolEqualityComparer.Default.Equals(
                Assert.IsType<NominalBoundType>(call.Receiver!.Type).RoslynSymbol, selected.ContainingType));
            Assert.True(call.MutatesOnlyFreshCollectionReceiver);
        }
        var value = Compile(module).GetType("GlobalModule")!.GetMethod("Values")!.Invoke(null, null);
        var values = nested ? Assert.IsType<List<List<Type>>>(value)[0] : Assert.IsType<List<Type>>(value);
        Assert.Equal(new[] { typeof(int), typeof(string) }, values);
        if (nested)
            Assert.Empty(Assert.IsType<List<List<Type>>>(value)[1]);
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public void MetadataMethodsPropertiesAndNativeOverloadsUseSystemType()
    {
        const string source = """
            module TypeOfBinding
              using System
              func Pick (System.Type:value) → int
                !
                return 1
              func Pick (int:value) → int
                !
                return 2
              func Pick (object:value) → int
                !
                return 3
              func Selected () → int
                !
                return Pick(typeof(int))
              func Assignable () → bool
                !
                return typeof(object).IsAssignableFrom(typeof(string))
              func Name () → string
                !
                return typeof(int).Name
              func Code () → TypeCode
                !
                return Type.GetTypeCode(typeof(int))
            """;
        var (module, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = Nodes(bound).OfType<BoundCallExpression>().ToArray();
        var selected = Assert.Single(calls.Where(call => call.Target == "Pick"));
        Assert.Equal("System.Type", Assert.Single(selected.ResolvedSymbol!.Parameters).TypeName);
        var metadata = Assert.Single(calls.Where(call => call.ResolvedMetadataMethod?.Name == "IsAssignableFrom"));
        Assert.Equal("System.Type", metadata.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
        var parameter = Assert.Single(metadata.ResolvedMetadataMethod.Parameters).Type;
        Assert.Equal("System.Type", parameter.WithNullableAnnotation(Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated).ToDisplayString());
        Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.Annotated, parameter.NullableAnnotation);
        var property = Assert.Single(Nodes(bound).OfType<BoundFieldAccessExpression>()
            .Where(node => node.FieldName == "Name"));
        Assert.Equal("System.Type", property.Target.Type.FullyQualifiedName);
        var metadataProperty = Assert.IsAssignableFrom<IPropertySymbol>(property.ResolvedMetadataMember);
        Assert.Equal("System.Reflection.MemberInfo", metadataProperty.ContainingType.ToDisplayString());
        Assert.Equal("Name", metadataProperty.Name);
        Assert.Equal("Name", source[property.FieldNameSpan.Start..property.FieldNameSpan.End]);
        var code = Assert.Single(calls.Where(call => call.ResolvedMetadataMethod?.Name == "GetTypeCode"));
        Assert.Equal("System.Type", code.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
        Assert.Equal("GetTypeCode", source[code.CalleeSpan.Start..code.CalleeSpan.End]);
        var assembly = Compile(module);
        var owner = assembly.GetType("GlobalModule")!;
        Assert.Equal(1, owner.GetMethod("Selected")!.Invoke(null, null));
        Assert.Equal(true, owner.GetMethod("Assignable")!.Invoke(null, null));
        Assert.Equal("Int32", owner.GetMethod("Name")!.Invoke(null, null));
        Assert.Equal(TypeCode.Int32, owner.GetMethod("Code")!.Invoke(null, null));
        var effects = new DiagnosticBag();
        new EffectEnforcementPass(effects).Enforce(module);
        Assert.Contains(effects, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall
            && diagnostic.Message.Contains("System.Type.IsAssignableFrom", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("new List<System.Type>() { 1 }")]
    [InlineData("new List<int>() { typeof(int) }")]
    [InlineData("new List<List<Type>>() { typeof(int) }")]
    public void WrongCollectionElementDoesNotAcquireAnAddOverload(string expression)
    {
        var source = $$"""
            module TypeOfBinding
              using System
              using System.Collections.Generic
              func Invalid () → object
                ! alloc
                return {{expression}}
            """;
        var (_, bound, diagnostics) = Bind(source);
        var error = Assert.Single(diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload));
        var call = Assert.Single(Nodes(bound).OfType<BoundNewExpression>().SelectMany(node => node.CollectionInitializers));
        Assert.Equal(call.Span, error.Span);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.False(call.MutatesOnlyFreshCollectionReceiver);
    }

    [Fact]
    public void WrongMetadataArgumentDoesNotSelectAnOverload()
    {
        const string source = """
            module TypeOfBinding
              func Invalid () → System.TypeCode
                !
                return System.Type.GetTypeCode(1)
            """;
        var (module, bound, _) = Bind(source);
        var call = Assert.Single(Nodes(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        var errors = Compilation(module).GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains(errors, diagnostic => diagnostic.Id == "CS1503"
            && diagnostic.GetMessage().Contains("System.Type", StringComparison.Ordinal));
    }

    [Fact]
    public void UnrelatedMissingPeerImportDoesNotEraseResolvedBclCollectionTypes()
    {
        const string source = """
            module TypeOfBinding
              using System
              using System.Collections.Generic
              using Missing.Peer
              func Values () → List<List<Type>>
                ! alloc
                let lists = new List<List<Type>>() { new List<Type>() { typeof(int) } }
                let currentList = lists[0]
                each item in currentList
                  let currentCombination = new List<Type>() { item }
                return lists
            """;
        var (module, bound, diagnostics) = Bind(source);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var calls = Nodes(bound).OfType<BoundNewExpression>().SelectMany(node => node.CollectionInitializers).ToArray();
        Assert.Equal(3, calls.Length);
        Assert.All(calls, call => Assert.NotNull(call.ResolvedMetadataMethod));
        Assert.Contains(Compilation(module).GetDiagnostics(), diagnostic => diagnostic.Id == "CS0246"
            && diagnostic.GetMessage().Contains("Missing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("using Type = System.String")]
    [InlineData("using Type = Missing.Type")]
    [InlineData("using Type = System.String\n  using Type = System.Type")]
    public void ConflictingOrUnresolvedElementIdentityDoesNotBecomeSystemType(string imports)
    {
        var source = $$"""
            module TypeOfBinding
              using System
              using System.Collections.Generic
              using Missing.Peer
              {{imports}}
              func Invalid () → object
                ! alloc
                return new List<Type>() { typeof(int) }
            """;
        var (_, bound, diagnostics) = Bind(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        var call = Assert.Single(Nodes(bound).OfType<BoundNewExpression>().SelectMany(node => node.CollectionInitializers));
        Assert.Null(call.ResolvedMetadataMethod);
    }

    private static (ModuleNode Module, BoundModule Bound, DiagnosticBag Diagnostics) Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return (module, new Binder(diagnostics).Bind(module), diagnostics);
    }

    private static IEnumerable<BoundNode> Nodes(BoundNode node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
            foreach (var descendant in Nodes(child))
                yield return descendant;
    }

    private static CSharpCompilation Compilation(ModuleNode module)
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies unavailable.")).Split(Path.PathSeparator);
        return CSharpCompilation.Create("TypeOf_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(module))],
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Assembly Compile(ModuleNode module)
    {
        var compilation = Compilation(module);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
