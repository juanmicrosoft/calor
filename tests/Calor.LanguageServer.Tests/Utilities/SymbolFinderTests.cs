using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Utilities;

public class SymbolFinderTests
{
    [Theory]
    [InlineData("WidgetException", "new WidgetException()", "WidgetException")]
    [InlineData("int", "value", "value")]
    public void NativeCollectionInitializers_ExposeNestedConstructorAndArgumentSymbols(
        string elementType, string element, string expected)
    {
        var source = $$"""
            module CollectionSymbols
              using System.Collections.Generic
              class WidgetException
              func Make (int:value) → List<{{elementType}}>
                ! alloc, mut
                return new List<{{elementType}}> { {{element}} }
            """;
        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);
        var offset = source.LastIndexOf(element, StringComparison.Ordinal)
            + (element.StartsWith("new ", StringComparison.Ordinal) ? "new ".Length : 0);
        var symbol = new SymbolFinderVisitor(offset, source).FindSymbol(ast);
        Assert.Equal(expected, symbol?.Name);
        Assert.Equal(expected, source.Substring(symbol!.Span.Start, symbol.Span.Length));
    }

    [Fact]
    public void NativeCollectionImplicitAdd_HasNoAuthoredRenameSite()
    {
        const string source = """
            module CollectionReferences
              class Tracked extends System.Collections.Generic.List<int>
                func Add (int:value) → void
                  !
                  return
              func Make () → Tracked
                ! alloc, mut
                return new Tracked {1}
            """;
        var state = LspTestHarness.CreateDocument(source);
        Assert.NotNull(state.BoundModule);
        var add = state.BoundModule.SymbolsById.Values.OfType<FunctionSymbol>()
            .Single(function => function.Name == "Tracked.Add");
        var creation = Assert.IsType<BoundNewExpression>(
            Assert.IsType<BoundReturnStatement>(
                Assert.Single(state.BoundModule.Functions.Single(function => function.Symbol.Name == "Make").Body)).Expression);
        Assert.Contains(Assert.Single(creation.CollectionInitializers).ResolvedSymbols, symbol => symbol.Id == add.Id);
        Assert.Empty(SymbolFinder.FindBoundReferences(state.BoundModule, add.Id, includeDeclaration: false));
        Assert.Equal([add.DeclarationSpan],
            SymbolFinder.FindBoundReferences(state.BoundModule, add.Id, includeDeclaration: true));
    }

    [Theory]
    [InlineData("for item from 0 to 1 step 1", "for item from 2 to 3 step 1", "")]
    [InlineData("for item = 0; item < 2; item = item + 1", "for item = 2; item < 4; item = item + 1", "")]
    [InlineData("each int:item in items", "each int:item in items", "")]
    [InlineData("each key, value in pairs", "each key, value in pairs", "")]
    [InlineData("while false", "while false", "")]
    [InlineData("do", "do", "while false")]
    public void Visitor_LoopScopesDoNotDependOnStatementIds(
        string firstHeader, string secondHeader, string trailingCondition)
    {
        var source = $$"""
            module Test
              func Probe (int[]:items, System.Collections.Generic.Dictionary<string,int>:pairs) → void
                ! cw
                {{firstHeader}}
                  let int:local = 1
                  System.Console.WriteLine(local)
                {{trailingCondition}}
                {{secondHeader}}
                  let int:local = 2
                  System.Console.WriteLine(local)
                {{trailingCondition}}
            """;
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        var firstOffset = source.IndexOf("WriteLine(local)", StringComparison.Ordinal) + "WriteLine(".Length;
        var secondOffset = source.LastIndexOf("WriteLine(local)", StringComparison.Ordinal) + "WriteLine(".Length;
        var first = new SymbolFinderVisitor(firstOffset, source).FindSymbol(ast);
        var second = new SymbolFinderVisitor(secondOffset, source).FindSymbol(ast);

        Assert.Equal("local", first?.Name);
        Assert.Equal("local", second?.Name);
        Assert.NotEqual(first!.Span, second!.Span);
        Assert.NotSame(first.Node, second.Node);
    }

    [Fact]
    public void Visitor_TypedLoopCompletionUsesTheBinderTypeAndDeclarationSpan()
    {
        const string source = """
            module Test
              func Probe () → void
                ! cw
                for long:item = 0; item < 2; item = item + 1
                  System.Console.WriteLine(item)
            """;
        var diagnostics = new DiagnosticBag();
        var ast = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        var offset = source.IndexOf("WriteLine(item)", StringComparison.Ordinal) + "WriteLine(".Length;
        var visitor = new SymbolFinderVisitor(offset, source);
        var symbol = Assert.Single(visitor.GetVisibleSymbols(ast), symbol => symbol.Name == "item");

        Assert.Equal("long", symbol.Type);
        Assert.Equal("item", source.Substring(symbol.Span.Start, symbol.Span.Length));
        Assert.Equal("long", visitor.FindSymbol(ast)?.Type);
    }

    [Fact]
    public void FindBoundReferences_SameNameSymbolsRemainSeparatedBySymbolId()
    {
        var firstId = SymbolId.Create("module", "function", "local:first");
        var secondId = SymbolId.Create("module", "function", "local:second");
        var first = new VariableSymbol(
            firstId,
            "value",
            "INT",
            isMutable: true,
            declarationSpan: new TextSpan(1, 2, 1, 1));
        var second = new VariableSymbol(
            secondId,
            "value",
            "INT",
            isMutable: true,
            declarationSpan: new TextSpan(3, 4, 1, 3));
        var functionSymbol = new FunctionSymbol(
            SymbolId.Create("module", "function"),
            "Test",
            "INT",
            [],
            declarationSpan: new TextSpan(0, 20, 1, 0));
        var function = new BoundFunction(
            functionSymbol.DeclarationSpan,
            functionSymbol,
            [
                new BoundBindStatement(first.DeclarationSpan, first, new BoundIntLiteral(first.DeclarationSpan, 1)),
                new BoundBindStatement(second.DeclarationSpan, second, new BoundIntLiteral(second.DeclarationSpan, 2)),
                new BoundExpressionStatement(
                    new TextSpan(10, 11, 1, 10),
                    new BoundVariableExpression(new TextSpan(10, 11, 1, 10), first)),
                new BoundExpressionStatement(
                    new TextSpan(12, 13, 1, 12),
                    new BoundVariableExpression(new TextSpan(12, 13, 1, 12), second)),
            ],
            new Scope());
        var module = new BoundModule(
            functionSymbol.DeclarationSpan,
            "Module",
            [function],
            new Dictionary<SymbolId, Symbol>
            {
                [functionSymbol.Id] = functionSymbol,
                [first.Id] = first,
                [second.Id] = second,
            });

        Assert.Equal(
            [first.DeclarationSpan, new TextSpan(10, 11, 1, 10)],
            SymbolFinder.FindBoundReferences(module, firstId, includeDeclaration: true));
        Assert.Equal(
            [second.DeclarationSpan, new TextSpan(12, 13, 1, 12)],
            SymbolFinder.FindBoundReferences(module, secondId, includeDeclaration: true));
    }

    [Fact]
    public void CallReceiverAndCallee_ResolveAndReferenceExactIdentifierSpans()
    {
        const string source = """
            module TestModule TestModule block
              class Worker
                func Pick (int:value) → int
                  !
                  return value

                func Ping () → void
                  ! cw
                  Console.WriteLine("ping")

              func Use (Worker:worker) → int
                ! cw
                worker.Ping()
                return worker.Pick(1)
            """;
        var workspace = new Calor.LanguageServer.State.WorkspaceState();
        var state = workspace.GetOrCreate(
            OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From("file:///calls.calr"),
            source);
        var receiverOffset = source.IndexOf("worker.Pick", StringComparison.Ordinal);
        var calleeOffset = receiverOffset + "worker.".Length;
        var (receiverLine, receiverColumn) =
            LspTestHarness.GetLineColumn(source, receiverOffset);
        var (calleeLine, calleeColumn) =
            LspTestHarness.GetLineColumn(source, calleeOffset);

        var receiver = SymbolFinder.FindSymbolAtPosition(
            state.Ast!,
            receiverLine,
            receiverColumn,
            source,
            state.BoundModule);
        var callee = SymbolFinder.FindSymbolAtPosition(
            state.Ast!,
            calleeLine,
            calleeColumn,
            source,
            state.BoundModule);
        var parameter = state.BoundModule!.SymbolsById.Values
            .OfType<VariableSymbol>()
            .Single(symbol => symbol.Name == "worker");
        var method = state.BoundModule.Functions
            .Single(function => function.Symbol.Name == "Worker.Pick")
            .Symbol;
        var pingOffset = source.IndexOf("worker.Ping", StringComparison.Ordinal)
            + "worker.".Length;
        var (pingLine, pingColumn) =
            LspTestHarness.GetLineColumn(source, pingOffset);
        var ping = SymbolFinder.FindSymbolAtPosition(
            state.Ast!,
            pingLine,
            pingColumn,
            source,
            state.BoundModule);
        var pingMethod = state.BoundModule.Functions
            .Single(function => function.Symbol.Name == "Worker.Ping")
            .Symbol;

        Assert.Equal("worker", receiver?.Name);
        Assert.Equal(parameter.Id, receiver?.SymbolId);
        Assert.Equal("worker", source.Substring(
            receiver!.Span.Start,
            receiver.Span.Length));
        Assert.Equal("Pick", callee?.Name);
        Assert.Equal(method.Id, callee?.SymbolId);
        Assert.Equal("Pick", source.Substring(callee!.Span.Start, callee.Span.Length));
        Assert.Equal("Ping", ping?.Name);
        Assert.Equal(pingMethod.Id, ping?.SymbolId);
        Assert.Equal("Ping", source.Substring(ping!.Span.Start, ping.Span.Length));

        Assert.All(
            SymbolFinder.FindBoundReferences(
                state.BoundModule,
                parameter.Id,
                includeDeclaration: true),
            span => Assert.Equal("worker", source.Substring(span.Start, span.Length)));
        Assert.All(
            workspace.FindProjectFunctionReferences(method, includeDeclaration: true),
            reference => Assert.Equal(
                "Pick",
                reference.Snapshot.Source.Substring(
                    reference.Span.Start,
                    reference.Span.Length)));
        Assert.All(
            workspace.FindProjectFunctionReferences(pingMethod, includeDeclaration: true),
            reference => Assert.Equal(
                "Ping",
                reference.Snapshot.Source.Substring(
                    reference.Span.Start,
                    reference.Span.Length)));
    }

    private static string TextAt(string source, TextSpan span) =>
        source.Substring(span.Start, span.Length);

    [Fact]
    public void FindDefinition_Function_ReturnsFunction()
    {
        var source = """
            module TestModule TestModule block
              func Add private ()
                return 0
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Add");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.FunctionNode>(def);
    }

    [Fact]
    public void FindDefinition_Class_ReturnsClass()
    {
        var source = """
            module TestModule TestModule block
              class Person internal
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Person");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.ClassDefinitionNode>(def);
    }

    [Fact]
    public void FindDefinition_Interface_ReturnsInterface()
    {
        var source = """
            module TestModule TestModule block
              interface IShape
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "IShape");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.InterfaceDefinitionNode>(def);
    }

    [Fact]
    public void FindDefinition_Nonexistent_ReturnsNull()
    {
        var source = """
            module TestModule TestModule block
              func Test private ()
                return 0
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "NonExistent");

        Assert.Null(def);
    }

    [Fact]
    public void FindFunction_ExistingFunction_ReturnsFunction()
    {
        var source = """
            module TestModule TestModule block
              func Calculate private (int:x) → int
                return x
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "Calculate");

        Assert.NotNull(func);
        Assert.Equal("Calculate", func.Name);
        Assert.Single(func.Parameters);
    }

    [Fact]
    public void FindFunction_NonExistent_ReturnsNull()
    {
        var source = """
            module TestModule TestModule block
              func Test private ()
                return 0
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "Missing");

        Assert.Null(func);
    }

    [Fact]
    public void FindMethod_ExistingMethod_ReturnsMethod()
    {
        var source = """
            module TestModule TestModule block
              class Calculator internal
                func Add private (int:a, int:b) → int
                  return (a + b)
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var cls = ast.Classes.FirstOrDefault(c => c.Name == "Calculator");
        Assert.NotNull(cls);

        var method = SymbolFinder.FindMethod(cls, "Add");

        Assert.NotNull(method);
        Assert.Equal("Add", method.Name);
        Assert.Equal(2, method.Parameters.Count);
    }

    [Fact]
    public void FindMethod_NonExistent_ReturnsNull()
    {
        var source = """
            module TestModule TestModule block
              class Calculator internal
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var cls = ast.Classes.FirstOrDefault(c => c.Name == "Calculator");
        Assert.NotNull(cls);

        var method = SymbolFinder.FindMethod(cls, "Missing");

        Assert.Null(method);
    }

    [Fact]
    public void FindSymbol_AtTypeName_ReturnsType()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §F{Test}
                §I{/*cursor*/i32:x}
                §O{i32}
                §R x
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("i32", result.Name);
        Assert.Equal("type", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtVariableReference_ReturnsReference()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §F{Test}
                §I{i32:n}
                §O{i32}
                §R /*cursor*/n
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("n", result.Name);
        // It's either parameter or variable reference
        Assert.True(result.Kind.Contains("parameter") || result.Kind.Contains("reference"));
    }

    [Fact]
    public void FindSymbol_AtIntegerLiteral_ReturnsLiteral()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §F{Test}
                §O{i32}
                §R /*cursor*/42
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("42", result.Name);
        Assert.Equal("integer literal", result.Kind);
    }

    [Fact]
    public void FindSymbol_OutsideModule_ReturnsNull()
    {
        var source = """
            module TestModule TestModule block
              func Test private ()
                return 0
            """;

        // Position outside the module - far past the end
        var result = LspTestHarness.FindSymbol(source, 100, 1);

        Assert.Null(result);
    }

    [Fact]
    public void Ast_ContainsMultipleFunctions()
    {
        var source = """
            module TestModule TestModule block
              func First private ()
                return 1

              func Second private ()
                return 2
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Functions.Count);
        Assert.Contains(ast.Functions, f => f.Name == "First");
        Assert.Contains(ast.Functions, f => f.Name == "Second");
    }

    [Fact]
    public void Ast_ContainsMultipleClasses()
    {
        var source = """
            module TestModule TestModule block
              class Person internal
                field string:name private

              class Employee internal
                field int:id private
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Classes.Count);
        Assert.Contains(ast.Classes, c => c.Name == "Person");
        Assert.Contains(ast.Classes, c => c.Name == "Employee");
    }
}
