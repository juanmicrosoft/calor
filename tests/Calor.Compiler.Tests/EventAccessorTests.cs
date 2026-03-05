using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class EventAccessorTests
{
    #region Lexer Tests

    [Fact]
    public void Lexer_EventAdd_TokenizesCorrectly()
    {
        var source = "§EADD";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Contains(tokens, t => t.Kind == TokenKind.EventAdd);
    }

    [Fact]
    public void Lexer_EndEventAdd_TokenizesCorrectly()
    {
        var source = "§/EADD";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Contains(tokens, t => t.Kind == TokenKind.EndEventAdd);
    }

    [Fact]
    public void Lexer_EventRemove_TokenizesCorrectly()
    {
        var source = "§EREM";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Contains(tokens, t => t.Kind == TokenKind.EventRemove);
    }

    [Fact]
    public void Lexer_EndEventRemove_TokenizesCorrectly()
    {
        var source = "§/EREM";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Contains(tokens, t => t.Kind == TokenKind.EndEventRemove);
    }

    [Fact]
    public void Lexer_EndEvent_TokenizesCorrectly()
    {
        var source = "§/EVT[e001]";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Contains(tokens, t => t.Kind == TokenKind.EndEvent);
    }

    #endregion

    #region Parser Tests

    [Fact]
    public void Parser_SimpleEventField_StillWorks()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §EVT{e001:Click:pub:EventHandler}
            §/CL{c1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        var cls = Assert.Single(module.Classes);
        var evt = Assert.Single(cls.Events);
        Assert.Equal("e001", evt.Id);
        Assert.Equal("Click", evt.Name);
        Assert.Equal(Visibility.Public, evt.Visibility);
        Assert.Equal("EventHandler", evt.DelegateType);
        Assert.False(evt.HasAccessors);
        Assert.Null(evt.AddBody);
        Assert.Null(evt.RemoveBody);
    }

    [Fact]
    public void Parser_EventWithAccessors_ParsesCorrectly()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §EVT{e001:Click:pub:EventHandler}
            §EADD
            §R INT:0
            §/EADD
            §EREM
            §R INT:1
            §/EREM
            §/EVT{e001}
            §/CL{c1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        var cls = Assert.Single(module.Classes);
        var evt = Assert.Single(cls.Events);
        Assert.Equal("e001", evt.Id);
        Assert.Equal("Click", evt.Name);
        Assert.Equal(Visibility.Public, evt.Visibility);
        Assert.Equal("EventHandler", evt.DelegateType);
        Assert.True(evt.HasAccessors);
        Assert.NotNull(evt.AddBody);
        Assert.NotNull(evt.RemoveBody);
        Assert.Single(evt.AddBody!);
        Assert.Single(evt.RemoveBody!);
    }

    [Fact]
    public void Parser_EventWithAddOnly_ParsesCorrectly()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §EVT{e001:Click:pub:EventHandler}
            §EADD
            §R INT:0
            §/EADD
            §/EVT{e001}
            §/CL{c1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        var evt = Assert.Single(module.Classes[0].Events);
        Assert.True(evt.HasAccessors);
        Assert.NotNull(evt.AddBody);
        Assert.Null(evt.RemoveBody);
    }

    #endregion

    #region C# Emission Tests

    [Fact]
    public void CSharpEmitter_SimpleEventField_EmitsFieldStyle()
    {
        var evt = new EventDefinitionNode(
            TextSpan.Empty, "e001", "Click", Visibility.Public, "EventHandler",
            new AttributeCollection());

        var classNode = new ClassDefinitionNode(
            TextSpan.Empty, "c1", "MyClass",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields: Array.Empty<ClassFieldNode>(),
            properties: Array.Empty<PropertyNode>(),
            constructors: Array.Empty<ConstructorNode>(),
            methods: Array.Empty<MethodNode>(),
            events: new[] { evt },
            operatorOverloads: Array.Empty<OperatorOverloadNode>(),
            attributes: new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Public,
            indexers: Array.Empty<IndexerNode>());

        var moduleNode = new ModuleNode(
            TextSpan.Empty, "m1", "Test",
            usings: Array.Empty<UsingDirectiveNode>(),
            interfaces: Array.Empty<InterfaceDefinitionNode>(),
            classes: new[] { classNode },
            enums: Array.Empty<EnumDefinitionNode>(),
            enumExtensions: Array.Empty<EnumExtensionNode>(),
            delegates: Array.Empty<DelegateDefinitionNode>(),
            functions: Array.Empty<FunctionNode>(),
            attributes: new AttributeCollection(),
            issues: Array.Empty<IssueNode>(),
            assumptions: Array.Empty<AssumeNode>(),
            invariants: Array.Empty<InvariantNode>(),
            decisions: Array.Empty<DecisionNode>(),
            context: null);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(moduleNode);

        Assert.Contains("public event EventHandler Click;", output);
    }

    [Fact]
    public void CSharpEmitter_EventWithAccessors_EmitsAccessorBodies()
    {
        var addBody = new List<StatementNode>
        {
            new ReturnStatementNode(TextSpan.Empty, new IntLiteralNode(TextSpan.Empty, 0))
        };
        var removeBody = new List<StatementNode>
        {
            new ReturnStatementNode(TextSpan.Empty, new IntLiteralNode(TextSpan.Empty, 1))
        };

        var evt = new EventDefinitionNode(
            TextSpan.Empty, "e001", "Click", Visibility.Public, "EventHandler",
            new AttributeCollection(), addBody, removeBody);

        var classNode = new ClassDefinitionNode(
            TextSpan.Empty, "c1", "MyClass",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields: Array.Empty<ClassFieldNode>(),
            properties: Array.Empty<PropertyNode>(),
            constructors: Array.Empty<ConstructorNode>(),
            methods: Array.Empty<MethodNode>(),
            events: new[] { evt },
            operatorOverloads: Array.Empty<OperatorOverloadNode>(),
            attributes: new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Public,
            indexers: Array.Empty<IndexerNode>());

        var moduleNode = new ModuleNode(
            TextSpan.Empty, "m1", "Test",
            usings: Array.Empty<UsingDirectiveNode>(),
            interfaces: Array.Empty<InterfaceDefinitionNode>(),
            classes: new[] { classNode },
            enums: Array.Empty<EnumDefinitionNode>(),
            enumExtensions: Array.Empty<EnumExtensionNode>(),
            delegates: Array.Empty<DelegateDefinitionNode>(),
            functions: Array.Empty<FunctionNode>(),
            attributes: new AttributeCollection(),
            issues: Array.Empty<IssueNode>(),
            assumptions: Array.Empty<AssumeNode>(),
            invariants: Array.Empty<InvariantNode>(),
            decisions: Array.Empty<DecisionNode>(),
            context: null);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(moduleNode);

        Assert.Contains("public event EventHandler Click", output);
        Assert.Contains("add", output);
        Assert.Contains("remove", output);
        Assert.DoesNotContain("public event EventHandler Click;", output);
    }

    #endregion

    #region C# to Calor Conversion Tests

    [Fact]
    public void Conversion_SimpleEventField_ProducesEventNode()
    {
        var csharp = """
            public class MyClass
            {
                public event EventHandler Click;
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var cls = Assert.Single(result.Ast!.Classes);
        var evt = Assert.Single(cls.Events);
        Assert.Equal("Click", evt.Name);
        Assert.Equal(Visibility.Public, evt.Visibility);
        Assert.False(evt.HasAccessors);
    }

    [Fact]
    public void Conversion_EventWithAccessors_ProducesEventNodeWithBodies()
    {
        var csharp = """
            using System;

            public class MyClass
            {
                private EventHandler _click;
                public event EventHandler Click
                {
                    add { _click += value; }
                    remove { _click -= value; }
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var cls = Assert.Single(result.Ast!.Classes);
        var evt = cls.Events.First(e => e.Name == "Click");
        Assert.Equal(Visibility.Public, evt.Visibility);
        Assert.True(evt.HasAccessors);
        Assert.NotNull(evt.AddBody);
        Assert.NotNull(evt.RemoveBody);
    }

    [Fact]
    public void Conversion_EventWithAccessors_EmitsCalorWithAccessors()
    {
        var csharp = """
            using System;

            public class MyClass
            {
                private EventHandler _click;
                public event EventHandler Click
                {
                    add { _click += value; }
                    remove { _click -= value; }
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));

        var calor = result.CalorSource;
        Assert.Contains("§EADD", calor);
        Assert.Contains("§/EADD", calor);
        Assert.Contains("§EREM", calor);
        Assert.Contains("§/EREM", calor);
        Assert.Contains("§/EVT", calor);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_SimpleEventField_ParseAndEmit()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §EVT{e001:Click:pub:EventHandler}
            §/CL{c1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(module);

        Assert.Contains("public event EventHandler Click;", output);
    }

    [Fact]
    public void RoundTrip_EventWithAccessors_ParseAndEmit()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §EVT{e001:Click:pub:EventHandler}
            §EADD
            §R INT:0
            §/EADD
            §EREM
            §R INT:1
            §/EREM
            §/EVT{e001}
            §/CL{c1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(module);

        Assert.Contains("public event EventHandler Click", output);
        Assert.Contains("add", output);
        Assert.Contains("remove", output);
        Assert.DoesNotContain("public event EventHandler Click;", output);
    }

    #endregion
}
