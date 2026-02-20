using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for R2 (§RAW C# passthrough) and R6 (LINQ conversion produces valid AST nodes).
/// </summary>
public class RawPassthroughAndLinqAstTests
{
    #region R2: §RAW C# passthrough

    [Fact]
    public void Lexer_RawBlock_TokenizesAsSingleToken()
    {
        var source = "§RAW\nvar x = 1;\n§/RAW";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        // Should be: RawCSharp + EOF
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TokenKind.RawCSharp, tokens[0].Kind);
        Assert.Equal(TokenKind.Eof, tokens[1].Kind);
    }

    [Fact]
    public void Lexer_RawBlock_CapturesContentWithoutTokenizing()
    {
        var source = "§RAW\nvar x = items.Where(i => i > 5).ToList();\nConsole.WriteLine(x);\n§/RAW";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Equal(TokenKind.RawCSharp, tokens[0].Kind);
        var content = tokens[0].Value as string;
        Assert.NotNull(content);
        Assert.Contains("items.Where(i => i > 5)", content);
        Assert.Contains("Console.WriteLine(x)", content);
    }

    [Fact]
    public void Lexer_RawBlock_UnterminatedReportsError()
    {
        var source = "§RAW\nvar x = 1;";
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        _ = lexer.TokenizeAll();

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Message.Contains("§/RAW"));
    }

    [Fact]
    public void Parser_RawBlock_CreatesRawCSharpNode()
    {
        var source = @"
§M{m1:Test}
§F{f1:Main:pub}
  §O{void}
  §RAW
  var query = from x in items
              where x > 5
              select x * 2;
  §/RAW
§/F{f1}
§/M{m1}
";
        var module = ParseModule(source);
        var func = Assert.Single(module.Functions);
        var rawNode = Assert.Single(func.Body.OfType<RawCSharpNode>());
        Assert.Contains("from x in items", rawNode.CSharpCode);
        Assert.Contains("select x * 2", rawNode.CSharpCode);
    }

    [Fact]
    public void CSharpEmitter_RawBlock_EmitsContentVerbatim()
    {
        var source = @"
§M{m1:Test}
§F{f1:Main:pub}
  §O{void}
  §RAW
  var query = from x in items
              where x > 5
              select x * 2;
  §/RAW
§/F{f1}
§/M{m1}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("from x in items", csharp);
        Assert.Contains("select x * 2", csharp);
    }

    [Fact]
    public void Parser_RawBlock_PreservesCSharpSyntaxCharacters()
    {
        // Verify that C# syntax characters (braces, semicolons, etc.) pass through
        var source = "§M{m1:Test}\n§F{f1:Main:pub}\n  §O{void}\n  §RAW\n  if (x > 0) { Console.WriteLine(x); }\n  §/RAW\n§/F{f1}\n§/M{m1}\n";
        var csharp = ParseAndEmit(source);
        Assert.Contains("if (x > 0)", csharp);
        Assert.Contains("Console.WriteLine", csharp);
    }

    #endregion

    #region R6: LINQ conversion produces valid AST nodes

    [Fact]
    public void ConvertQueryExpression_Where_ProducesCallExpressionNode()
    {
        var csharp = """
            using System.Linq;
            public class Test
            {
                public void Run()
                {
                    var items = new[] { 1, 2, 3, 4, 5 };
                    var result = from x in items where x > 3 select x;
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        // The conversion should succeed (no exceptions)
        Assert.NotNull(result.Ast);

        // The LINQ query should NOT produce a ReferenceNode with raw Calor text
        // (which was the old behavior). Instead it should produce proper AST nodes.
        var allNodes = CollectAllNodes(result.Ast);
        var referenceNodes = allNodes.OfType<ReferenceNode>().ToList();

        // No ReferenceNode should contain raw Calor syntax like "§C{" or "§/C"
        foreach (var refNode in referenceNodes)
        {
            Assert.DoesNotContain("§C{", refNode.Name);
            Assert.DoesNotContain("§/C", refNode.Name);
            Assert.DoesNotContain("§LAM", refNode.Name);
        }
    }

    [Fact]
    public void ConvertQueryExpression_WhereSelect_ProducesChainedCallNode()
    {
        var csharp = """
            using System.Linq;
            public class Test
            {
                public void Run()
                {
                    var items = new[] { 1, 2, 3, 4, 5 };
                    var result = from x in items where x > 3 select x * 2;
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.NotNull(result.Ast);

        // MakeChainedCall serializes intermediate calls into the Target string,
        // so only the outermost call (Select) is a node. The Where call is
        // embedded in the Target string.
        var allNodes = CollectAllNodes(result.Ast);
        var callNodes = allNodes.OfType<CallExpressionNode>().ToList();

        Assert.True(callNodes.Count >= 1,
            $"Expected at least 1 CallExpressionNode, got {callNodes.Count}");

        // The outermost call is Select, with the Where call serialized in its target
        var outerCall = callNodes.First(c => c.Target.Contains("Select"));
        Assert.Contains("Where", outerCall.Target);

        // The Select call should have a lambda argument for the projection
        var lambdaArg = outerCall.Arguments.OfType<LambdaExpressionNode>().FirstOrDefault();
        Assert.NotNull(lambdaArg);
    }

    [Fact]
    public void ConvertQueryExpression_Where_HasLambdaArgument()
    {
        var csharp = """
            using System.Linq;
            public class Test
            {
                public void Run()
                {
                    var items = new[] { 1, 2, 3 };
                    var result = from x in items where x > 1 select x;
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.NotNull(result.Ast);

        var allNodes = CollectAllNodes(result.Ast);
        var callNodes = allNodes.OfType<CallExpressionNode>().ToList();
        var whereCall = callNodes.FirstOrDefault(c => c.Target.Contains("Where"));

        Assert.NotNull(whereCall);
        // The Where call should have a LambdaExpressionNode as an argument
        var lambdaArg = whereCall.Arguments.OfType<LambdaExpressionNode>().FirstOrDefault();
        Assert.NotNull(lambdaArg);
        Assert.NotEmpty(lambdaArg.Id);
        Assert.Single(lambdaArg.Parameters);
    }

    [Fact]
    public void ConvertQueryExpression_Join_FoldsSelectIntoResultSelector()
    {
        var csharp = """
            using System.Linq;
            public class Test
            {
                public void Run()
                {
                    var orders = new[] { new { Id = 1, ProductId = 10 } };
                    var products = new[] { new { Id = 10, Name = "Widget" } };
                    var result = from o in orders
                                 join p in products on o.ProductId equals p.Id
                                 select new { o.Id, p.Name };
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.NotNull(result.Ast);

        var allNodes = CollectAllNodes(result.Ast);
        var callNodes = allNodes.OfType<CallExpressionNode>().ToList();

        // The select projection is folded into the Join's result selector (4th arg),
        // so the outermost call IS the Join — no separate Select call.
        var joinCall = callNodes.FirstOrDefault(c => c.Target.Contains("Join"));
        Assert.NotNull(joinCall);

        // Join should have 4 arguments: inner collection, outer key, inner key, result selector
        Assert.Equal(4, joinCall.Arguments.Count);

        // The result selector (4th arg) should be a 2-parameter lambda with the projection
        var resultLambda = joinCall.Arguments[3] as LambdaExpressionNode;
        Assert.NotNull(resultLambda);
        Assert.Equal(2, resultLambda.Parameters.Count);
    }

    #endregion

    #region Helpers

    private static ModuleNode ParseModule(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        return module;
    }

    private static string ParseAndEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    /// <summary>
    /// Recursively collects all AST nodes from a module for inspection.
    /// </summary>
    private static List<AstNode> CollectAllNodes(ModuleNode module)
    {
        var nodes = new List<AstNode>();
        CollectNodes(module, nodes);
        return nodes;
    }

    private static void CollectNodes(AstNode node, List<AstNode> results)
    {
        results.Add(node);

        switch (node)
        {
            case ModuleNode m:
                foreach (var f in m.Functions) CollectNodes(f, results);
                foreach (var c in m.Classes) CollectNodes(c, results);
                break;
            case FunctionNode f:
                foreach (var s in f.Body) CollectNodes(s, results);
                break;
            case ClassDefinitionNode c:
                foreach (var method in c.Methods) CollectNodes(method, results);
                break;
            case MethodNode method:
                foreach (var s in method.Body) CollectNodes(s, results);
                break;
            case BindStatementNode b:
                if (b.Initializer != null) CollectNodes(b.Initializer, results);
                break;
            case CallExpressionNode call:
                foreach (var a in call.Arguments) CollectNodes(a, results);
                break;
            case LambdaExpressionNode lam:
                if (lam.ExpressionBody != null) CollectNodes(lam.ExpressionBody, results);
                if (lam.StatementBody != null)
                    foreach (var s in lam.StatementBody) CollectNodes(s, results);
                break;
            case ExpressionStatementNode es:
                CollectNodes(es.Expression, results);
                break;
        }
    }

    #endregion
}
