using Assembly = System.Reflection.Assembly;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using OutputKind = Microsoft.CodeAnalysis.OutputKind;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class ArrayAllocationAnnotationTests
{
    [Theory]
    [InlineData("List<int>[]", "values[0]", "int")]
    [InlineData("List<int>[,]", "values[0, 0]", "int")]
    [InlineData("List<int>[][]", "values[0][0]", "int")]
    [InlineData("List<string>[]", "values[0]", "string")]
    public void MetadataGenericArrayElements_RetainTheSelectedConstructedIdentity(string type, string access, string argument)
    {
        var source = $$"""
            module MetadataArrayElement
              func Read ({{type}}:values, {{argument}}:value) → bool
                !
                return ({{access}}).Contains(value)
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(new Parser(
            new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse());
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var element = Assert.IsType<GenericInstantiationBoundType>(call.Receiver!.Type);
        Assert.NotNull(element.RoslynSymbol);
        Assert.True(Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
            element.RoslynSymbol, call.ResolvedMetadataMethod?.ContainingType));
        Assert.Equal("System.Collections.Generic.List<T>", element.RoslynSymbol!.OriginalDefinition.ToDisplayString());
        Assert.Equal(NullableAnnotation.NotAnnotated, element.NullableAnnotation);
        Assert.Equal(argument == "int" ? Microsoft.CodeAnalysis.SpecialType.System_Int32
                : Microsoft.CodeAnalysis.SpecialType.System_String,
            Assert.Single(element.RoslynSymbol.TypeArguments).SpecialType);
        var receiver = call.Receiver switch
        {
            BoundArrayAccess indexed => indexed.Array,
            BoundMultiDimArrayAccess indexed => indexed.Array,
            _ => throw new InvalidOperationException("Expected an actual array access")
        };
        Assert.Same(NullabilityChecker.GetMethodInputArrayType(receiver)!.ElementType, element);
        var compiled = Program.Compile(source, "metadata-array-element.calr");
        Assert.Empty(compiled.Diagnostics);
    }

    [Fact]
    public void MetadataGenericArrayElements_UseTheSelectedCalleesDeclarationContext()
    {
        const string source = """
            module MetadataArrayDeclarationContext
              func Read<List> (System.Collections.Generic.List<int>[]:values) → bool
                !
                return Get(values)[0].Contains(0)
              func Get private (System.Collections.Generic.List<int>[]:values) → List<int>[]
                return values
            """;
        var result = Program.Compile(source, "metadata-array-declaration-context.calr");
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void MetadataGenericArrayElements_StillChargeTheSelectedMutation()
    {
        const string source = """
            module MetadataArrayMutation
              func Read (List<int>[]:values) → void
                !
                values[0].Add(1)
            """;
        var rejected = Program.Compile(source, "metadata-array-mutation.calr");
        Assert.Contains(rejected.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'mut'", StringComparison.Ordinal));
        Assert.DoesNotContain(rejected.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.Empty(Program.Compile(source.Replace("    !\n", "    ! mut\n", StringComparison.Ordinal),
            "metadata-array-mutation.calr").Diagnostics);
    }

    [Theory]
    [InlineData("List<Missing>[]", "", "")]
    [InlineData("List<T>[]", "<T>", "")]
    [InlineData("List<int>[]", "<List>", "")]
    [InlineData("List<int>[]", "", "  class List<T>\n")]
    [InlineData("List<@Int32>[]", "", "  class @Int32\n")]
    public void MetadataGenericArrayElements_DoNotBorrowUnestablishedOrSourceIdentities(
        string type, string parameters, string declaration)
    {
        var source = $$"""
            module UnestablishedMetadataArray
            {{declaration}}  func Read{{parameters}} ({{type}}:values) → bool
                !
                return (values[0]).Contains(0)
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(new Parser(
            new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse());
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(Program.Compile(source, "unestablished-metadata-array.calr").HasErrors);
    }

    [Theory]
    [InlineData("int[]", "values[0]")]
    [InlineData("int[,]", "values[0, 0]")]
    [InlineData("int[][]", "values[0][0]")]
    public void PrimitiveArrayElements_KeepCanonicalIdentityAndDeclarationDisplay(string type, string access)
    {
        var source = $$"""
            module PrimitiveArrayDisplay
              func Read ({{type}}:values) → int
                !
                return {{access}}
            """;
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(new Parser(
            new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse());
        var result = Assert.IsType<BoundReturnStatement>(Assert.Single(bound.Functions).Body[0]).Expression!;
        var element = Assert.IsType<PrimitiveBoundType>(result.Type);
        Assert.Equal("INT", element.Name);
        Assert.Equal(new PrimitiveBoundType("INT"), element);
        Assert.Equal("int", element.DisplayString);
        Assert.False(result.HasUnknownInferredType);
    }

    [Theory]
    [InlineData("Cell[]", "copy[0]")]
    [InlineData("Cell[,]", "copy[0, 0]")]
    [InlineData("Cell[][]", "copy[0][0]")]
    [InlineData("Cell[][,]", "copy[0][0, 0]")]
    [InlineData("Cell[,][]", "copy[0, 0][0]")]
    public void SourceArrayAccess_RetainsTheDeclaredElementThroughAliases(string type, string access)
    {
        var source = $$"""
            module SourceArrayIdentity
              class Cell public
                field int:V public
              func Read public ({{type}}:values) → int
                !
                let copy = values
                let cell = {{access}}
                return (cell).V
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Binder(diagnostics).Bind(new Parser(
            new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse());
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var function = Assert.Single(module.Functions);
        var variable = Assert.IsType<BoundBindStatement>(function.Body[1]);
        var element = Assert.IsType<NominalBoundType>(variable.Initializer!.Type);
        var declaration = Assert.Single(module.SymbolsById.Values.OfType<TypeSymbol>(), symbol => symbol.Name == "Cell");
        Assert.Same(declaration, element.Declaration);
        Assert.Equal(NullableAnnotation.NotAnnotated, element.NullableAnnotation);
        var field = Assert.IsType<BoundFieldAccessExpression>(
            Assert.IsType<BoundReturnStatement>(function.Body[2]).Expression);
        Assert.Same(declaration, Assert.IsType<NominalBoundType>(field.Target.Type).Declaration);
        Assert.Equal("Cell", field.ResolvedField?.DeclaringTypeName);
        Assert.Equal("V", field.ResolvedField?.Name);
        foreach (var expression in BoundNodeHelpers.DescendantsAndSelf(module).OfType<BoundExpression>())
        {
            var receiver = expression switch
            {
                BoundArrayAccess indexed => indexed.Array,
                BoundMultiDimArrayAccess indexed => indexed.Array,
                _ => null
            };
            if (receiver is null)
                continue;
            var array = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(receiver));
            Assert.Same(array.ElementType, expression.Type);
            Assert.False(expression.HasUnknownInferredType);
            Assert.Same(expression.Type as ArrayBoundType, expression.MethodInputArrayType);
        }
        var compiled = Program.Compile(source, "source-array-identity.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
    }

    [Theory]
    [InlineData("Cell?[]", "values[0]")]
    [InlineData("Cell?[,]", "values[0, 0]")]
    [InlineData("Cell?[][]", "values[0][0]")]
    public void SourceArrayAccess_DoesNotPromoteNullableElements(string type, string access)
    {
        var source = $$"""
            module NullableSourceArray
              class Cell public
              func Read public ({{type}}:values) → Cell
                !
                let cell = {{access}}
                return cell
            """;
        var result = Program.Compile(source, "nullable-source-array.calr");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Theory]
    [InlineData("field int:V private")]
    [InlineData("field int:V public static")]
    [InlineData("field int:V public\n    field int:V public")]
    public void SourceArrayFields_DoNotSelectInaccessibleStaticOrAmbiguousMembers(string declaration)
    {
        var source = $$"""
            module SourceArrayFieldBoundary
              class Cell public
                {{declaration}}
              func Read public (Cell[]:values) → int
                !
                return (values[0]).V
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Binder(diagnostics).Bind(new Parser(
            new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse());
        var read = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(module).OfType<BoundFieldAccessExpression>());
        Assert.Null(read.ResolvedField);
        Assert.True(read.HasUnknownInferredType);
        Assert.True(Program.Compile(source, "source-array-field-boundary.calr").HasErrors);
    }

    [Fact]
    public void ArrayDisplaySpellingAndComputedChildren_DoNotEstablishElementIdentity()
    {
        var named = new BoundStructuralExpression(default, "Unproven", "Cell[]");
        var child = new BoundArrayCreation(default, "array", "array", "Cell", null, []);
        var computed = new BoundStructuralExpression(default, "Computed", "Cell[]", [child]);
        foreach (var receiver in new BoundExpression[] { named, computed })
        {
            var scalar = new BoundArrayAccess(default, receiver, new BoundIntLiteral(default, 0));
            var rectangular = new BoundMultiDimArrayAccess(default, receiver, [new BoundIntLiteral(default, 0)]);
            Assert.True(scalar.HasUnknownInferredType);
            Assert.True(rectangular.HasUnknownInferredType);
            Assert.Null(Assert.IsType<NominalBoundType>(scalar.Type).Declaration);
            Assert.Null(Assert.IsType<NominalBoundType>(rectangular.Type).Declaration);
        }
    }

    [Fact]
    public void ProductionFizzBuzzRange_ConvertsCompilesAndRuns()
    {
        var path = Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests", "Calor.RoundTrip.Synthetic", "SyntheticLib", "FizzBuzz.cs");
        var conversion = new CSharpToCalorConverter().Convert(File.ReadAllText(path), "FizzBuzz.cs");
        Assert.NotNull(conversion.CalorSource);
        Assert.Contains("new string[", conversion.CalorSource);
        Assert.Contains("set results[", conversion.CalorSource);
        var result = Program.Compile(conversion.CalorSource, "FizzBuzz.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.True(conversion.Success);
        var type = Emit(result.GeneratedCode).GetTypes().Single(type => type.Name == "FizzBuzz");
        var values = Assert.IsType<string[]>(type.GetMethod("Range")!.Invoke(null, [1, 15]));
        Assert.Equal(15, values.Length);
        Assert.Equal("1", values[0]);
        Assert.Equal("Fizz", values[2]);
        Assert.Equal("Buzz", values[4]);
        Assert.Equal("FizzBuzz", values[14]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConvertedSizedArray_PreservesDeclaredElementAnnotation(bool nullableElements, bool rectangular)
    {
        var suffix = rectangular ? "[,]" : "[]";
        var dimensions = rectangular ? "count, 2" : "count";
        var source = $$"""
            #nullable enable
            public static class Allocator {
                public static string{{(nullableElements ? "?" : "")}}{{suffix}} Allocate(int count) {
                    return new string{{(nullableElements ? "?" : "")}}[{{dimensions}}];
                }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(source, "Allocator.cs");
        Assert.NotNull(conversion.CalorSource);
        var result = Program.Compile(conversion.CalorSource, "Allocator.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var type = Emit(result.GeneratedCode).GetTypes().Single(type => type.Name == "Allocator");
        var array = Assert.IsAssignableFrom<Array>(type.GetMethod("Allocate")!.Invoke(null, [3]));
        Assert.Equal(rectangular ? 2 : 1, array.Rank);
        Assert.Equal(3, array.GetLength(0));
        Assert.Null(rectangular ? array.GetValue(0, 0) : array.GetValue(0));
    }

    [Fact]
    public void ConvertedNominalArrayWithConstructors_CompilesAndRuns()
    {
        var conversion = new CSharpToCalorConverter().Convert("""
            #nullable enable
            public class Box {
                public int Value;
                public Box(int value) { Value = value; }
                public static Box[] Allocate(int count) {
                    var results = new Box[count];
                    for (int i = 0; i < count; i++) results[i] = new Box(i);
                    return results;
                }
            }
            """, "Box.cs");
        Assert.NotNull(conversion.CalorSource);
        var result = Program.Compile(conversion.CalorSource, "Box.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        var type = Emit(result.GeneratedCode).GetTypes().Single(type => type.Name == "Box");
        var values = Assert.IsAssignableFrom<Array>(type.GetMethod("Allocate")!.Invoke(null, [3]));
        Assert.Equal(2, type.GetField("Value")!.GetValue(values.GetValue(2)));
    }

    public static IEnumerable<object[]> AllocationCases()
    {
        foreach (var nullableElements in new[] { false, true })
        foreach (var inferred in new[] { false, true })
        foreach (var size in new[] { "0", "3", "count" })
        foreach (var boundary in new[] { "binding", "return", "input" })
            yield return [nullableElements, inferred, size, boundary];
    }

    [Theory]
    [MemberData(nameof(AllocationCases))]
    public void NativeAllocation_PreservesStaticShape(
        bool nullableElements, bool inferred, string size, string boundary)
    {
        var element = nullableElements ? "string?" : "string";
        var binding = inferred ? "values" : $"{element}[]:values";
        var sink = boundary switch
        {
            "binding" => "let string[]:required = values",
            "return" => "return values",
            "input" => "Take(values)",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        var source = $$"""
            module AllocatedArray
              func Take public (string[]:value) → void
                !
              func Probe public (int:count) → {{(boundary == "return" ? "string[]" : "void")}}
                ! alloc
                let {{binding}} = new {{element}}[{{size}}]
                {{sink}}
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Binder(diagnostics).Bind(new Parser(tokens, diagnostics).Parse());
        var function = module.Functions.Single(function => function.Symbol.Name == "Probe");
        var bound = Assert.IsType<BoundBindStatement>(function.Body[0]);
        var creation = Assert.IsType<BoundArrayCreation>(bound.Initializer);
        var created = Assert.IsType<ArrayBoundType>(NullabilityChecker.GetMethodInputArrayType(creation));
        Assert.Equal(NullableAnnotation.NotAnnotated, NullabilityChecker.GetAnnotation(creation.Type));
        Assert.Equal(NullableAnnotation.NotAnnotated, created.NullableAnnotation);
        Assert.Equal(nullableElements ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(created.ElementType).NullableAnnotation);
        Assert.Equal(created, bound.Variable.MethodInputArrayType);
        var expected = boundary switch
        {
            "binding" => DiagnosticCode.NullableToNonNullableBinding,
            "return" => DiagnosticCode.NullableReturnFromNonNullable,
            _ => DiagnosticCode.NullableArgumentToNonNullableParameter
        };
        Assert.DoesNotContain(diagnostics.Errors, diagnostic => diagnostic.Code != expected);
        Assert.Equal(nullableElements, diagnostics.Any(diagnostic =>
            diagnostic.Code == expected && BindingDiagnosticPolicy.IsCompilationError(diagnostic)));
        foreach (var enableTypeChecking in new[] { true, false })
        {
            var result = Program.Compile(source, "allocated-array.calr",
                new CompilationOptions { EnableTypeChecking = enableTypeChecking });
            Assert.True(nullableElements == result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.Equal(nullableElements, string.IsNullOrEmpty(result.GeneratedCode));
            Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic => diagnostic.Code != expected);
            Assert.Equal(nullableElements, result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == expected && BindingDiagnosticPolicy.IsCompilationError(diagnostic)));
        }
    }

    [Theory]
    [InlineData("string[]?", "binding")]
    [InlineData("string[]?", "return")]
    [InlineData("string[]?", "input")]
    [InlineData("string?[]", "binding")]
    [InlineData("string?[]", "return")]
    [InlineData("string?[]", "input")]
    public void NullableParameterArray_RemainsRejected(string type, string boundary)
        => AssertNullableArrayRejected(type, boundary, initializedLocal: false);

    [Theory]
    [InlineData("string?[]", "binding")]
    [InlineData("string?[]", "return")]
    [InlineData("string?[]", "input")]
    public void ExplicitNullableLocalAnnotation_RemainsRejected(string type, string boundary)
        => AssertNullableArrayRejected(type, boundary, initializedLocal: true);

    // 4d7a36e3d (0.30): a `let` initialized with a non-null array narrows only its current
    // container; the declared annotation and the element annotations are kept, and writes,
    // loop back edges and mutable captures invalidate the fact. So `string[]?` initialized
    // with `new string[3]` now reaches a non-null container boundary, as in C# flow analysis.
    [Theory]
    [InlineData("binding")]
    [InlineData("return")]
    [InlineData("input")]
    public void InitializedNullableArrayContainer_IsNarrowedByItsNonNullInitializer(string boundary)
    {
        var sink = boundary switch
        {
            "binding" => "let string[]:required = values",
            "return" => "return values",
            _ => "Take(values)"
        };
        var result = Program.Compile($$"""
            module NullableArray
              func Take public (string[]:value) → void
                !
              func Probe public () → {{(boundary == "return" ? "string[]" : "void")}}
                ! alloc
                let string[]?:values = new string[3]
                {{sink}}
            """, "nullable-array.calr");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is
            DiagnosticCode.NullableToNonNullableBinding
            or DiagnosticCode.NullableReturnFromNonNullable
            or DiagnosticCode.NullableArgumentToNonNullableParameter);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
    }

    private static void AssertNullableArrayRejected(string type, string boundary, bool initializedLocal)
    {
        var sink = boundary switch
        {
            "binding" => "let string[]:required = values",
            "return" => "return values",
            _ => "Take(values)"
        };
        var source = $$"""
            module NullableArray
              func Take public (string[]:value) → void
                !
              func Probe public ({{(initializedLocal ? "" : $"{type}:values")}}) → {{(boundary == "return" ? "string[]" : "void")}}
                ! alloc
            {{(initializedLocal ? $"    let {type}:values = new string[3]" : "")}}
                {{sink}}
            """;
        var result = Program.Compile(source, "nullable-array.calr");
        var expected = boundary switch
        {
            "binding" => DiagnosticCode.NullableToNonNullableBinding,
            "return" => DiagnosticCode.NullableReturnFromNonNullable,
            _ => DiagnosticCode.NullableArgumentToNonNullableParameter
        };
        Assert.True(result.HasErrors);
        Assert.True(string.IsNullOrEmpty(result.GeneratedCode));
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == expected
            && BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    private static Assembly Emit(string source)
    {
        var compilation = CSharpCompilation.Create("Allocated_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
