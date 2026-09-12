using System.Reflection;
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
    [InlineData("?str")]
    [InlineData("?string")]
    [InlineData("str?")]
    [InlineData("string?")]
    [InlineData("?System.String")]
    public void NullableStringLiteralAndInlineParameter_AgreeWithGeneratedRuntime(string spelling)
    {
        var source = $$"""
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} ({{spelling}}:input) -> {{spelling}}
                §E{}
                §B{literal:{{spelling}}} "value"
                §B{copy:{{spelling}}} input
                §R literal
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
    public void NullableBclReturn_IsAReferenceAndRetainsNullAtRuntime(string spelling)
    {
        // This annotation assertion uses the repository-scoped metadata profile, not its Oblivious fallback.
        using var metadataContext = MetadataContext.Create();
        var key = "CALOR_T1_ABSENT_" + Guid.NewGuid().ToString("N");
        Assert.Null(Environment.GetEnvironmentVariable(key));
        var source = $$"""
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} () -> {{spelling}}
                §E{env}
                §B{value:{{spelling}}} §C{System.Environment.GetEnvironmentVariable} §A "{{key}}" §/C
                §R value
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
            §M{m1:NullableTyping}
              §CL{c1:Foo:pub}
                §FLD{i32:value:pub}
              §F{f1:Probe:pub} (?Foo:input) -> ?Foo
                §E{alloc}
                §B{copy:?Foo} input
                §B{created:?Foo} §NEW{Foo}
                §R created
            """;
        foreach (var text in RoundTrip(source))
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(text, checking);
            AssertNoTypingNoise(result);
            Assert.DoesNotContain("Calor.Runtime.Option", result.GeneratedCode);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal("Foo", method.ReturnType.Name);
            Assert.Equal(method.ReturnType, Assert.Single(method.GetParameters()).ParameterType);
            Assert.NotNull(method.Invoke(null, [null]));
        }
    }

    [Fact]
    public void ExplicitOptionConstructionAndConsumption_KeepTheirRuntimeWrapper()
    {
        const string source = """
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} (bool:present) -> Option<str>
                §E{alloc}
                §IF{if1} present
                  §R §SM "value"
                §EL
                  §R §NN{str}
            """;
        foreach (var text in RoundTrip(source))
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(text, checking);
            AssertNoTypingNoise(result);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(typeof(Option<string>), method.ReturnType);
            Assert.Equal("value", Assert.IsType<Option<string>>(method.Invoke(null, [true])).Unwrap());
            Assert.True(Assert.IsType<Option<string>>(method.Invoke(null, [false])).IsNone);
        }
    }

    [Theory]
    [InlineData("?str", "§SM \"value\"")]
    [InlineData("?string", "§NN{str}")]
    [InlineData("str", "§SM \"value\"")]
    [InlineData("Option<str>", "\"value\"")]
    [InlineData("Option<str>", "input")]
    public void ReferenceOptionMismatches_AreNotImplicitConversions(string target, string expression)
    {
        var source = $$"""
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{alloc}
                §B{value:{{target}}} {{expression}}
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
            Assert.Contains(result.Diagnostics.Errors, d => d.Span.Line == 4);
        }
        var (_, diagnostics) = Bind(source);
        Assert.Contains(diagnostics.Errors, BindingDiagnosticPolicy.IsCompilationError);
    }

    [Fact]
    public void ExpandedNullableAssignment_RetainsTransitionalRejectionAndAnalysisOwnership()
    {
        const string source = """
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{x:?str} null
                §B{y:str} x
            """;
        var rejected = Program.Compile(source, "nullable-typing.calr");
        var error = Assert.Single(rejected.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.TypeMismatch, error.Code);
        Assert.Equal(5, error.Span.Line);
        Assert.Null(error.BindingContext);
        Assert.DoesNotContain(rejected.Diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
        var accepted = Compile(source, checking: false);
        Assert.True(GeneratedCSharpCompiler.Validate(accepted.GeneratedCode).CompilationSuccess);
        var (_, diagnostics) = Bind(source);
        var analysis = Assert.Single(diagnostics.Errors,
            d => d.Code == DiagnosticCode.NullableToNonNullableBinding);
        Assert.Equal(DiagnosticCode.NullableToNonNullableBinding, analysis.Code);
        Assert.Equal(5, analysis.Span.Line);
        Assert.True(BindingDiagnosticPolicy.IsAnalysisOnly(analysis));
    }

    [Fact]
    public void RawInlineNullableFlow_IsNotNewlyActivatedByNormalizingItsType()
    {
        const string source = """
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} (?str:input) -> void
                §E{}
                §B{y:str} input
            """;
        foreach (var checking in new[] { true, false })
            AssertNoTypingNoise(Compile(source, checking));
        var (_, diagnostics) = Bind(source);
        Assert.True(BindingDiagnosticPolicy.IsAnalysisOnly(Assert.Single(diagnostics.Errors)));
    }

    [Fact]
    public void NullableNativeArguments_KeepRejectionWithNullabilityOwnership()
    {
        const string source = """
            §M{m1:NullableTyping}
              §F{f1:Take:pub} (str:input) -> void
                §E{env}
              §F{f2:Probe:pub} () -> void
                §E{env}
                §C{Take} §A §C{System.Environment.GetEnvironmentVariable} §A "PATH" §/C §/C
            """;
        var (bound, diagnostics) = Bind(source);
        var call = Assert.IsType<BoundCallStatement>(bound.Functions[1].Body[0]);
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
            §M{m1:NullableTyping}
              §F{take:Take:pub} (?str:value) -> ?str
                §E{}
                §R value
              §F{probe:Probe:pub} (?str:value) -> ?str
                §E{}
                §R §C{Take} §A value §/C
              §F{nonNull:NonNull:pub} (str:value) -> ?str
                §E{}
                §R §C{Take} §A value §/C
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
    public void PreviouslyAcceptedObjectAlternative_KeepsClrOverloadSelection_NotSafety()
    {
        const string source = """
            §M{m1:NullableTyping}
              §F{takeString:Take:pub} (str:value) -> i32
                §E{}
                §R 1
              §F{takeObject:Take:pub} (object:value) -> i32
                §E{}
                §R 2
              §F{probe:Probe:pub} (?str:value) -> i32
                §E{}
                §R §C{Take} §A value §/C
            """;
        var (_, diagnostics) = Bind(source);
        Assert.True(BindingDiagnosticPolicy.IsAnalysisOnly(Assert.Single(diagnostics.Errors)));
        var result = Program.Compile(source, "native-string-overload-runtime.calr");
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics));
        var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
        Assert.Equal(1, method.Invoke(null, [null]));
        Assert.Equal(1, method.Invoke(null, ["live"]));
    }

    [Theory]
    [InlineData("?str", "42")]
    [InlineData("i32", "\"value\"")]
    [InlineData("?i32", "42")]
    public void GenuineMismatchesAndUnimplementedNullableValues_DoNotBecomeUniversallyAssignable(
        string target, string expression)
    {
        var result = Program.Compile($$"""
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} () -> void
                §E{}
                §B{value:{{target}}} {{expression}}
            """, "nullable-typing.calr");
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch && d.Span.Line == 4);
    }

    [Theory]
    [InlineData("i32[]", "str[]")]
    [InlineData("[i32]", "[str]")]
    [InlineData("Option<i32>", "Option<str>")]
    [InlineData("List<i32>", "List<str>")]
    public void ArrayAndGenericMismatches_KeepTheirExistingTypeChecks(string sourceType, string target)
    {
        var result = Program.Compile($$"""
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} ({{sourceType}}:input) -> void
                §E{}
                §B{value:{{target}}} input
            """, "nullable-typing.calr");
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch);
    }

    [Fact]
    public void NullableAndOptionTypeModels_HaveDistinctIdentitiesAndHashes()
    {
        var nullable = new NullableReferenceType(PrimitiveType.String);
        var option = new OptionType(PrimitiveType.String);
        Assert.NotEqual<CalorType>(nullable, option);
        Assert.Equal("?str", nullable.SurfaceName);
        Assert.Equal("Option<str>", option.SurfaceName);
        Assert.Equal(2, new HashSet<CalorType> { nullable, option }.Count);
        Assert.NotEqual(TypeIdentity.Canonicalize("?Option<str>"), TypeIdentity.Canonicalize("Option<?str>"));
        Assert.NotEqual(TypeIdentity.Canonicalize("str?[]"), TypeIdentity.Canonicalize("?str[]"));
        Assert.Equal("System.String", TypeIdentity.MapShortTypeNameToFullName("?str"));
        Assert.Equal("System.Nullable`1", TypeIdentity.MapShortTypeNameToFullName("?i32"));
        Assert.Equal("System.Nullable`1", TypeIdentity.MapShortTypeNameToFullName("?Option<str>"));
        Assert.Equal("Calor.Runtime.Option`1", TypeIdentity.MapShortTypeNameToFullName("Option<str>"));
        Assert.Throws<ArgumentException>(() => new NullableReferenceType(PrimitiveType.Int));
        Assert.Throws<ArgumentException>(() => new NullableReferenceType(option));
    }

    [Fact]
    public void NullableStringConcatenation_PreservesExistingNonNullRuntimeResult()
    {
        const string source = """
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} (?str:left, ?str:right) -> str
                §E{}
                §R (+ left right)
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
    [InlineData("[?str]", "string?[]")]
    [InlineData("Option<?str>", "Option<string?>")]
    public void NullablePayloads_DoNotCollapseTheirArrayOrRuntimeOptionContainer(string type, string emittedType)
    {
        var source = $$"""
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} ({{type}}:input) -> {{type}}
                §E{}
                §B{copy:{{type}}} input
                §R copy
            """;
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(source, checking);
            AssertNoTypingNoise(result);
            Assert.Contains(emittedType, result.GeneratedCode);
            Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(type.StartsWith("Option", StringComparison.Ordinal)
                ? typeof(Option<string>) : typeof(string[]), method.ReturnType);
        }
    }

    [Theory]
    [InlineData("?str", "§SM \"value\"")]
    [InlineData("Option<str>", "\"value\"")]
    public void ReturnRepresentationErrors_AreActiveWithoutTypeChecking(string returnType, string expression)
    {
        var source = $$"""
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} () -> {{returnType}}
                §E{alloc}
                §R {{expression}}
            """;
        foreach (var checking in new[] { true, false })
        foreach (var transpile in new[] { true, false })
        {
            var result = Program.Compile(source, "nullable-typing.calr", new CompilationOptions
            {
                EnableTypeChecking = checking,
                UnsafeTranspileOnly = transpile
            });
            var diagnostic = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.ReferenceOptionMismatch, diagnostic.Code);
            Assert.Equal(4, diagnostic.Span.Line);
            Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
        }
    }

    [Fact]
    public void LambdaReturn_DoesNotUseEnclosingReferenceReturnContract()
    {
        const string source = """
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} () -> str
                §E{alloc}
                §B{factory:Func<Option<str>>} §LAM{lam1}
                  §R §SM "value"
                §/LAM{lam1}
                §R "outer"
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
            §M{m1:NullableTyping}
              §CL{c1:Foo:pub}
                §FLD{i32:value:pub}
              §CL{c2:Value:pub:struct}
                §FLD{i32:value:pub}
              §EN{e1:Kind:pub}
                First
              §F{f1:Probe:pub} (?Foo:reference, ?Value:value, ?Kind:kind) -> void
                §E{}
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
            && d.Message.Contains("?Foo", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UndefinedReference
            && d.Message.Contains("?Value", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UndefinedReference
            && d.Message.Contains("?Kind", StringComparison.Ordinal));
    }

    [Fact]
    public void NullableObjectBoxing_PreservesTheWholeOptionValueWithoutUnwrapping()
    {
        const string source = """
            §M{m1:NullableTyping}
              §F{f1:Probe:pub} () -> ?object
                §E{alloc}
                §B{boxed:?object} §SM "value"
                §R boxed
            """;
        foreach (var checking in new[] { true, false })
        {
            var result = Compile(source, checking);
            AssertNoTypingNoise(result);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(typeof(object), method.ReturnType);
            Assert.Equal("value", Assert.IsType<Option<string>>(method.Invoke(null, null)).Unwrap());
        }
    }

    [Fact]
    public void NullableNominalMemberAccess_IsNotMisdiagnosedAsARecordOperation()
    {
        const string source = """
            §M{m1:NullableTyping}
              §CL{c1:Foo:pub}
                §FLD{i32:value:pub}
              §F{f1:Probe:pub} () -> i32
                §E{alloc}
                §B{input:?Foo} §NEW{Foo}
                §R input.value
            """;
        foreach (var checking in new[] { true, false })
        {
            // This unresolved member-effect path is outside the supported typing assertion.
            // Keep its current rejection visible; the effect opt-out isolates typing.
            var ordinary = Program.Compile(source, "nullable-typing.calr",
                new CompilationOptions { EnableTypeChecking = checking });
            Assert.Contains(ordinary.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
            var result = Program.Compile(source, "nullable-typing.calr", new CompilationOptions
            {
                EnableTypeChecking = checking,
                EnforceEffects = false
            });
            Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
            AssertNoTypingNoise(result);
            var method = Emit(result.GeneratedCode).GetType("NullableTyping.NullableTypingModule")!.GetMethod("Probe")!;
            Assert.Equal(0, method.Invoke(null, null));
        }

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryReceiverGetters_AreChargedInsteadOfBeingTreatedAsOptionMembers(bool conditional)
    {
        var source = $$"""
            §M{m1:ReceiverTyping}
              §U{System.Collections.Generic}
              §CL{c1:Parent:pub}
                §PROP{p1:Value:i32:pub}
                  §GET
                    §B{created:List<i32>} §NEW{List<i32>}
                    §R INT:3
              §CL{c2:Node:pub}
                §FLD{i32:Value:pub}
                §PROP{p2:Child:Node:pub}
                  §GET
                    §B{created:List<i32>} §NEW{List<i32>}
                    §B{child:Node} §NEW{Node}
                    §R child
              §CL{c3:Derived:pub}
                §EXT{Parent}
                §MT{mt1:Read:pub} (Node:input) -> i32?
                  §E{}
                  §R {{(conditional ? "input?.Child .Value" : "§BASE.Value")}}
            """;
        var rejected = Program.Compile(source, "receiver-typing.calr",
            new CompilationOptions { EnableTypeChecking = false });
        Assert.True(rejected.Diagnostics.Errors.Any(d => d.Code == DiagnosticCode.ForbiddenEffect
            && d.Message.Contains("Read", StringComparison.Ordinal)
            && d.Message.Contains("alloc", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, rejected.Diagnostics) + Environment.NewLine + rejected.GeneratedCode);

        var accepted = Compile(source.Replace("§E{}", "§E{alloc}", StringComparison.Ordinal), checking: false);
        var assembly = Emit(accepted.GeneratedCode);
        var derived = assembly.GetType("ReceiverTyping.Derived")!;
        var node = assembly.GetType("ReceiverTyping.Node")!;
        var method = derived.GetMethod("Read")!;
        Assert.Equal(conditional ? 0 : 3,
            method.Invoke(Activator.CreateInstance(derived), [Activator.CreateInstance(node)]));
        Assert.Equal(conditional ? null : (object)3,
            method.Invoke(Activator.CreateInstance(derived), [null]));
    }

    [Fact]
    public void UnknownReceiverSentinel_DoesNotResolveToPureRuntimeOptionMembers()
    {
        Assert.Equal("?", TypeIdentity.MapShortTypeNameToFullName("?"));
        const string source = """
            §M{m1:ReceiverTyping}
              §CL{c1:Node:pub}
                §FLD{i32:Value:pub}
              §F{f1:Probe:pub} (Node:input) -> i32
                §E{}
                §R (?? input input).Value
            """;
        var result = Program.Compile(source, "receiver-typing.calr",
            new CompilationOptions { EnableTypeChecking = false });
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownExternalCall
            && d.Message.Contains("?.get_Value", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Theory]
    [InlineData("?str", "\"value\"")]
    [InlineData("?Foo", "§NEW{Foo}")]
    [InlineData("?Value", "§NEW{Value}")]
    public void NullableNormalization_PreservesActiveNonFunctionRowErrors(string type, string expression)
    {
        var source = $$"""
            §M{m1:NullableTyping}
              §CL{c1:Foo:pub}
                §FLD{i32:value:pub}
              §CL{c2:Value:pub:struct}
                §FLD{i32:value:pub}
              §F{f1:Probe:pub} () -> void
                §E{alloc}
                §B{item:{{type}}} §E{} {{expression}}
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
