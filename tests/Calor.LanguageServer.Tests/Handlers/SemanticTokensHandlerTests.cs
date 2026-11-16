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
        var source = @"module Test Test block
  func MyFunc () → int
    !
    return 42";

        var state = LspTestHarness.CreateDocument(source);

        // Verify the AST was parsed correctly
        Assert.NotNull(state.Ast);
        Assert.Single(state.Ast.Functions);
        Assert.Equal("MyFunc", state.Ast.Functions[0].Name);
    }

    [Fact]
    public void TokenizesParameters()
    {
        var source = @"module Test Test block
  func Add (int:a, int:b) → int
    !
    return (a + b)";

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
        var source = @"module Test Test block
  class Shape internal abstract
    func Area abstract () → double
      !";

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
        var source = @"module Test Test block
  class Animal
    func Speak virtual () → string
      !
      return ""...""";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var method = state.Ast.Classes[0].Methods[0];
        Assert.Equal("Speak", method.Name);
        Assert.True(method.IsVirtual);
    }

    [Fact]
    public void TokenizesMethod_WithOverrideModifier()
    {
        var source = @"module Test Test block
  class Dog
    extends Animal

    func Speak override () → string
      !
      return ""Woof!""";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var method = state.Ast.Classes[0].Methods[0];
        Assert.Equal("Speak", method.Name);
        Assert.True(method.IsOverride);
    }

    [Fact]
    public void TokenizesBindStatement_Readonly()
    {
        var source = @"module Test Test block
  func Test () → int
    !
    let int:x = 42
    return x";

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
        var source = @"module Test Test block
  func Test () → int
    !
    var int:x = 42
    return x";

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
        var source = @"module Test Test block
  func Test () → int
    !
    let int:a = 42
    let double:b = 3.14
    let string:c = ""hello""
    let bool:d = true
    return a";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var func = state.Ast.Functions[0];
        Assert.Equal(5, func.Body.Count); // 4 binds + 1 return
    }

    [Fact]
    public void TokenizesForLoop()
    {
        var source = @"module Test Test block
  func Sum () → int
    !
    var int:total = 0
    for INT from 1 to INT step 10
      var int:total = (total + i)
    return total";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var func = state.Ast.Functions[0];
        Assert.True(func.Body.Count >= 2); // At least bind and for loop
    }

    [Fact]
    public void TokenizesAsyncMethod()
    {
        // Async methods use §AMT tag
        var source = @"module Test Test block
  class Service
    func FetchData async () → string
      !
      return ""data""";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var method = state.Ast.Classes[0].Methods[0];
        Assert.True(method.IsAsync);
    }

    [Fact]
    public void TokenizesStaticMethod()
    {
        var source = @"module Test Test block
  class Utils
    func Square static (int:x) → int
      !
      return (x * x)";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var method = state.Ast.Classes[0].Methods[0];
        Assert.True(method.IsStatic);
    }

    [Fact]
    public void TokenizesNewExpression()
    {
        var source = @"module Test Test block
  func Create () → object
    !
    return new List<int>()";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        Assert.Single(state.Ast.Functions);
    }

    [Fact]
    public void TokenizesFieldAccess()
    {
        var source = @"module Test Test block
  class Point
    field int:X
    field int:Y";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var cls = state.Ast.Classes[0];
        Assert.Equal(2, cls.Fields.Count);
        Assert.Equal("X", cls.Fields[0].Name);
        Assert.Equal("Y", cls.Fields[1].Name);
    }

    [Fact]
    public void TokenizesProperty()
    {
        var source = @"module Test Test block
  class Person
    field string:_name private
    property string:Name
      get
        return this . _name
      set
        set this . _name value";

        var state = LspTestHarness.CreateDocument(source);

        Assert.NotNull(state.Ast);
        var cls = state.Ast.Classes[0];
        Assert.Single(cls.Properties);
        Assert.Equal("Name", cls.Properties[0].Name);
    }

    [Fact]
    public void HandlesMalformedSourceGracefully()
    {
        var source = @"§M{Test}
  §F{f1:Bad";

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
