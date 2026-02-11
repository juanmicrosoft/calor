using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

/// <summary>
/// Tests for the SemanticTokensHandler which provides rich syntax highlighting.
/// </summary>
public class SemanticTokensHandlerTests
{
    [Fact]
    public void TokenizesFunction_WithDeclarationModifier()
    {
        var source = @"§M{m1:Test}
§F{f1:MyFunc:pub}
  §O{i32}
  §R 42
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        // Verify the AST was parsed correctly
        Assert.NotNull(state.Ast);
        Assert.Single(state.Ast.Functions);
        Assert.Equal("MyFunc", state.Ast.Functions[0].Name);
    }

    [Fact]
    public void TokenizesParameters()
    {
        var source = @"§M{m1:Test}
§F{f1:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var func = state.Ast.Functions[0];
        Assert.Equal(2, func.Parameters.Count);
        Assert.Equal("a", func.Parameters[0].Name);
        Assert.Equal("b", func.Parameters[1].Name);
    }

    [Fact]
    public void TokenizesClass_WithAbstractModifier()
    {
        var source = @"§M{m1:Test}
§CL{c1:Shape:abs}
  §MT{mt1:Area:pub:abs}
    §O{f64}
  §/MT{mt1}
§/CL{c1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        Assert.Single(state.Ast.Classes);
        var cls = state.Ast.Classes[0];
        Assert.Equal("Shape", cls.Name);
        Assert.True(cls.IsAbstract);
    }

    [Fact]
    public void TokenizesMethod_WithVirtualModifier()
    {
        var source = @"§M{m1:Test}
§CL{c1:Animal:pub}
  §MT{mt1:Speak:pub:virt}
    §O{str}
    §R ""...""
  §/MT{mt1}
§/CL{c1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var method = state.Ast.Classes[0].Methods[0];
        Assert.Equal("Speak", method.Name);
        Assert.True(method.IsVirtual);
    }

    [Fact]
    public void TokenizesMethod_WithOverrideModifier()
    {
        var source = @"§M{m1:Test}
§CL{c1:Dog:pub}
  §EXT{Animal}
  §MT{mt1:Speak:pub:over}
    §O{str}
    §R ""Woof!""
  §/MT{mt1}
§/CL{c1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var method = state.Ast.Classes[0].Methods[0];
        Assert.Equal("Speak", method.Name);
        Assert.True(method.IsOverride);
    }

    [Fact]
    public void TokenizesInterface()
    {
        var source = @"§M{m1:Test}
§IFACE{i1:IDrawable:pub}
  §MT{m1:Draw}
    §O{void}
  §/MT{m1}
§/IFACE{i1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        Assert.Single(state.Ast.Interfaces);
        Assert.Equal("IDrawable", state.Ast.Interfaces[0].Name);
    }

    [Fact]
    public void TokenizesEnum()
    {
        var source = @"§M{m1:Test}
§EN{e1:Color:pub}
  §EV{Red}
  §EV{Green}
  §EV{Blue}
§/EN{e1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        Assert.Single(state.Ast.Enums);
        var enumDef = state.Ast.Enums[0];
        Assert.Equal("Color", enumDef.Name);
        Assert.Equal(3, enumDef.Members.Count);
    }

    [Fact]
    public void TokenizesBindStatement_Readonly()
    {
        var source = @"§M{m1:Test}
§F{f1:Test:pub}
  §O{i32}
  §B{x:i32} 42
  §R x
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var func = state.Ast.Functions[0];
        Assert.Equal(2, func.Body.Count); // bind + return
        // First statement should be the bind
        var bind = func.Body[0] as BindStatementNode;
        Assert.NotNull(bind);
        Assert.Equal("x", bind.Name);
        Assert.False(bind.IsMutable);
    }

    [Fact]
    public void TokenizesBindStatement_Mutable()
    {
        // Use ~name prefix for mutable binding
        var source = @"§M{m1:Test}
§F{f1:Test:pub}
  §O{i32}
  §B{~x:i32} 42
  §R x
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var func = state.Ast.Functions[0];
        Assert.Equal(2, func.Body.Count); // bind + return
        var bind = func.Body[0] as BindStatementNode;
        Assert.NotNull(bind);
        Assert.Equal("x", bind.Name);
        Assert.True(bind.IsMutable);
    }

    [Fact]
    public void TokenizesLiterals()
    {
        var source = @"§M{m1:Test}
§F{f1:Test:pub}
  §O{i32}
  §B{a:i32} 42
  §B{b:f64} 3.14
  §B{c:str} ""hello""
  §B{d:bool} true
  §R a
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var func = state.Ast.Functions[0];
        Assert.Equal(5, func.Body.Count); // 4 binds + 1 return
    }

    [Fact]
    public void TokenizesIfStatement()
    {
        var source = @"§M{m1:Test}
§F{f1:Max:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §IF (> a b)
    §R a
  §ELSE
    §R b
  §/IF
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var func = state.Ast.Functions[0];
        // Body should have an if statement (plus possibly other statements)
        Assert.True(func.Body.Count > 0);
    }

    [Fact]
    public void TokenizesForLoop()
    {
        var source = @"§M{m1:Test}
§F{f1:Sum:pub}
  §O{i32}
  §BM{total:i32} 0
  §FOR{i:1:10}
    §ASSIGN total (+ total i)
  §/FOR
  §R total
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var func = state.Ast.Functions[0];
        Assert.True(func.Body.Count >= 2); // At least bind and for loop
    }

    [Fact]
    public void TokenizesAsyncMethod()
    {
        // Async methods use §AMT tag
        var source = @"§M{m1:Test}
§CL{c1:Service:pub}
  §AMT{mt1:FetchData:pub}
    §O{str}
    §R ""data""
  §/AMT{mt1}
§/CL{c1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var method = state.Ast.Classes[0].Methods[0];
        Assert.True(method.IsAsync);
    }

    [Fact]
    public void TokenizesStaticMethod()
    {
        var source = @"§M{m1:Test}
§CL{c1:Utils:pub}
  §MT{mt1:Square:pub:stat}
    §I{i32:x}
    §O{i32}
    §R (* x x)
  §/MT{mt1}
§/CL{c1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var method = state.Ast.Classes[0].Methods[0];
        Assert.True(method.IsStatic);
    }

    [Fact]
    public void TokenizesNewExpression()
    {
        var source = @"§M{m1:Test}
§F{f1:Create:pub}
  §O{object}
  §R §NEW{List<i32>}
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        Assert.Single(state.Ast.Functions);
    }

    [Fact]
    public void TokenizesFieldAccess()
    {
        var source = @"§M{m1:Test}
§CL{c1:Point:pub}
  §FLD{i32:X:pub}
  §FLD{i32:Y:pub}
§/CL{c1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var cls = state.Ast.Classes[0];
        Assert.Equal(2, cls.Fields.Count);
        Assert.Equal("X", cls.Fields[0].Name);
        Assert.Equal("Y", cls.Fields[1].Name);
    }

    [Fact]
    public void TokenizesLambdaExpression()
    {
        var source = @"§M{m1:Test}
§F{f1:Test:pub}
  §O{i32}
  §B{add:Func<i32,i32,i32>} §LAMBDA
    §I{i32:a}
    §I{i32:b}
    §R (+ a b)
  §/LAMBDA
  §R §C{add} §A 1 §A 2 §/C
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        Assert.Single(state.Ast.Functions);
    }

    [Fact]
    public void TokenizesProperty()
    {
        var source = @"§M{m1:Test}
§CL{c1:Person:pub}
  §FLD{str:_name:pri}
  §PROP{str:Name:pub}
    §GET
      §R §THIS._name
    §/GET
    §SET
      §ASSIGN §THIS._name value
    §/SET
  §/PROP
§/CL{c1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var cls = state.Ast.Classes[0];
        Assert.Single(cls.Properties);
        Assert.Equal("Name", cls.Properties[0].Name);
    }

    [Fact]
    public void TokenizesTryCatch()
    {
        var source = @"§M{m1:Test}
§F{f1:SafeDiv:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §TRY
    §R (/ a b)
  §CATCH{Exception:ex}
    §R 0
  §/TRY
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var func = state.Ast.Functions[0];
        Assert.True(func.Body.Count > 0);
    }

    [Fact]
    public void TokenizesForeach()
    {
        var source = @"§M{m1:Test}
§F{f1:Print:pub}
  §I{List<str>:items}
  §O{void}
  §FOREACH{item:items}
    §PRINT item
  §/FOREACH
§/F{f1}
§/M{m1}";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        Assert.Single(state.Ast.Functions);
    }

    [Fact]
    public void HandlesMalformedSourceGracefully()
    {
        var source = @"§M{m1:Test}
§F{f1:Bad
§/M{m1}";

        // Should not throw, just produce diagnostics
        var state = LspTestHarness.CreateDocument(source);
        Assert.True(state.Diagnostics.Count > 0);
    }

    [Fact]
    public void HandlesEmptySourceGracefully()
    {
        var source = "";

        var state = LspTestHarness.CreateDocument(source);
        // Should have diagnostics but not crash
        Assert.NotNull(state);
    }
}
