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
    [Theory]
    [InlineData("03_contracts", 3)]
    [InlineData("07_quantifiers", 5)]
    [InlineData("08_contract_inheritance_z3", 4)]
    public void EmbeddedContractGolden_PreservesFrozenBodiesAndParsedNativeMetadata(string name, int count)
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
        Assert.Equal(
            normalizer.Visit(CSharpSyntaxTree.ParseText(projected).GetRoot())!.DescendantTokens()
                .Select(token => (token.RawKind, token.Text)),
            normalizer.Visit(CSharpSyntaxTree.ParseText(result.ActualOutput!).GetRoot())!.DescendantTokens()
                .Select(token => (token.RawKind, token.Text)));
        Assert.Equal(scenario.ExpectedOutput, File.ReadAllText(Path.Combine(root, "tests", "E2E", "scenarios", name, "output.g.cs")));
    }

    private static List<ObjectCreationExpressionSyntax> ContractCreations(string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(node => node.Type.ToString() == "Calor.Runtime.ContractViolationException").ToList();

    private sealed class NativeGroupingNormalizer : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node) =>
            Visit(node.Expression);

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
