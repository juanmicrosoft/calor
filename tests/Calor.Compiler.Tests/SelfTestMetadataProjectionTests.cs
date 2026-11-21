using System.Globalization;
using Calor.Compiler.Ast;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.SelfTest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Calor.Compiler.Tests;

public class SelfTestMetadataProjectionTests
{
    // #1618: the self-tests state preconditions as parameter refinements, whose entry checks
    // are ArgumentOutOfRangeException guards, so only postconditions remain contract
    // creations; the frozen baselines were re-frozen from the rewritten embedded goldens.
    [Theory]
    [InlineData("03_contracts", 1)]
    [InlineData("07_quantifiers", 1)]
    [InlineData("08_contract_inheritance_z3", 2)]
    public void EmbeddedContractGolden_ChangesOnlyParsedOwnerAndLocation(string name, int count)
    {
        var root = CliTestHarness.FindRepoRoot();
        var baseline = File.ReadAllText(Path.Combine(root, "tests", "TestData", "self-test-metadata", name + ".g.cs.txt"));
        var scenario = Assert.Single(SelfTestRunner.LoadScenarios(), scenario => scenario.Name == name);
        var parsed = CalorSourceHelper.Parse(scenario.Input.Replace("\r\n", "\n"));
        Assert.True(parsed.IsSuccess, string.Join("\n", parsed.Errors));
        var module = parsed.Ast!;
        var contracts = module.Functions
            .SelectMany(function => Contracts(function.Id, function.Preconditions, function.Postconditions))
            .Concat(module.Classes.SelectMany(type => type.Methods)
                .SelectMany(method => Contracts(method.Id, method.Preconditions, method.Postconditions))).ToList();
        Assert.Equal(count, contracts.Count);
        var result = SelfTestRunner.Run(scenario);
        Assert.True(result.Passed, result.Error ?? result.Diff);
        var actualCreations = ContractCreations(result.ActualOutput!);
        Assert.Equal(count, actualCreations.Count);

        var creations = CSharpSyntaxTree.ParseText(baseline).GetRoot()
            .DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Where(node => node.Type.ToString() == "Calor.Runtime.ContractViolationException").ToList();
        Assert.Equal(count, creations.Count);
        var changes = new List<TextChange>();
        foreach (var ((creation, actualCreation), contract) in creations.Zip(actualCreations).Zip(contracts))
        {
            var actual = actualCreation.ArgumentList!.Arguments;
            Assert.Equal($"\"{contract.Id}\"", actual[1].Expression.ToString());
            foreach (var (field, value) in new[]
            {
                ("startOffset", contract.Span.Start), ("length", contract.Span.Length),
                ("line", contract.Span.Line), ("column", contract.Span.Column)
            })
                Assert.Equal(value.ToString(CultureInfo.InvariantCulture),
                    Assert.Single(actual, argument => argument.NameColon?.Name.Identifier.ValueText == field)
                        .Expression.ToString());
            var condition = Assert.Single(actual, argument => argument.NameColon?.Name.Identifier.ValueText == "condition");
            Assert.False(string.IsNullOrEmpty(Assert.IsType<LiteralExpressionSyntax>(condition.Expression).Token.ValueText));
            Assert.StartsWith(contract.Kind == "Requires" ? "requires" : "=>",
                scenario.Input.Substring(contract.Span.Start, contract.Span.Length));
            var arguments = creation.ArgumentList!.Arguments;
            Assert.Equal("Calor.Runtime.ContractKind." + contract.Kind, arguments[2].Expression.ToString());
            changes.Add(new TextChange(arguments[1].Expression.Span, $"\"{contract.Id}\""));
            foreach (var argument in arguments.Where(argument => argument.NameColon != null))
            {
                var value = argument.NameColon!.Name.Identifier.ValueText switch
                {
                    "startOffset" => contract.Span.Start,
                    "length" => contract.Span.Length,
                    "line" => contract.Span.Line,
                    "column" => contract.Span.Column,
                    _ => (int?)null
                };
                if (value.HasValue)
                    changes.Add(new TextChange(argument.Expression.Span, value.Value.ToString(CultureInfo.InvariantCulture)));
            }
        }

        var projected = SourceText.From(baseline).WithChanges(changes).ToString();
        var normalizer = new NativeGroupingNormalizer();
        Assert.True(SyntaxFactory.AreEquivalent(
            normalizer.Visit(CSharpSyntaxTree.ParseText(projected).GetRoot()),
            normalizer.Visit(CSharpSyntaxTree.ParseText(result.ActualOutput!).GetRoot())),
            "Native grouping normalization must preserve the frozen C# syntax tree, not just its token order.");
        Assert.Equal(scenario.ExpectedOutput, File.ReadAllText(Path.Combine(root, "tests", "E2E", "scenarios", name, "output.g.cs")));
    }

