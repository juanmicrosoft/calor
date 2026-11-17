using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Binding.Metadata;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Calor.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;
using NullableAnnotation = Calor.Compiler.Binding.BoundTypes.NullableAnnotation;

namespace Calor.Compiler.Tests;

public class NullableReferenceTypingTests
{
    [Theory]
    [InlineData("string?")]
    [InlineData("System.String?")]
    public void NullableStringLiteralAndInlineParameter_AgreeWithGeneratedRuntime(string spelling)
    {
        var source = $$"""
            module NullableTyping NullableTyping
              func Probe public ({{spelling}}:input) → {{spelling}}
                !
                let {{spelling}}:literal = "value"
                let {{spelling}}:copy = input
                return literal
            """;
        foreach (var text in RoundTrip(source))
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(text, checking);
            AssertNoTypingNoise(result);
            Assert.DoesNotContain("Calor.Runtime.Option", result.GeneratedCode);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(typeof(string), method.ReturnType);
            Assert.Equal(typeof(string), Assert.Single(method.GetParameters()).ParameterType);
            Assert.Equal("value", method.Invoke(null, [null]));
        }
    }

    [Theory]
    [InlineData("?str")]
    [InlineData("?string")]
    [InlineData("str?")]
    [InlineData("?System.String")]
    public void RetiredNullableStringSpellings_AreRejectedInsideNativeSignatures(string spelling)
    {
        var source = $$"""
            module NullableTyping NullableTyping
              func Probe public ({{spelling}}:input) → {{spelling}}
                !
                return input
            """;
        foreach (var checking in new[] { true, false })
        foreach (var transpile in new[] { true, false })
        {
            var result = Program.Compile(source, "nullable-typing.calr", new CompilationOptions
            {
                EnableTypeChecking = checking,
                UnsafeTranspileOnly = transpile,
                StatusWriter = TextWriter.Null
            });
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics.Errors, error =>
                error.Code == DiagnosticCode.UnexpectedToken && error.Span.Line == 2);
            Assert.DoesNotContain(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.UnknownSectionMarker);
        }
    }

    [Theory]
    [InlineData("string?")]
    [InlineData("System.String?")]
    public void NullableBclReturn_IsAReferenceAndRetainsNullAtRuntime(string spelling)
    {
        // This annotation assertion uses the repository-scoped metadata profile, not its Oblivious fallback.
        using var metadataContext = MetadataContext.Create();
        var key = "CALOR_T1_ABSENT_" + Guid.NewGuid().ToString("N");
        Assert.Null(Environment.GetEnvironmentVariable(key));
        var source = $$"""
            module NullableTyping NullableTyping
              func Probe public () → {{spelling}}
                ! env
                let {{spelling}}:value = System.Environment.GetEnvironmentVariable("{{key}}")
                return value
            """;
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(source, checking);
            AssertNoTypingNoise(result);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(typeof(string), method.ReturnType);
            Assert.Null(method.Invoke(null, null));
        }
        var (bound, diagnostics) = Bind(source);
        Assert.Empty(diagnostics.Errors);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundBindStatement>(Assert.Single(bound.Functions).Body[0]).Initializer);
        Assert.Equal(NullableAnnotation.Annotated, Assert.IsType<NominalBoundType>(call.Type).NullableAnnotation);
    }

    [Fact]
    public void DeclaredNominalReference_ParametersCopiesAndConstructorsRemainReferences()
    {
        const string source = """
            module NullableTyping NullableTyping
              class Foo public
                field int:value public
              func Probe public (Foo?:input) → Foo?
                ! alloc
                let Foo?:copy = input
                let Foo?:created = new Foo()
                return created
            """;
        foreach (var text in RoundTrip(source))
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(text, checking);
            AssertNoTypingNoise(result);
            Assert.DoesNotContain("Calor.Runtime.Option", result.GeneratedCode);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal("Foo", method.ReturnType.Name);
            Assert.Equal("NullableTyping.Foo", method.ReturnType.FullName);
            Assert.Equal(method.ReturnType, Assert.Single(method.GetParameters()).ParameterType);
            Assert.NotNull(method.Invoke(null, [null]));
        }
    }

    [Fact]
    public void NullableConstructionAndConsumption_KeepTheirDeclaredType()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Probe public (bool:present) → string?
                ! alloc
                if present
                  return "value"
                else
                  return null
            """;
        foreach (var text in RoundTrip(source))
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(text, checking);
            AssertNoTypingNoise(result);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(typeof(string), method.ReturnType);
            Assert.Equal("value", method.Invoke(null, [true]));
            Assert.Null(method.Invoke(null, [false]));
        }
    }

    [Theory]
    [InlineData("string?", "§SM \"value\"")]
    [InlineData("string?", "§NN{str}")]
    [InlineData("string", "§SM \"value\"")]
    public void RemovedOptionConstructors_AreRejectedInNullableBindings(string target, string expression)
    {
        var source = $$"""
            module NullableTyping NullableTyping
              func Probe public (string?:input) → void
                ! alloc
                let {{target}}:value = {{expression}}
            """;
        foreach (var checking in new[] { true, false })
        foreach (var transpile in new[] { true, false })
        {
            var result = Program.Compile(source, "nullable-typing.calr", new CompilationOptions
            {
                EnableTypeChecking = checking,
                UnsafeTranspileOnly = transpile,
                StatusWriter = TextWriter.Null
            });
            Assert.True(result.HasErrors, result.GeneratedCode);
            Assert.Contains(result.Diagnostics.Errors,
                d => d.Code == DiagnosticCode.UnknownSectionMarker && d.Span.Line == 4);
        }
    }

    [Fact]
    public void ExpandedNullableAssignment_UsesActivatedBinderOwnership()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Probe public () → void
                !
                let string?:x = null
                let string:y = x
            """;
        foreach (var checking in new[] { true, false })
        {
            var rejected = Program.Compile(source, "nullable-typing.calr", new CompilationOptions
            {
                EnableTypeChecking = checking,
                StatusWriter = TextWriter.Null
            });
            var error = Assert.Single(rejected.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.NullableToNonNullableBinding, error.Code);
            Assert.Equal(5, error.Span.Line);
            Assert.True(BindingDiagnosticPolicy.IsCompilationError(error));
            Assert.DoesNotContain(rejected.Diagnostics, d => d.Code == DiagnosticCode.TypeMismatch);
            Assert.DoesNotContain(rejected.Diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
        }
        var (_, diagnostics) = Bind(source);
        var bindingError = Assert.Single(diagnostics.Errors,
            d => d.Code == DiagnosticCode.NullableToNonNullableBinding);
        Assert.Equal(5, bindingError.Span.Line);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(bindingError));
    }

    [Fact]
    public void RawInlineNullableFlow_IsActivatedForScalarString()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Probe public (string?:input) → void
                !
                let string:y = input
            """;
        foreach (var checking in new[] { true, false })
        {
            var rejected = Program.Compile(source, "nullable-typing.calr", new CompilationOptions
            {
                EnableTypeChecking = checking,
                StatusWriter = TextWriter.Null
            });
            var error = Assert.Single(rejected.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.NullableToNonNullableBinding, error.Code);
            Assert.Equal(4, error.Span.Line);
            Assert.True(BindingDiagnosticPolicy.IsCompilationError(error));
            Assert.DoesNotContain(rejected.Diagnostics, d => d.Code == DiagnosticCode.TypeMismatch);
        }
        var (_, diagnostics) = Bind(source);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(Assert.Single(diagnostics.Errors)));
    }

    [Fact]
    public void NullableNativeArguments_KeepRejectionWithNullabilityOwnership()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Take public (string:input) → void
                ! env
              func Probe public () → void
                ! env
                Take(System.Environment.GetEnvironmentVariable("PATH"))
            """;
        var (bound, diagnostics) = Bind(source);
        var call = Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundExpressionStatement>(bound.Functions[1].Body[0]).Expression);
        Assert.True(diagnostics.Any(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
                && BindingDiagnosticPolicy.IsCompilationError(d)),
            $"Argument: {Assert.Single(call.Arguments).Type.DisplayString}; target: {call.ResolvedSymbol?.Parameters[0].TypeName}; {string.Join("; ", diagnostics)}");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload);
        Assert.NotNull(call.ResolvedSymbol);
    }

    [Fact]
    public void NullableNativeTarget_PreservesNullAndNonNullRuntimeValues()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Take public (string?:value) → string?
                !
                return value
              func Probe public (string?:value) → string?
                !
                return Take(value)
              func NonNull public (string:value) → string?
                !
                return Take(value)
            """;
        var result = Program.Compile(source, "native-string-runtime.calr");
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics));
        Assert.DoesNotContain("??", result.GeneratedCode);
        Assert.DoesNotContain("Calor.Runtime.Option", result.GeneratedCode);
        var type = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!;
        var probe = type.GetMethod("Probe")!;
        Assert.Equal(typeof(string), probe.ReturnType);
        Assert.Null(probe.Invoke(null, [null]));
        Assert.Equal("live", probe.Invoke(null, ["live"]));
        Assert.Equal("live", type.GetMethod("NonNull")!.Invoke(null, ["live"]));
    }

    [Fact]
    public void ObjectAlternative_DoesNotBypassScalarStringActivation()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Take public (string:value) → int
                !
                return 1
              func Take public (object:value) → int
                !
                return 2
              func Probe public (string?:value) → int
                !
                return Take(value)
            """;
        var (_, diagnostics) = Bind(source);
        var bindingError = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, bindingError.Code);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(bindingError));
        var result = Program.Compile(source, "native-string-overload-runtime.calr");
        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, error.Code);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(error));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData("push")]
    [InlineData("set-index")]
    [InlineData("insert")]
    public void ExpandedCompatibilityCollectionNodes_RejectNullableStringPayloads(string operation)
    {
        foreach (var nullable in new[] { false, true })
        {
            var diagnostics = new DiagnosticBag();
            var module = new Parser(new Lexer($$"""
                module NullableTyping NullableTyping
                  func Probe public ({{(nullable ? "string?" : "string")}}:value) → void
                    ! alloc, mut
                    let List<string>:items = new List<string>{"seed"}
                """, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
            var function = Assert.Single(module.Functions);
            var span = TextSpan.Empty;
            var value = new ReferenceNode(span, "value");
            StatementNode statement = operation switch
            {
                "push" => new CollectionPushNode(span, "items", value),
                "set-index" => new CollectionSetIndexNode(span, "items", new IntLiteralNode(span, 0), value),
                "insert" => new CollectionInsertNode(span, "items", new IntLiteralNode(span, 0), value),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            // These retained nodes use the expanded compatibility contract, not native List<T> call enforcement.
            var parameter = Assert.Single(function.Parameters);
            var compatibilityParameter = new ParameterNode(parameter.Span, parameter.Name,
                AttributeHelper.ExpandType(parameter.TypeName), parameter.Attributes);
            var compatibilityFunction = new FunctionNode(function.Span, function.Id, function.Name,
                function.Visibility, [compatibilityParameter], function.Output, function.Effects,
                [.. function.Body, statement], function.Attributes);
            var compatibilityModule = new ModuleNode(module.Span, module.Id, module.Name,
                module.Usings, [compatibilityFunction], module.Attributes);

            new TypeChecker(diagnostics).Check(compatibilityModule);

            if (nullable)
                Assert.Equal(DiagnosticCode.TypeMismatch, Assert.Single(diagnostics.Errors).Code);
            else
                Assert.Empty(diagnostics.Errors);
        }
    }

    [Theory]
    [InlineData("§PUSH{items} value")]
    [InlineData("§SETIDX{items} 0 value")]
    [InlineData("§INS{items} 0 value")]
    public void RetiredCollectionOperations_AreNotAdmittedAsNativeStatements(string operation)
    {
        var result = Program.Compile($$"""
            module NullableTyping NullableTyping
              func Probe public (string?:value) → void
                ! alloc mut
                let List<string>:items = new List<string>{"seed"}
                {{operation}}
            """, "nullable-typing.calr");

        Assert.Contains(result.Diagnostics.Errors, error =>
            error.Code == DiagnosticCode.UnknownSectionMarker && error.Span.Line == 5);
    }

    [Fact]
    public void NullableStringReturn_PreservesAnnotatedPayload()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Probe public (string?:value) → string
                ! throw
                return value
            """;

        var (bound, diagnostics) = Bind(source);
        var returned = Assert.IsType<BoundReturnStatement>(
            Assert.Single(bound.Functions).Body.Single());
        var call = Assert.IsType<BoundVariableExpression>(returned.Expression);
        Assert.Equal(NullableAnnotation.Annotated,
            Assert.IsType<NominalBoundType>(call.Type).NullableAnnotation);
        Assert.Equal(DiagnosticCode.NullableReturnFromNonNullable,
            Assert.Single(diagnostics.Errors).Code);

        foreach (var checking in new[] { true, false })
        {
            var result = Program.Compile(source, "nullable-option-unwrap.calr",
                new CompilationOptions
                {
                    EnableTypeChecking = checking,
                    StatusWriter = TextWriter.Null
                });
            var error = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.NullableReturnFromNonNullable, error.Code);
            Assert.DoesNotContain(result.Diagnostics,
                d => d.Code == DiagnosticCode.TypeMismatch);
        }
    }

    [Theory]
    [InlineData("string?", "42")]
    [InlineData("int", "\"value\"")]
    public void GenuineMismatches_DoNotBecomeUniversallyAssignable(
        string target, string expression)
    {
        var result = Program.Compile($$"""
            module NullableTyping NullableTyping
              func Probe public () → void
                !
                let {{target}}:value = {{expression}}
            """, "nullable-typing.calr");
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch && d.Span.Line == 4);
    }

    [Fact]
    public void NullableValueTypes_AcceptImplicitUnderlyingValueConversion()
    {
        var result = Program.Compile("""
            module NullableTyping NullableTyping
              func Probe public () → void
                !
                let int?:value = 42
            """, "nullable-typing.calr");

        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics));
    }

    [Theory]
    [InlineData("int[,]", "int[,]")]
    [InlineData("int[,,]", "int[,,]")]
    [InlineData("int[,,,]", "int[,,,]")]
    [InlineData("int[][,]", "int[][,]")]
    [InlineData("int[,][,,]", "int[,][,,]")]
    [InlineData("int[,][]?", "int[,][]?")]
    [InlineData("string[,]", "string[,]")]
    [InlineData("List<int>[,]", "List<int>[,]")]
    public void ArrayRankSuffixes_AcceptNullWithDefaultChecking(string type, string emittedType)
    {
        var result = Program.Compile($$"""
            module NullableTyping NullableTyping
              using System.Collections.Generic
              func Probe public () → void
                !
                let {{type}}:grid = null
            """, "array-rank-typing.calr");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains($"{emittedType} grid = null;", result.GeneratedCode);
        Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
    }

    [Theory]
    [InlineData("int[]")]
    [InlineData("int[,][]")]
    [InlineData("Option<int>")]
    [InlineData("Result<int, string>")]
    [InlineData("Option<int[,]>")]
    [InlineData("Result<int[,], string>")]
    public void ArraysAndOrdinaryGenerics_KeepExistingNullRejection(string type)
    {
        var result = Program.Compile($$"""
            module NullableTyping NullableTyping
              func Probe public () → void
                !
                let {{type}}:value = null
            """, "array-generic-typing.calr");

        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.TypeMismatch &&
            diagnostic.Message.Contains("Cannot assign null", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("List<int[]>", "List<int[]>")]
    [InlineData("Dictionary<int, string[]>", "Dictionary<int, string[]>")]
    public void GenericsWithArrayArguments_KeepTheirGenericIdentity(string type, string emittedType)
    {
        var result = Program.Compile($$"""
            module NullableTyping NullableTyping
              using System.Collections.Generic
              func Probe public ({{type}}:input) → void
                !
                let {{type}}:grid = input
            """, "array-generic-typing.calr");

        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Contains($"{emittedType} grid = input;", result.GeneratedCode);
        Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
    }

    [Theory]
    [InlineData("int[]", "string[]")]
    [InlineData("Option<int>", "Option<string>")]
    [InlineData("List<int>", "List<string>")]
    public void ArrayAndGenericMismatches_KeepTheirExistingTypeChecks(string sourceType, string target)
    {
        var result = Program.Compile($$"""
            module NullableTyping NullableTyping
              func Probe public ({{sourceType}}:input) → void
                !
                let {{target}}:value = input
            """, "nullable-typing.calr");
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData("[i32]", "[i32]", "null")]
    [InlineData("Option[i32]", "Option[i32]", "null")]
    [InlineData("Result[i32, str]", "Result[i32, str]", "null")]
    [InlineData("[i32]", "[str]", "input")]
    public void RetiredArrayAndGenericSpellings_AreRejectedInsideNativeDeclarations(
        string sourceType, string target, string expression)
    {
        var result = Program.Compile($$"""
            module NullableTyping NullableTyping
              func Probe public ({{sourceType}}:input) → void
                !
                let {{target}}:value = {{expression}}
            """, "nullable-typing.calr");

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.UnexpectedToken);
        Assert.DoesNotContain(result.Diagnostics.Errors, error => error.Code == DiagnosticCode.UnknownSectionMarker);
    }

    [Fact]
    public void NullableAndNominalGenericTypeModels_HaveDistinctIdentitiesAndHashes()
    {
        var nullable = new NullableReferenceType(PrimitiveType.String);
        var option = new ExternalType("Option<str>");
        Assert.NotEqual<CalorType>(nullable, option);
        Assert.Equal("?str", nullable.SurfaceName);
        Assert.Equal("Option<str>", option.SurfaceName);
        Assert.Equal(2, new HashSet<CalorType> { nullable, option }.Count);
        Assert.NotEqual(TypeIdentity.Canonicalize("?Option<str>"), TypeIdentity.Canonicalize("Option<?str>"));
        Assert.NotEqual(TypeIdentity.Canonicalize("str?[]"), TypeIdentity.Canonicalize("?str[]"));
        Assert.Equal("System.String", TypeIdentity.MapShortTypeNameToFullName("?str"));
        Assert.Equal("System.Nullable`1", TypeIdentity.MapShortTypeNameToFullName("?i32"));
        Assert.Equal("Option<str>", TypeIdentity.MapShortTypeNameToFullName("?Option<str>"));
        Assert.Equal("Option<str>", TypeIdentity.MapShortTypeNameToFullName("Option<str>"));
        Assert.Throws<ArgumentException>(() => new NullableReferenceType(PrimitiveType.Int));
        Assert.Equal("?Option<str>", new NullableReferenceType(option).SurfaceName);
    }

    [Fact]
    public void NullableStringConcatenation_PreservesExistingNonNullRuntimeResult()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Probe public (string?:left, string?:right) → string
                !
                return left + right
            """;
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(source, checking);
            AssertNoTypingNoise(result);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal("", method.Invoke(null, [null, null]));
            Assert.Equal("ab", method.Invoke(null, ["a", "b"]));
        }
    }

    [Theory]
    [InlineData("string?[]", "string?[]")]
    public void NullablePayloads_DoNotCollapseTheirArrayContainer(string type, string emittedType)
    {
        var source = $$"""
            module NullableTyping NullableTyping
              func Probe public ({{type}}:input) → {{type}}
                !
                let {{type}}:copy = input
                return copy
            """;
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(source, checking);
            AssertNoTypingNoise(result);
            Assert.Contains(emittedType, result.GeneratedCode);
            Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(typeof(string[]), method.ReturnType);
            string?[] values = ["value", null];
            Assert.Same(values, method.Invoke(null, [values]));
        }
    }

    [Theory]
    [InlineData("string?", "§SM \"value\"")]
    [InlineData("string?", "§NN{str}")]
    public void RemovedOptionReturns_AreRejectedWithoutTypeChecking(string returnType, string expression)
    {
        var source = $$"""
            module NullableTyping NullableTyping
              func Probe public () → {{returnType}}
                ! alloc
                return {{expression}}
            """;
        foreach (var checking in new[] { true, false })
        foreach (var transpile in new[] { true, false })
        {
            var result = Program.Compile(source, "nullable-typing.calr", new CompilationOptions
            {
                EnableTypeChecking = checking,
                UnsafeTranspileOnly = transpile
            });
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics.Errors,
                diagnostic => diagnostic.Code == DiagnosticCode.UnknownSectionMarker && diagnostic.Span.Line == 4);
        }
    }

    [Fact]
    public void LambdaReturn_DoesNotUseEnclosingReferenceReturnContract()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Probe public () → string
                ! alloc
                let Func<string?>:factory = () =>
                  return null
                return "outer"
            """;
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(source, checking);
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ReferenceOptionMismatch);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal("outer", method.Invoke(null, null));
        }
    }

    [Theory]
    [InlineData("str[]?", "string[]?")]
    [InlineData("str?[]", "string?[]")]
    [InlineData("[?str]", "string?[]")]
    [InlineData("?i32", "int?")]
    [InlineData("?Option<str>", "Option<string>?")]
    public void Emission_KeepsReferenceValueAndContainerNullableDecorators(string source, string expected)
        => Assert.Equal(expected, TypeMapper.CalorToCSharp(source));

    [Fact]
    public void DeclarationClassification_DoesNotTreatStructsOrEnumsAsReferences()
    {
        const string source = """
            module NullableTyping NullableTyping
              class Foo public
                field int:value public
              class Value public struct
                field int:value public
              enum Kind public
                First
              func Probe public (Foo?:reference, Value?:value, Kind?:kind) → void
                !
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        var references = AttributeHelper.GetDeclaredReferenceTypeNames(module);
        Assert.Contains("Foo", references);
        Assert.DoesNotContain("Value", references);
        Assert.DoesNotContain("Kind", references);
        var result = Compile(source, checking: true);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UndefinedReference
            && d.Message.Contains("Foo?", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UndefinedReference
            && d.Message.Contains("Value?", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UndefinedReference
            && d.Message.Contains("Kind?", StringComparison.Ordinal));
    }

    [Fact]
    public void NullableObjectBoxing_PreservesTheValue()
    {
        const string source = """
            module NullableTyping NullableTyping
              func Probe public () → object?
                ! alloc
                let object?:boxed = "value"
                return boxed
            """;
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(source, checking);
            AssertNoTypingNoise(result);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(typeof(object), method.ReturnType);
            Assert.Equal("value", Assert.IsType<string>(method.Invoke(null, null)));
        }
    }

    [Fact]
    public void NullableNominalMemberAccess_IsNotMisdiagnosedAsARecordOperation()
    {
        const string source = """
            module NullableTyping NullableTyping
              class Foo public
                field int:value public
              func Probe public () → int
                ! alloc
                let Foo?:input = new Foo()
                return input.value
            """;
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(source, checking);
            AssertNoTypingNoise(result);
            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Code is DiagnosticCode.UnknownExternalCall or DiagnosticCode.AssumedEffects);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(0, method.Invoke(null, null));
        }

    }

    [Theory]
    [InlineData(false, "base.Value")]
    [InlineData(true, "input?.Child .Value")]
    [InlineData(true, "input?.Child.Value")]
    [InlineData(true, "(input)?.Child.Value")]
    [InlineData(true, "input?.@Child.Value")]
    [InlineData(true, "input?.Child.@Value")]
    [InlineData(true, "input?.Child?.Value")]
    public void PrimaryReceiverGetters_AreChargedInsteadOfBeingTreatedAsOptionMembers(
        bool conditional, string expression)
    {
        var source = $$"""
            module ReceiverTyping ReceiverTyping
              using System.Collections.Generic
              class Parent public
                property int:Value public
                  get
                    let List<int>:created = new List<int>()
                    return 3
              class Node public
                field int:Value public
                property Node:Child public
                  get
                    let List<int>:created = new List<int>()
                    let Node:child = new Node()
                    return child
              class Derived public extends Parent
                func Read public (Node:input) → int?
                  !
                  return {{expression}}
            """;
        var rejected = Program.Compile(source, "receiver-typing.calr",
            new CompilationOptions { EnableTypeChecking = false });
        Assert.True(rejected.Diagnostics.Errors.Any(d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("Read", StringComparison.Ordinal)
            && d.Message.Contains("alloc", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, rejected.Diagnostics) + Environment.NewLine + rejected.GeneratedCode);

        var accepted = Compile(source.Replace("      !\n", "      ! alloc\n", StringComparison.Ordinal), checking: false);
        var assembly = Emit(accepted.GeneratedCode);
        var derived = assembly.GetType("ReceiverTyping.Derived")!;
        var node = assembly.GetType("ReceiverTyping.Node")!;
        var method = derived.GetMethod("Read")!;
        Assert.Equal(conditional ? 0 : 3,
            method.Invoke(Activator.CreateInstance(derived), [Activator.CreateInstance(node)]));
        Assert.Equal(conditional ? null : (object)3,
            method.Invoke(Activator.CreateInstance(derived), [null]));
    }

    [Theory]
    [InlineData("input?.Child.Value")]
    [InlineData("input?.Child .Value")]
    [InlineData("(input)?.Child.Value")]
    [InlineData("input?.@Child.Value")]
    [InlineData("input?.Child.@Value")]
    public void NativeNullConditionalChain_RetainsIndividualMembersAndTheirSpans(string expression)
    {
        var source = $$"""
            module Chain
              func Read (Node:input) → int?
                !
                return {{expression}}
            """;
        foreach (var text in RoundTrip(source))
        {
            var diagnostics = new DiagnosticBag();
            var tokens = new Lexer(text, diagnostics).TokenizeAllForParser();
            var module = new Parser(tokens, diagnostics).Parse();
            Assert.False(diagnostics.HasErrors,
                text + "\n" + string.Join("\n", diagnostics) + "\n"
                + string.Join(" ", tokens.Select(token => $"{token.Kind}:{token.Text}")));
            var field = Assert.IsType<FieldAccessNode>(
                Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions).Body.Single()).Expression);
            Assert.Equal("Value", field.FieldName);
            Assert.Equal("Value", text.Substring(field.FieldNameSpan.Start, field.FieldNameSpan.Length).TrimStart('@'));
            var getter = Assert.IsType<NullConditionalNode>(field.Target);
            Assert.Equal("Child", getter.MemberName);
            Assert.EndsWith("Child", text.Substring(getter.Span.Start, getter.Span.Length));
            Assert.Equal("input", Assert.IsType<ReferenceNode>(getter.Target).Name);
        }
    }

    [Fact]
    public void UnknownReceiverSentinel_DoesNotResolveToPureRuntimeOptionMembers()
    {
        Assert.Equal("?", TypeIdentity.MapShortTypeNameToFullName("?"));
        const string source = """
            module ReceiverTyping ReceiverTyping
              class Node public
                field int:Value public
              func Probe public (Node:input) → int
                !
                return (input ?? input).Value
            """;
        var result = Program.Compile(source, "receiver-typing.calr",
            new CompilationOptions { EnableTypeChecking = false });
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("?.get_Value", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("string?", "\"value\"")]
    [InlineData("Foo?", "new Foo()")]
    [InlineData("Value?", "new Value()")]
    public void NullableNormalization_PreservesActiveNonFunctionRowErrors(string type, string expression)
    {
        var source = $$"""
            module NullableTyping NullableTyping
              class Foo public
                field int:value public
              class Value public struct
                field int:value public
              func Probe public () → void
                ! alloc
                let {{type}}:item ! = {{expression}}
            """;
        foreach (var checking in new[] { true, false })
        {
            var result = Program.Compile(source, "nullable-typing.calr", new CompilationOptions
            {
                EnableTypeChecking = checking,
                EnforceEffects = false
            });
            Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.EffectRowMisplaced);
        }
    }

    private static CompilationResult Compile(string source, bool checking)
    {
        var result = Program.Compile(source, "nullable-typing.calr", new CompilationOptions
        {
            EnableTypeChecking = checking,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        return result;
    }

    private static void AssertNoTypingNoise(CompilationResult result)
        => Assert.DoesNotContain(result.Diagnostics,
            d => d.Code is DiagnosticCode.UndefinedReference or DiagnosticCode.TypeMismatch);

    private static (BoundModule Module, DiagnosticBag Diagnostics) Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        return (new Binder(diagnostics).Bind(module), diagnostics);
    }

    private static IEnumerable<string> RoundTrip(string source)
    {
        yield return source;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        yield return new CalorEmitter().Emit(module);
    }

    private static Assembly Emit(string csharp)
    {
        var compilation = CSharpCompilation.Create("NullableTyping_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(csharp)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
