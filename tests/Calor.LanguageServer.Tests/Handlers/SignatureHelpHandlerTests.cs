using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class SignatureHelpHandlerTests
{
    [Theory]
    [InlineData("Task", false)]
    [InlineData("ValueTask", false)]
    [InlineData("Task", true)]
    [InlineData("ValueTask", true)]
    public void AsyncClassCarrierTypeParameter_FallbackSignatureReportsWrappedResult(string parameter, bool nested)
    {
        var source = $$"""
            §M{AsyncClassSignature}
              §CL{Box:pub}<{{parameter}}>
                §AMT{Produce:pub} ({{parameter}}:value) -> {{parameter}}
                  §R value
            """;
        if (nested)
            source = source.Replace("    §AMT", "    §CL{Inner:pub}\n      §AMT", StringComparison.Ordinal)
                .Replace("      §R", "        §R", StringComparison.Ordinal);
        var compilation = Compiler.Program.Compile(source, "async-class-signature.calr",
            new Compiler.CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.NotNull(compilation.Ast);
        var cls = Assert.Single(compilation.Ast.Classes);
        var method = Assert.Single(nested ? Assert.Single(cls.NestedClasses).Methods : cls.Methods);
        var buildLabel = typeof(LanguageServer.Handlers.SignatureHelpHandler)
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Single(candidate => candidate.Name == "BuildSignatureLabel"
                && candidate.GetParameters()[0].ParameterType == typeof(Compiler.Ast.MethodNode));
        var label = Assert.IsType<string>(buildLabel.Invoke(null, [method, compilation.Ast]));
        Assert.Equal($"Produce(value: {parameter}) -> global::System.Threading.Tasks.Task<{parameter}>", label);
        var completionType = typeof(LanguageServer.Handlers.CompletionHandler)
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Single(candidate => candidate.Name == "CallableReturnType"
                && candidate.GetParameters()[0].ParameterType == typeof(Compiler.Ast.MethodNode));
        Assert.Equal($"global::System.Threading.Tasks.Task<{parameter}>",
            Assert.IsType<string>(completionType.Invoke(null, [method, compilation.Ast, cls])));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics.Errors));
    }

    [Fact]
    public void FindFunction_WithParameters_ReturnsFunction()
    {
        var source = """
            module TestModule TestModule block
              func Add private (int:a, int:b) → int
                return (a + b)
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
            module TestModule TestModule block
              func GetValue private () → int
                return 42
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
            module TestModule TestModule block
              func GetString private () → string
                return "hello"
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
            module TestModule TestModule block
              func DoSomething private ()
                Console.WriteLine("done")
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
            module TestModule TestModule block
              class Calculator internal
                func Add private (int:x, int:y) → int
                  return (x + y)
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
            module TestModule TestModule block
              class Base
                func Process virtual () → void
                  !
                  return
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
            module TestModule TestModule block
              class Base
                func Process virtual () → void
                  !
                  return

              class Derived
                extends Base

                func Process override () → void
                  !
                  return
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
            module TestModule TestModule block
              func Divide private (int:a, int:b) → int
                requires (b != 0)
                return (a / b)
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
            module TestModule TestModule block
              func Abs private (int:x) → int
                => (result >= 0)
                if (x >= 0)
                  return x
                else
                  return (-x)
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
            module TestModule TestModule block
              func Process private (int:count, string:name, bool:enabled, double:ratio) → void
                return
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
            module TestModule TestModule block
              class Utils
                func Helper static () → int
                  !
                  return 0
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
            module TestModule TestModule block
              class Service internal
                func FetchData private async () → string
                  return "data"
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
            module TestModule TestModule block
              func LoadData private async () → string
                return "loaded"
            """;

        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);

        var func = SymbolFinder.FindFunction(ast, "LoadData");

        Assert.NotNull(func);
        Assert.True(func.IsAsync);
    }
}