    private static List<ObjectCreationExpressionSyntax> ContractCreations(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(node => node.Type.ToString() == "Calor.Runtime.ContractViolationException").ToList();

    [Fact]
    public void NativeGroupingNormalization_DoesNotHideChangedOperatorPrecedence()
    {
        var normalizer = new NativeGroupingNormalizer();
        var original = normalizer.Visit(SyntaxFactory.ParseExpression("(a + b) * c"));
        var redundantGrouping = normalizer.Visit(SyntaxFactory.ParseExpression("((a + b)) * (c)"));
        var changedGrouping = normalizer.Visit(SyntaxFactory.ParseExpression("a + (b * c)"));
        Assert.True(SyntaxFactory.AreEquivalent(original, redundantGrouping));
        Assert.False(SyntaxFactory.AreEquivalent(original, changedGrouping));
        Assert.True(SyntaxFactory.AreEquivalent(
            normalizer.Visit(SyntaxFactory.ParseExpression("(i >= 0 && i < n) && (j >= 0 && j < m)")),
            normalizer.Visit(SyntaxFactory.ParseExpression("i >= 0 && i < n && j >= 0 && j < m"))));
        Assert.False(SyntaxFactory.AreEquivalent(
            normalizer.Visit(SyntaxFactory.ParseExpression("i >= 0 && i < n")),
            normalizer.Visit(SyntaxFactory.ParseExpression("i < n && i >= 0"))));
    }

    private sealed class NativeGroupingNormalizer : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node) =>
            Visit(node.Expression);

        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var visited = (BinaryExpressionSyntax)base.VisitBinaryExpression(node)!;
            if (!visited.IsKind(SyntaxKind.LogicalAndExpression))
                return visited;
            // Native quantifiers group these integer range checks by dimension.
            var operands = Conjuncts(visited).ToArray();
            if (operands.Any(operand => operand is not BinaryExpressionSyntax comparison
                || !comparison.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
                    && !comparison.IsKind(SyntaxKind.LessThanExpression)))
                return visited;
            return operands.Aggregate((left, right) =>
                SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, left, right));
        }

        private static IEnumerable<ExpressionSyntax> Conjuncts(ExpressionSyntax expression) =>
            expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.LogicalAndExpression)
                ? Conjuncts(binary.Left).Concat(Conjuncts(binary.Right))
                : [expression];

        public override SyntaxNode? VisitArgument(ArgumentSyntax node)
        {
            if (node.Expression is LiteralExpressionSyntax literal
                && node.Parent?.Parent is ObjectCreationExpressionSyntax creation
                && creation.Type.ToString() == "Calor.Runtime.ContractViolationException")
            {
                var text = literal.Token.ValueText;
                var prefix = text.StartsWith("Precondition failed: ", StringComparison.Ordinal) ? "Precondition failed: "
                    : text.StartsWith("Postcondition failed: ", StringComparison.Ordinal) ? "Postcondition failed: " : "";
                if (prefix.Length > 0 || node.NameColon?.Name.Identifier.ValueText == "condition")
                {
                    var expression = Visit(SyntaxFactory.ParseExpression(text[prefix.Length..]))!;
                    return node.WithExpression(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(prefix + string.Concat(expression.DescendantTokens().Select(token => token.Text)))));
                }
            }
            return base.VisitArgument(node);
        }
    }

    private static IEnumerable<(string Id, string Kind, Calor.Compiler.Parsing.TextSpan Span)> Contracts(
        string id, IReadOnlyList<RequiresNode> preconditions, IReadOnlyList<EnsuresNode> postconditions) =>
        preconditions.Select(contract => (id, "Requires", contract.Span))
            .Concat(postconditions.Select(contract => (id, "Ensures", contract.Span)));
}
