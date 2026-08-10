using Calor.Compiler.Binding;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Utilities;

public class SymbolFinderTests
{
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
            §M{m001:TestModule}
              §CL{c1:Container:pub}
                §FLD{i32:field:priv}
                §MT{m1:Compute:pub} (i32:parameter) -> i32
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
    public void NestedLocalDeclarationSpans_AreIdentifierTokens()
    {
        var source = """
            §M{m001:TestModule}
              §F{f1:Scopes:pub} ([str]:items, IDisposable:input) -> bool
                §L{l1:index:0:1:1}
                  §P index
                §EACH{e1:item:str:position} items
                  §P item
                §USE{u1:resource:IDisposable} input
                  §P STR:"using"
                §TR{t1}
                  §P STR:"try"
                §CA{Exception:error}
                  §P STR:"catch"
                §/TR{t1}
                §B{predicate} §LAM{lam1:lambdaValue:i32} (> lambdaValue 0) §/LAM{lam1}
                §B{matched:i32} §W{match1} INT:1
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

    [Fact]
    public void FindDefinition_Function_ReturnsFunction()
    {
        var source = """
            §M{m001:TestModule}
              §F{f001:Add}
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
            §M{m001:TestModule}
              §CL{c001:Person}
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
            §M{m001:TestModule}
              §IFACE{i001:IShape}
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
            §M{m001:TestModule}
              §EN{e001:Color}
              §EM{Red}
              §/EN{e001}
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
            §M{m001:TestModule}
              §DEL{d001:Callback}
              §I{i32:value}
              §O{void}
              §/DEL{d001}
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
            §M{m001:TestModule}
              §F{f001:Test}
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
            §M{m001:TestModule}
              §F{f001:Calculate}
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
            §M{m001:TestModule}
              §F{f001:Test}
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
            §M{m001:TestModule}
              §CL{c001:Calculator}
                §MT{m001:Add}
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
            §M{m001:TestModule}
              §CL{c001:Calculator}
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
            §M{m001:/*cursor*/TestModule}
              §F{f001:Test}
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
            §M{m001:TestModule}
              §F{f001:/*cursor*/Add}
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
            §M{m001:TestModule}
              §F{f001:Add}
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
            §M{m001:TestModule}
              §F{f001:Test}
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
            §M{m001:TestModule}
              §F{f001:Test}
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
            §M{m001:TestModule}
              §F{f001:Test}
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
            §M{m001:TestModule}
              §CL{c001:/*cursor*/Person}
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
            §M{m001:TestModule}
              §CL{c001:Person}
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
            §M{m001:TestModule}
              §EN{e001:/*cursor*/Color}
              §EM{Red}
              §/EN{e001}
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
            §M{m001:TestModule}
              §EN{e001:Color}
              §EM{/*cursor*/Red}
              §/EN{e001}
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
            §M{m001:TestModule}
              §F{f001:Test}
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
            §M{m001:TestModule}
              §F{f001:Test}
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
            §M{m001:TestModule}
              §CL{c001:Calculator}
                §MT{m001:/*cursor*/Add}
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
            §M{m001:TestModule}
              §F{f001:First}
                §R 1
              §F{f002:Second}
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
            §M{m001:TestModule}
              §CL{c001:Person}
                §FLD{str:name}
              §CL{c002:Employee}
                §FLD{i32:id}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Classes.Count);
        Assert.Contains(ast.Classes, c => c.Name == "Person");
        Assert.Contains(ast.Classes, c => c.Name == "Employee");
    }
}
