using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Utilities;

public class SymbolFinderTests
{
    [Theory]
    [InlineData("§L{item:0:1:1}", "§L{item:2:3:1}")]
    [InlineData("§EACH{same:item:i32} items", "§EACH{same:item:i32} items")]
    [InlineData("§WH BOOL:false", "§WH BOOL:false")]
    public void Visitor_LoopScopesDoNotDependOnStatementIds(string firstHeader, string secondHeader)
    {
        var source = $$"""
            §M{Test}
              §F{Probe:pub} ([i32]:items) -> void
                {{firstHeader}}
                  §B{local:i32} 1
                  §P local
                {{secondHeader}}
                  §B{local:i32} 2
                  §P local
            """;
        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);
        var firstOffset = source.IndexOf("§P local", StringComparison.Ordinal) + "§P ".Length;
        var secondOffset = source.LastIndexOf("§P local", StringComparison.Ordinal) + "§P ".Length;
        var first = new SymbolFinderVisitor(firstOffset, source).FindSymbol(ast);
        var second = new SymbolFinderVisitor(secondOffset, source).FindSymbol(ast);

        Assert.Equal("local", first?.Name);
        Assert.Equal("local", second?.Name);
        Assert.NotEqual(first!.Span, second!.Span);
        Assert.NotSame(first.Node, second.Node);
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
    public void ParsedDeclarationIdentifierSpans_AreSafeRenameEdits()
    {
        var source = """
            §M{TestModule}
              §CL{Container:pub}
                §FLD{i32:field:priv}
                §MT{Compute:pub} (i32:parameter) -> i32
                  §B{local:i32} parameter
                  §R (+ local field)
            """;
        var state = LspTestHarness.CreateDocument(source);
        var symbols = state.BoundModule!.SymbolsById.Values.ToArray();

        foreach (var (name, symbol) in new[]
                 {
                     ("Container", symbols.Single(symbol => symbol.Name == "Container")),
                     ("field", symbols.Single(symbol => symbol.Name == "field")),
                     ("Compute", symbols.Single(symbol => symbol.Name == "Container.Compute")),
                     ("parameter", symbols.Single(symbol => symbol.Name == "parameter")),
                     ("local", symbols.Single(symbol => symbol.Name == "local")),
                 })
        {
            Assert.Equal(
                name,
                source.Substring(symbol.DeclarationSpan.Start, symbol.DeclarationSpan.Length));
        }
    }

