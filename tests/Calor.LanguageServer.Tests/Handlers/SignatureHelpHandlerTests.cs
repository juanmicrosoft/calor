using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class SignatureHelpHandlerTests
{
    [Fact]
    public void FindFunction_WithParameters_ReturnsFunction()
    {
        var source = """
            §M{TestModule}
              §F{Add}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §R a + b
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "Add");

        Assert.NotNull(func);
        Assert.Equal("Add", func.Name);
        Assert.Equal(2, func.Parameters.Count);
        Assert.Equal("a", func.Parameters[0].Name);
        Assert.Equal("INT", func.Parameters[0].TypeName);
        Assert.Equal("b", func.Parameters[1].Name);
        Assert.Equal("INT", func.Parameters[1].TypeName);
    }

    [Fact]
    public void FindFunction_NoParameters_ReturnsFunction()
    {
        var source = """
            §M{TestModule}
              §F{GetValue}
                §O{i32}
                §R 42
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "GetValue");

        Assert.NotNull(func);
        Assert.Equal("GetValue", func.Name);
        Assert.Empty(func.Parameters);
    }

    [Fact]
    public void FindFunction_WithOutput_HasOutputType()
    {
        var source = """
            §M{TestModule}
              §F{GetString}
                §O{str}
                §R "hello"
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "GetString");

        Assert.NotNull(func);
        Assert.NotNull(func.Output);
        Assert.Equal("STRING", func.Output.TypeName);
    }

    [Fact]
    public void FindFunction_VoidOutput_HasNullOutput()
    {
        var source = """
            §M{TestModule}
              §F{DoSomething}
                §P "done"
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "DoSomething");

        Assert.NotNull(func);
        Assert.Null(func.Output);
    }

    [Fact]
    public void FindMethod_InClass_ReturnsMethod()
    {
        var source = """
            §M{TestModule}
              §CL{Calculator}
                §MT{Add}
                  §I{i32:x}
                  §I{i32:y}
                  §O{i32}
                  §R x + y
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
    public void FindMethod_VirtualMethod_HasVirtualFlag()
    {
        var source = """
            §M{TestModule}
              §CL{Base:pub}
                §MT{Process:pub:virt}
                  §O{void}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var cls = ast.Classes.FirstOrDefault(c => c.Name == "Base");
        Assert.NotNull(cls);

        var method = SymbolFinder.FindMethod(cls, "Process");

        Assert.NotNull(method);
        Assert.True(method.IsVirtual);
    }

    [Fact]
    public void FindMethod_OverrideMethod_HasOverrideFlag()
    {
        var source = """
            §M{TestModule}
              §CL{Base:pub}
                §MT{Process:pub:virt}
                  §O{void}
              §CL{Derived:pub}
                §EXT{Base}
                §MT{Process:pub:over}
                  §O{void}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var cls = ast.Classes.FirstOrDefault(c => c.Name == "Derived");
        Assert.NotNull(cls);

        var method = SymbolFinder.FindMethod(cls, "Process");

        Assert.NotNull(method);
        Assert.True(method.IsOverride);
    }

    [Fact]
    public void Function_WithPreconditions_HasPreconditions()
    {
        var source = """
            §M{TestModule}
              §F{Divide}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §Q b != 0
                §R a / b
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "Divide");

        Assert.NotNull(func);
        Assert.NotEmpty(func.Preconditions);
    }

    [Fact]
    public void Function_WithPostconditions_HasPostconditions()
    {
        var source = """
            §M{TestModule}
              §F{Abs}
                §I{i32:x}
                §O{i32}
                §S result >= 0
                §IF x >= 0
                  §R x
                §EL
                  §R -x
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "Abs");

        Assert.NotNull(func);
        Assert.NotEmpty(func.Postconditions);
    }

    [Fact]
    public void Function_MixedParameterTypes_AllTypesPreserved()
    {
        var source = """
            §M{TestModule}
              §F{Process}
                §I{i32:count}
                §I{str:name}
                §I{bool:enabled}
                §I{f64:ratio}
                §O{void}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "Process");

        Assert.NotNull(func);
        Assert.Equal(4, func.Parameters.Count);
        Assert.Equal("INT", func.Parameters[0].TypeName);
        Assert.Equal("STRING", func.Parameters[1].TypeName);
        Assert.Equal("BOOL", func.Parameters[2].TypeName);
        Assert.Equal("FLOAT", func.Parameters[3].TypeName);
    }

    [Fact]
    public void Method_StaticMethod_HasStaticFlag()
    {
        var source = """
            §M{TestModule}
              §CL{Utils:pub}
                §MT{Helper:pub:static}
                  §O{i32}
                  §R 0
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var cls = ast.Classes.FirstOrDefault(c => c.Name == "Utils");
        Assert.NotNull(cls);

        var method = SymbolFinder.FindMethod(cls, "Helper");

        Assert.NotNull(method);
        Assert.True(method.IsStatic);
    }

    [Fact]
    public void Method_AsyncMethod_HasAsyncFlag()
    {
        var source = """
            §M{TestModule}
              §CL{Service}
                §AMT{FetchData}
                §O{str}
                §R "data"
                §/AMT
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var cls = ast.Classes.FirstOrDefault(c => c.Name == "Service");
        Assert.NotNull(cls);

        var method = SymbolFinder.FindMethod(cls, "FetchData");

        Assert.NotNull(method);
        Assert.True(method.IsAsync);
    }

    [Fact]
    public void Function_AsyncFunction_HasAsyncFlag()
    {
        var source = """
            §M{TestModule}
              §AF{LoadData}
                §O{str}
                §R "loaded"
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "LoadData");

        Assert.NotNull(func);
        Assert.True(func.IsAsync);
    }
}
