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
            §M{m001:TestModule}
            §F{f001:Add}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §R a + b
            §/F{f001}
            §/M{m001}
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
            §M{m001:TestModule}
            §F{f001:GetValue}
            §O{i32}
            §R 42
            §/F{f001}
            §/M{m001}
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
            §M{m001:TestModule}
            §F{f001:GetString}
            §O{str}
            §R "hello"
            §/F{f001}
            §/M{m001}
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
            §M{m001:TestModule}
            §F{f001:DoSomething}
            §P "done"
            §/F{f001}
            §/M{m001}
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
            §M{m001:TestModule}
            §CL{c001:Calculator}
            §MT{m001:Add}
            §I{i32:x}
            §I{i32:y}
            §O{i32}
            §R x + y
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
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
            §M{m001:TestModule}
            §CL{c001:Base:pub}
            §MT{m001:Process:pub:virt}
            §O{void}
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
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
            §M{m001:TestModule}
            §CL{c001:Base:pub}
            §MT{m001:Process:pub:virt}
            §O{void}
            §/MT{m001}
            §/CL{c001}
            §CL{c002:Derived:pub}
            §EXT{Base}
            §MT{m002:Process:pub:over}
            §O{void}
            §/MT{m002}
            §/CL{c002}
            §/M{m001}
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
            §M{m001:TestModule}
            §F{f001:Divide}
            §I{i32:a}
            §I{i32:b}
            §O{i32}
            §Q b != 0
            §R a / b
            §/F{f001}
            §/M{m001}
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
            §M{m001:TestModule}
            §F{f001:Abs}
            §I{i32:x}
            §O{i32}
            §S result >= 0
            §IF{if001} x >= 0
            §R x
            §EL
            §R -x
            §/I{if001}
            §/F{f001}
            §/M{m001}
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
            §M{m001:TestModule}
            §F{f001:Process}
            §I{i32:count}
            §I{str:name}
            §I{bool:enabled}
            §I{f64:ratio}
            §O{void}
            §/F{f001}
            §/M{m001}
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
            §M{m001:TestModule}
            §CL{c001:Utils:pub}
            §MT{m001:Helper:pub:static}
            §O{i32}
            §R 0
            §/MT{m001}
            §/CL{c001}
            §/M{m001}
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
            §M{m001:TestModule}
            §CL{c001:Service}
            §AMT{m001:FetchData}
            §O{str}
            §R "data"
            §/AMT{m001}
            §/CL{c001}
            §/M{m001}
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
            §M{m001:TestModule}
            §AF{f001:LoadData}
            §O{str}
            §R "loaded"
            §/AF{f001}
            §/M{m001}
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "LoadData");

        Assert.NotNull(func);
        Assert.True(func.IsAsync);
    }
}
