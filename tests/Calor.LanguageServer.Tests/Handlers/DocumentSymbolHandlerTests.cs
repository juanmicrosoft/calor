using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class DocumentSymbolHandlerTests
{
    [Fact]
    public void DocumentSymbols_NativeRecordHasRecordDetailAndNoSyntheticCopyDeclaration()
    {
        const string source = """
            module Records Records block
              record P (int:X, int:Y)
            """;
        var ast = LspTestHarness.GetAst(source);
        Assert.NotNull(ast);
        var method = typeof(Calor.LanguageServer.Handlers.DocumentSymbolHandler).GetMethod(
            "GetModuleChildren", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var symbols = Assert.IsAssignableFrom<IEnumerable<OmniSharp.Extensions.LanguageServer.Protocol.Models.DocumentSymbol>>(
            method.Invoke(null, [ast, source]));
        var record = Assert.Single(symbols);
        Assert.Equal("record", record.Detail);
        Assert.Equal(OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Class, record.Kind);
        Assert.Equal(2, record.Children!.Count(child =>
            child.Kind == OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Property));
        Assert.Single(record.Children!.Where(child =>
            child.Kind == OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Constructor));
    }

    [Fact]
    public void DocumentSymbols_FunctionsListed()
    {
        var source = """
            module TestModule TestModule block
              func Add private ()
                return 0

              func Subtract private ()
                return 0
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Functions.Count);
        Assert.Contains(ast.Functions, f => f.Name == "Add");
        Assert.Contains(ast.Functions, f => f.Name == "Subtract");
    }

    [Fact]
    public void DocumentSymbols_ClassesListed()
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

    [Fact]
    public void DocumentSymbols_ClassWithMembersListed()
    {
        var source = """
            module TestModule TestModule block
              class Calculator internal
                field int:value private
                func Add private (int:x) → int
                  return x

                func Subtract private (int:x) → int
                  return x
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Classes);
        var cls = ast.Classes[0];
        Assert.Equal("Calculator", cls.Name);
        Assert.Single(cls.Fields);
        Assert.Equal(2, cls.Methods.Count);
        Assert.Contains(cls.Methods, m => m.Name == "Add");
        Assert.Contains(cls.Methods, m => m.Name == "Subtract");
    }

    [Fact]
    public void DocumentSymbols_FunctionParameters_Counted()
    {
        var source = """
            module TestModule TestModule block
              func Calculate private (int:a, int:b, string:label) → int
                return (a + b)
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var func = ast.Functions[0];
        Assert.Equal("Calculate", func.Name);
        Assert.Equal(3, func.Parameters.Count);
    }

    [Fact]
    public void DocumentSymbols_ClassProperties_Listed()
    {
        var source = """
            module TestModule TestModule block
              class Person internal
                property string:Name
                  get
                    return ""
                property int:Age
                  get
                    return 0
                  set
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var cls = ast.Classes[0];
        Assert.Equal(2, cls.Properties.Count);
        Assert.Contains(cls.Properties, p => p.Name == "Name");
        Assert.Contains(cls.Properties, p => p.Name == "Age");
    }
}
