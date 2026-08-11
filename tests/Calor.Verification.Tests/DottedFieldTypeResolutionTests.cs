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
    private const string Source = """
        §M{m001:DottedFields}
          §CL{o001:Outer:pub:partial}
            §CL{b001:Base:pub:partial}
              §FLD{i64:Wide:pub}
              §FLD{i16:Hidden:priv}
          §CL{o002:Outer:pub:partial}
            §CL{b002:Base:pub:partial}
              §FLD{u8:Small:int}
            §CL{d001:Derived:Base}
              §FLD{bool:Own:priv}
          §F{f001:WideBoundary:pub} (Outer.Derived:item) -> bool
            §S (>= item.Wide INT:-2147483648)
            §R true
          §F{f002:SmallBoundary:pub} (Outer.Derived:item) -> bool
            §S (&& (>= item.Small INT:0) (<= item.Small INT:255))
            §R true
          §F{f003:MissingBoundary:pub} (Outer.Derived:item) -> bool
            §S (>= item.Missing INT:-2147483648)
            §R true
        """;

    private const string ProductionSource = """
        §M{m001:DottedProduction}
          §CL{b001:Base:pub}
            §FLD{i64:Wide:pub}
            §FLD{u8:Small:pub}
          §CL{d001:Derived:Base}
            §FLD{bool:Own:priv}
          §F{f001:WideBoundary:pub} (Derived:item) -> bool
            §S (>= item.Wide INT:-2147483648)
            §R true
          §F{f002:SmallBoundary:pub} (Derived:item) -> bool
            §S (&& (>= item.Small INT:0) (<= item.Small INT:255))
            §R true
        """;

    [Fact]
    public void RegistryMergesNestedPartialsAndInheritedAccessibleFields()
    {
        var module = Parse(Source);
        var registry = ContractTranslator.BuildUserTypeRegistry(module);

        Assert.Equal("i64", registry["outer.base"]["Wide"]);
        Assert.Equal("u8", registry["outer.base"]["Small"]);
        Assert.Equal("i16", registry["outer.base"]["Hidden"]);
        Assert.Equal("i64", registry["outer.derived"]["Wide"]);
        Assert.Equal("u8", registry["outer.derived"]["Small"]);
        Assert.False(registry["outer.derived"].ContainsKey("Hidden"));

        Assert.Same(registry["outer.base"], registry["base"]);
        Assert.Same(registry["outer.derived"], registry["derived"]);

        var changedModule = Parse(Source.Replace(
            "§FLD{i64:Wide:pub}",
            "§FLD{i32:Wide:pub}",
            StringComparison.Ordinal));
        var changedRegistry = ContractTranslator.BuildUserTypeRegistry(changedModule);
        Assert.NotEqual(
            ContractTranslator.BuildUserTypeRegistryCacheScope(registry),
            ContractTranslator.BuildUserTypeRegistryCacheScope(changedRegistry));
    }

    [Fact]
    public void ParsedDottedReferencesUseInheritedI64AndU8SortsWithoutGuessing()
    {
        Assert.True(
            Z3ContextFactory.IsAvailable,
            "Dotted field sort regression cannot run: Z3 is unavailable.");

        var module = Parse(Source);
        var registry = ContractTranslator.BuildUserTypeRegistry(module);
        var wideCondition = module.Functions
            .Single(function => function.Name == "WideBoundary")
            .Postconditions.Single().Condition;
        var smallCondition = module.Functions
            .Single(function => function.Name == "SmallBoundary")
            .Postconditions.Single().Condition;

        var wideReference = FindDottedReference(wideCondition, "item.Wide");
        var smallReference = FindDottedReference(smallCondition, "item.Small");
        Assert.Equal("item.Wide", wideReference.Name);
        Assert.Equal("item.Small", smallReference.Name);

        var inheritedWide = TranslateAndCheckNegation(
            wideCondition,
            wideReference,
            registry);
        var inheritedSmall = TranslateAndCheckNegation(
            smallCondition,
            smallReference,
            registry);

        Assert.Equal(64u, inheritedWide.Width);
        Assert.Equal(Status.SATISFIABLE, inheritedWide.NegatedStatus);
        Assert.Equal(8u, inheritedSmall.Width);
        Assert.Equal(Status.UNSATISFIABLE, inheritedSmall.NegatedStatus);

        var i32Mutation = registry.ToDictionary(
            type => type.Key,
            type => (IReadOnlyDictionary<string, string>)
                new Dictionary<string, string>(type.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
        i32Mutation["outer.derived"] =
            new Dictionary<string, string>(registry["outer.derived"], StringComparer.Ordinal)
            {
                ["Wide"] = "i32"
            };
        var historicalFallback = TranslateAndCheckNegation(
            wideCondition,
            wideReference,
            i32Mutation);

        Assert.Equal(32u, historicalFallback.Width);
        Assert.Equal(
            Status.UNSATISFIABLE,
            historicalFallback.NegatedStatus);
    }

    [Fact]
    public void ParsedMissingDottedFieldIsExplicitlyRefused()
    {
        Assert.True(
            Z3ContextFactory.IsAvailable,
            "Dotted field refusal regression cannot run: Z3 is unavailable.");

        var module = Parse(Source);
        var registry = ContractTranslator.BuildUserTypeRegistry(module);
        var function = module.Functions.Single(candidate => candidate.Name == "MissingBoundary");
        var condition = function.Postconditions.Single().Condition;

        using var context = Z3ContextFactory.Create();
        var translator = new ContractTranslator(context);
        translator.SetUserTypeRegistry(registry);
        Assert.True(translator.DeclareVariable("item", "Outer.Derived"));

        Assert.Null(translator.TranslateBoolExpr(condition));
        Assert.Contains(
            "field 'outer.derived.Missing' has no registered type",
            translator.LastRefusalReason,
            StringComparison.Ordinal);
    }

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
            result.GetFunctionResult("f001")!.PostconditionResults);
        var small = Assert.Single(
            result.GetFunctionResult("f002")!.PostconditionResults);
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
