using Calor.LanguageServer.Tests.Helpers;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class DocumentSymbolHandlerTests
{
    [Fact]
    public void DocumentSymbols_NativeRecordHasRecordDetailAndNoSyntheticCopyDeclaration()
    {
        const string source = """
            §M{Records}
              record P:pub (int:X, int:Y)
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
            §M{TestModule}
              §F{Add}
                §R 0
              §F{Subtract}
                §R 0
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

    [Fact]
    public void DocumentSymbols_ClassWithMembersListed()
    {
        var source = """
            §M{TestModule}
              §CL{Calculator}
                §FLD{i32:value}
                §MT{Add}
                  §I{i32:x}
                  §O{i32}
                  §R x
                §MT{Subtract}
                  §I{i32:x}
                  §O{i32}
                  §R x
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
    public void DocumentSymbols_InterfacesListed()
    {
        var source = """
            §M{TestModule}
              §IFACE{IReadable}
                §MT{Read}
                  §O{str}
              §IFACE{IWritable}
                §MT{Write}
                  §I{str:data}
                  §O{void}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Interfaces.Count);
        Assert.Contains(ast.Interfaces, i => i.Name == "IReadable");
        Assert.Contains(ast.Interfaces, i => i.Name == "IWritable");
    }

    [Fact]
    public void DocumentSymbols_EnumsListed()
    {
        var source = """
            §M{TestModule}
              §EN{Color}
              §EM{Red}
              §EM{Green}
              §EM{Blue}
              §/EN
              §EN{Size}
              §EM{Small}
              §EM{Medium}
              §EM{Large}
              §/EN
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Enums.Count);
        Assert.Contains(ast.Enums, e => e.Name == "Color");
        Assert.Contains(ast.Enums, e => e.Name == "Size");
    }

    [Fact]
    public void DocumentSymbols_EnumMembersListed()
    {
        var source = """
            §M{TestModule}
              §EN{Status}
              §EM{Pending}
              §EM{Active}
              §EM{Complete}
              §/EN
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Enums);
        var enumDef = ast.Enums[0];
        Assert.Equal("Status", enumDef.Name);
        Assert.Equal(3, enumDef.Members.Count);
        Assert.Contains(enumDef.Members, m => m.Name == "Pending");
        Assert.Contains(enumDef.Members, m => m.Name == "Active");
        Assert.Contains(enumDef.Members, m => m.Name == "Complete");
    }

    [Fact]
    public void DocumentSymbols_DelegatesListed()
    {
        var source = """
            §M{TestModule}
              §DEL{Action}
              §O{void}
              §/DEL
              §DEL{Func}
              §I{i32:x}
              §O{i32}
              §/DEL
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Equal(2, ast.Delegates.Count);
        Assert.Contains(ast.Delegates, d => d.Name == "Action");
        Assert.Contains(ast.Delegates, d => d.Name == "Func");
    }

    [Fact]
    public void DocumentSymbols_MixedTypesListed()
    {
        var source = """
            §M{TestModule}
              §F{Helper}
                §R 0
              §CL{MyClass}
                §FLD{i32:dummy:priv}
              §IFACE{IMyInterface}
                §MT{Foo:pub}
                  §O{void}
              §EN{MyEnum}
                §EM{Value}
              §DEL{MyDelegate}
                §O{void}
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        Assert.Single(ast.Functions);
        Assert.Single(ast.Classes);
        Assert.Single(ast.Interfaces);
        Assert.Single(ast.Enums);
        Assert.Single(ast.Delegates);
    }

    [Fact]
    public void DocumentSymbols_FunctionParameters_Counted()
    {
        var source = """
            §M{TestModule}
              §F{Calculate}
                §I{i32:a}
                §I{i32:b}
                §I{str:label}
                §O{i32}
                §R a + b
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
            §M{TestModule}
              §CL{Person}
                §PROP{Name:str:pub}
                §GET
                §R ""
                §/GET
                §/PROP
                §PROP{Age:i32:pub}
                §GET
                §R 0
                §/GET
                §SET
                §/SET
                §/PROP
            """;

        var ast = LspTestHarness.GetAst(source);

        Assert.NotNull(ast);
        var cls = ast.Classes[0];
        Assert.Equal(2, cls.Properties.Count);
        Assert.Contains(cls.Properties, p => p.Name == "Name");
        Assert.Contains(cls.Properties, p => p.Name == "Age");
    }
}
