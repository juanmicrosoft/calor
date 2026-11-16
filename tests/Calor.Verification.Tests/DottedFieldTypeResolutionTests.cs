using Calor.Compiler;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.Z3;
using Xunit;

namespace Calor.Verification.Tests;

public sealed class DottedFieldTypeResolutionTests
{
    [Fact]
    public void NativeCaseDistinctClasses_KeepTheirOwnFieldProofs()
    {
        const string source = """
            module CaseDistinctFields
              class Box public
                field long:Value public
              class box public
                field byte:Value public
              func WideBoundary public (Box:item) → bool
                !
                => item.Value >= -2147483648
                return true
              func SmallBoundary public (box:item) → bool
                !
                => item.Value >= 0 && item.Value <= 255
                return true
            """;
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
        };
        var compilation = Program.Compile(source, "case-distinct-fields.calr", options);
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors.Select(error => error.Message)));
        var registry = ContractTranslator.BuildUserTypeRegistry(compilation.Ast!);
        Assert.Equal("long", registry["Box"]["Value"]);
        Assert.Equal("byte", registry["box"]["Value"]);

        var wide = options.VerificationResults!.Functions.Single(function => function.FunctionName == "WideBoundary");
        var small = options.VerificationResults.Functions.Single(function => function.FunctionName == "SmallBoundary");
        Assert.Equal(ProofStatus.Refuted, Assert.Single(wide.PostconditionResults).EffectiveOutcome.Status);
        Assert.Equal(ProofStatus.Assumed, Assert.Single(small.PostconditionResults).EffectiveOutcome.Status);
    }

    [Fact]
    public void CaseDistinctUserTypes_DoNotShareSolverSorts()
    {
        using var context = Z3ContextFactory.Create();
        var translator = new ContractTranslator(context);
        Assert.True(translator.DeclareVariable("upper", "Box"));
        Assert.True(translator.DeclareVariable("lower", "box"));

        Assert.NotEqual(
            translator.Variables["upper"].Expr.Sort,
            translator.Variables["lower"].Expr.Sort);
    }

    [Theory]
    [InlineData("byte", 8u, false)]
    [InlineData("sbyte", 8u, true)]
    [InlineData("short", 16u, true)]
    [InlineData("ushort", 16u, false)]
    public void NativeSmallFieldTypes_KeepTheirWidthAndSignedness(string type, uint width, bool signed)
    {
        var module = Parse($$"""
            module SmallFieldTypes
              class Payload public
                field {{type}}:Value public
            """);
        using var context = Z3ContextFactory.Create();
        var translator = new ContractTranslator(context);
        translator.SetUserTypeRegistry(ContractTranslator.BuildUserTypeRegistry(module));
        Assert.True(translator.DeclareVariable("item", "Payload"));
        var field = new FieldAccessNode(default, new ReferenceNode(default, "item"), "Value");
        var translatedField = translator.TranslateBitVecExpr(field);
        Assert.NotNull(translatedField);
        Assert.Equal(width, translatedField.SortSize);

        var nonnegative = translator.TranslateBoolExpr(new BinaryOperationNode(
            default, BinaryOperator.GreaterOrEqual, field, new IntLiteralNode(default, 0)));
        Assert.NotNull(nonnegative);
        using var solver = context.MkSolver();
        solver.Assert(context.MkNot(nonnegative));
        Assert.Equal(signed ? Status.SATISFIABLE : Status.UNSATISFIABLE, solver.Check());
    }

    [Fact]
    public void CaseFoldedTypeCacheEntries_AreNotReused()
    {
        var entry = new VerificationCacheEntry
        {
            Version = "1.23",
            Status = ContractVerificationStatus.Proven
        };
        Assert.False(entry.IsValidFor(currentZ3Version: null));
    }

    private const string Source = """
        §M{DottedFields}
          §CL{Outer:pub:partial}
            §CL{Base:pub:partial}
              §FLD{i64:Wide:pub}
              §FLD{i16:Hidden:priv}
          §CL{Outer:pub:partial}
            §CL{Base:pub:partial}
              §FLD{u8:Small:int}
            §CL{Derived:Base}
              §FLD{bool:Own:priv}
          §F{WideBoundary:pub} (Outer.Derived:item) -> bool
            §S (>= item.Wide INT:-2147483648)
            §R true
          §F{SmallBoundary:pub} (Outer.Derived:item) -> bool
            §S (&& (>= item.Small INT:0) (<= item.Small INT:255))
            §R true
          §F{MissingBoundary:pub} (Outer.Derived:item) -> bool
            §S (>= item.Missing INT:-2147483648)
            §R true
        """;

    private const string ProductionSource = """
        module DottedProduction DottedProduction block
          class Base
            field long:Wide
            field byte:Small

          class Derived internal
            extends Base

            field bool:Own private

          func WideBoundary (Derived:item) → bool
            !
            => (item.Wide >= -2147483648)
            return true

          func SmallBoundary (Derived:item) → bool
            !
            => ((item.Small >= 0) && (item.Small <= 255))
            return true
        """;

    [Fact]
    public void ProductionPassUsesInheritedDottedTypesWithoutFalseProven()
    {
        Assert.True(
            Z3ContextFactory.IsAvailable,
            "Production dotted field regression cannot run: Z3 is unavailable.");

        var module = Parse(ProductionSource);
        var diagnostics = new DiagnosticBag();
        var result = new ContractVerificationPass(
            diagnostics,
            new VerificationOptions
            {
                CacheOptions = new VerificationCacheOptions { Enabled = false }
            })
            .Verify(module);

        var wide = Assert.Single(
            result.GetFunctionResult(module.Functions.Single(f => f.Name == "WideBoundary").Id)!.PostconditionResults);
        var small = Assert.Single(
            result.GetFunctionResult(module.Functions.Single(f => f.Name == "SmallBoundary").Id)!.PostconditionResults);
        Assert.Equal(ProofStatus.Refuted, wide.EffectiveOutcome.Status);
        Assert.Equal(ProofStatus.Assumed, small.EffectiveOutcome.Status);
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.ContractVerificationUnsupported);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(
            diagnostics.HasErrors,
            string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message)));
        return module;
    }

    private static (uint Width, Status NegatedStatus) TranslateAndCheckNegation(
        ExpressionNode condition,
        ReferenceNode dottedReference,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> registry)
    {
        using var context = Z3ContextFactory.Create();
        var translator = new ContractTranslator(context);
        translator.SetUserTypeRegistry(registry);
        Assert.True(translator.DeclareVariable("item", "Outer.Derived"));

        var translatedReference = translator.TranslateBitVecExpr(dottedReference);
        Assert.NotNull(translatedReference);
        var translatedCondition = translator.TranslateBoolExpr(condition);
        Assert.NotNull(translatedCondition);

        using var solver = context.MkSolver();
        solver.Assert(context.MkNot(translatedCondition));
        return (translatedReference.SortSize, solver.Check());
    }

    private static ReferenceNode FindDottedReference(ExpressionNode expression, string name)
    {
        return FindDottedReferenceOrNull(expression, name)
            ?? throw new Xunit.Sdk.XunitException($"Dotted reference '{name}' was not found.");
    }

    private static ReferenceNode? FindDottedReferenceOrNull(ExpressionNode expression, string name)
    {
        if (expression is ReferenceNode reference && reference.Name == name)
            return reference;
        foreach (var child in expression switch
                 {
                     BinaryOperationNode binary => new[] { binary.Left, binary.Right },
                     UnaryOperationNode unary => new[] { unary.Operand },
                     ImplicationExpressionNode implication =>
                         new[] { implication.Antecedent, implication.Consequent },
                     _ => Array.Empty<ExpressionNode>()
                 })
        {
            var found = FindDottedReferenceOrNull(child, name);
            if (found is not null)
                return found;
        }

        return null;
    }
}
