using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeIdentifierSemanticsTests
{
    [Theory]
    [InlineData("@null")]
    [InlineData("@true")]
    [InlineData("@false")]
    public void EscapedIntegerParameters_RetainActualSolverBindings(string name)
    {
        var module = Parse($"""
            module ProofIdentity
              func Identity (int:{name}) → int
                !
                => result == {name}
                return {name}
            """);
        foreach (var source in new[] { module, Parse(new CalorEmitter().Emit(module)) })
        {
            var function = Assert.Single(source.Functions);
            using var context = Z3ContextFactory.Create();
            using var verifier = new Z3Verifier(context);
            var result = verifier.VerifyPostcondition(
                function.Parameters.Select(parameter => (parameter.Name, parameter.TypeName)).ToArray(),
                function.Output!.TypeName, function.Preconditions,
                Assert.Single(function.Postconditions), function.Body);
            Assert.Equal(ProofStatus.Proven, result.EffectiveOutcome.Status);
        }
    }

    [Fact]
    public void NullLiteral_CannotAliasAnEscapedSolverVariableOrItsCacheKey()
    {
        using var context = Z3ContextFactory.Create();
        var translator = new ContractTranslator(context);
        Assert.True(translator.DeclareVariable("null", "int"));
        var literal = new ReferenceNode(default, "null");
        var variable = new ReferenceNode(default, "null", isVerbatim: true);
        Assert.NotNull(translator.Translate(variable));
        Assert.Null(translator.Translate(literal));
        Assert.Contains("Null literals", translator.LastRefusalReason);
        var hasher = new ContractHasher();
        var parameters = new[] { ("null", "int") };
        RequiresNode Condition(ReferenceNode reference) => new(default,
            new BinaryOperationNode(default, BinaryOperator.Equal, reference, new IntLiteralNode(default, 0)),
            null, new AttributeCollection());
        Assert.NotEqual(hasher.HashPrecondition(parameters, Condition(literal)),
            hasher.HashPrecondition(parameters, Condition(variable)));
        var arrays = new ContractTranslator(context);
        Assert.True(arrays.DeclareVariable("null", "int[]"));
        Assert.Null(arrays.Translate(new ArrayAccessNode(default, literal, new IntLiteralNode(default, 0))));
        Assert.Null(arrays.Translate(new ArrayLengthNode(default, literal)));
        Assert.NotNull(arrays.Translate(new ArrayAccessNode(default, variable, new IntLiteralNode(default, 0))));
        Assert.NotNull(arrays.Translate(new ArrayLengthNode(default, variable)));
    }

    [Theory]
    [InlineData("other != @null", true)]
    [InlineData("other != null", false)]
    public void OnlyGenuineNullComparisons_NarrowNullableStorage(string comparison, bool expectNullableReturn)
    {
        var source = $"""
            module NullNarrowing
              func Run (string?:@null, string?:other) → string
                !
                if {comparison}
                  return other
                return "fallback"
            """;
        var module = Parse(source);
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
        {
            var result = Compile(text);
            Assert.Equal(expectNullableReturn,
                result.Diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable));
            if (!expectNullableReturn)
                Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        }
    }

    [Fact]
    public void GenuineNullReturn_RemainsNullableWhenAnEscapedParameterHasTheSameName()
    {
        var result = Compile("""
            module NullReturn
              func Run (string:@null) → string
                !
                return null
            """);
        Assert.Contains(result.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    private static CompilationResult Compile(string source) => Program.Compile(source, "native-identifiers.calr",
        new CompilationOptions { EnableTypeChecking = true, EnforceEffects = true, StatusWriter = TextWriter.Null });

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return module;
    }
}