    [Fact]
    public void CallReceiverAndCallee_ResolveAndReferenceExactIdentifierSpans()
    {
        const string source = """
            §M{TestModule}
              §CL{Worker:pub}
                §MT{Pick:pub} (i32:value) -> i32
                  §R value
                §MT{Ping:pub} () -> void
                  §P STR:"ping"
              §F{Use:pub} (Worker:worker) -> i32
                §C{worker.Ping}
                §R §C{worker.Pick} §A INT:1 §/C
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

    [Fact]
    public void TupleGenericNewTypes_IndexContainersAndSkipAnonymousTupleNodes()
    {
        const string source = """
            §M{TupleReferences}
              §CL{List:pub}<T>
                §CTOR{pub} ()
                  §P STR:"list"
              §CL{Dictionary:pub}<TKey,TValue>
                §CTOR{pub} ()
                  §P STR:"dictionary"
              §F{MakeList:pub} () -> object
                §R §NEW{List<(i32,i32)>} §/NEW
              §F{MakeDictionary:pub} () -> object
                §R §NEW{Dictionary<str,(i32,i32)>} §/NEW
            """;
        var state = LspTestHarness.CreateDocument(source);
        Assert.NotNull(state.Ast);
        Assert.True(
            state.BoundModule != null,
            string.Join(
                Environment.NewLine,
                state.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message}")));
        var typeSymbols = state.BoundModule.SymbolsById.Values
            .OfType<TypeSymbol>()
            .ToArray();
        var list = Assert.Single(
            typeSymbols,
            symbol => symbol.QualifiedName == "List`1");
        var dictionary = Assert.Single(
            typeSymbols,
            symbol => symbol.QualifiedName == "Dictionary`2");

        TypeReferenceIndexResult? index = null;
        var exception = Record.Exception(
            () => index = TypeReferenceIndex.BuildDetailed(
                state.Ast,
                state.BoundModule,
                source,
                typeSymbols));

        Assert.Null(exception);
        Assert.NotNull(index);
        Assert.Contains(
            index.References,
            reference => reference.SymbolId == list.Id
                && TextAt(source, reference.Span) == "List");
        Assert.Contains(
            index.References,
            reference => reference.SymbolId == dictionary.Id
                && TextAt(source, reference.Span) == "Dictionary");
        Assert.DoesNotContain(
            index.References,
            reference => string.IsNullOrWhiteSpace(reference.Name));

        foreach (var (name, expectedId) in new[]
                 {
                     ("List<(i32,i32)>", list.Id),
                     ("Dictionary<str,(i32,i32)>", dictionary.Id),
                 })
        {
            var offset = source.IndexOf(name, StringComparison.Ordinal);
            var (line, column) = LspTestHarness.GetLineColumn(source, offset);
            var symbol = SymbolFinder.FindSymbolAtPosition(
                state.Ast,
                line,
                column,
                source,
                state.BoundModule);

            Assert.NotNull(symbol);
            Assert.Equal(expectedId, symbol.SymbolId);
        }

        Assert.Throws<ArgumentException>(
            () => TypeIdentity.ToLookupName(" \t"));
    }

    [Fact]
    public void NestedLocalDeclarationSpans_AreIdentifierTokens()
    {
        var source = """
            §M{TestModule}
              §F{Scopes:pub} ([str]:items, IDisposable:input) -> bool
                §L{index:0:1:1}
                  §P index
                §EACH{item:str:position} items
                  §P item
                §USE{resource:IDisposable} input
                  §P STR:"using"
                §TR
                  §P STR:"try"
                §CA{Exception:error}
                  §P STR:"catch"
                §/TR
                §B{predicate} §LAM{lambdaValue:i32} (> lambdaValue 0) §/LAM
                §B{matched:i32} §W INT:1
                  §K §VAR{captured} → captured
                §R (forall ((quantified i32)) (>= quantified 0))
            """;
        var state = LspTestHarness.CreateDocument(source);
        var symbols = state.BoundModule!.SymbolsById.Values
            .OfType<VariableSymbol>()
            .ToArray();

        foreach (var name in new[]
                 {
                     "index",
                     "item",
                     "position",
                     "resource",
                     "error",
                     "lambdaValue",
                     "captured",
                     "quantified",
                 })
        {
            var symbol = symbols.Single(candidate => candidate.Name == name);
            Assert.Equal(
                name,
                source.Substring(symbol.DeclarationSpan.Start, symbol.DeclarationSpan.Length));
        }
    }

    private static string TextAt(string source, TextSpan span) =>
        source.Substring(span.Start, span.Length);

    [Fact]
    public void FindDefinition_Function_ReturnsFunction()
    {
        var source = """
            §M{TestModule}
              §F{Add}
                §R 0
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
            §M{TestModule}
              §CL{Person}
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
            §M{TestModule}
              §IFACE{IShape}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "IShape");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.InterfaceDefinitionNode>(def);
    }

    [Fact]
    public void FindDefinition_Enum_ReturnsEnum()
    {
        var source = """
            §M{TestModule}
              §EN{Color}
              §EM{Red}
              §/EN
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Color");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.EnumDefinitionNode>(def);
    }

    [Fact]
    public void FindDefinition_Delegate_ReturnsDelegate()
    {
        var source = """
            §M{TestModule}
              §DEL{Callback}
              §I{i32:value}
              §O{void}
              §/DEL
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var def = SymbolFinder.FindDefinition(ast, "Callback");

        Assert.NotNull(def);
        Assert.IsType<Calor.Compiler.Ast.DelegateDefinitionNode>(def);
    }

    [Fact]
    public void FindDefinition_Nonexistent_ReturnsNull()
    {
        var source = """
            §M{TestModule}
              §F{Test}
                §R 0
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
            §M{TestModule}
              §F{Calculate}
                §I{i32:x}
                §O{i32}
                §R x
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
            §M{TestModule}
              §F{Test}
                §R 0
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
            §M{TestModule}
              §CL{Calculator}
                §MT{Add}
                  §I{i32:a}
                  §I{i32:b}
                  §O{i32}
                  §R a + b
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
            §M{TestModule}
              §CL{Calculator}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var cls = ast.Classes.FirstOrDefault(c => c.Name == "Calculator");
        Assert.NotNull(cls);

        var method = SymbolFinder.FindMethod(cls, "Missing");

        Assert.Null(method);
    }

    // Position-based tests using the marker approach
    [Fact]
    public void FindSymbol_AtModuleName_ReturnsModule()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{/*cursor*/TestModule}
              §F{Test}
                §R 0
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("TestModule", result.Name);
        Assert.Equal("module", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtFunctionName_ReturnsFunction()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §F{/*cursor*/Add}
                §I{i32:a}
                §O{i32}
                §R a
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Add", result.Name);
        Assert.Equal("function", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtParameterName_ReturnsParameter()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §F{Add}
                §I{i32:/*cursor*/myParam}
                §O{i32}
                §R myParam
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("myParam", result.Name);
        Assert.Equal("parameter", result.Kind);
        Assert.Equal("INT", result.Type);
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
    public void FindSymbol_AtLocalVariable_ReturnsVariable()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §F{Test}
                §B{/*cursor*/myVar:i32} 42
                §R myVar
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("myVar", result.Name);
        Assert.Contains("variable", result.Kind);
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
    public void FindSymbol_AtClassName_ReturnsClass()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §CL{/*cursor*/Person}
                §FLD{str:name}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Person", result.Name);
        Assert.Equal("class", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtFieldName_ReturnsField()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §CL{Person}
                §FLD{str:/*cursor*/name}
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("name", result.Name);
        Assert.Equal("field", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtEnumName_ReturnsEnum()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §EN{/*cursor*/Color}
              §EM{Red}
              §/EN
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Color", result.Name);
        Assert.Equal("enum", result.Kind);
    }

    [Fact]
    public void FindSymbol_AtEnumMember_ReturnsEnumMember()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §EN{Color}
              §EM{/*cursor*/Red}
              §/EN
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Red", result.Name);
        Assert.Equal("enum member", result.Kind);
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
            §M{TestModule}
              §F{Test}
                §R 0
            """;

        // Position outside the module - far past the end
        var result = LspTestHarness.FindSymbol(source, 100, 1);

        Assert.Null(result);
    }

    [Fact]
    public void FindSymbol_AtMethodName_ReturnsMethod()
    {
        var (source, line, column) = LspTestHarness.FindMarker("""
            §M{TestModule}
              §CL{Calculator}
                §MT{/*cursor*/Add}
                  §I{i32:a}
                  §I{i32:b}
                  §O{i32}
                  §R a + b
            """);

        var result = LspTestHarness.FindSymbol(source, line, column);

        Assert.NotNull(result);
        Assert.Equal("Add", result.Name);
        Assert.Equal("method", result.Kind);
    }

    [Fact]
    public void Ast_ContainsMultipleFunctions()
    {
        var source = """
            §M{TestModule}
              §F{First}
                §R 1
              §F{Second}
                §R 2
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
            §M{TestModule}
              §CL{Person}
                §FLD{str:name}
              §CL{Employee}
                §FLD{i32:id}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Classes.Count);
        Assert.Contains(ast.Classes, c => c.Name == "Person");
        Assert.Contains(ast.Classes, c => c.Name == "Employee");
    }
}
