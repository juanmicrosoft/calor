using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Ids.Tests;

public class IdScannerTests
{
    [Theory]
    [InlineData("", IdKind.Module, "TestModule")]
    [InlineData("  func TestFunction () → void\n    !", IdKind.Function, "TestFunction")]
    [InlineData("  class TestClass", IdKind.Class, "TestClass")]
    [InlineData("  class TestClass\n    func TestMethod () → void\n      !", IdKind.Method, "TestMethod")]
    [InlineData("  class TestClass\n    property int:TestProperty\n      get\n      set", IdKind.Property, "TestProperty")]
    [InlineData("  class TestClass\n    ctor ()", IdKind.Constructor, ".ctor")]
    [InlineData("  interface ITestInterface", IdKind.Interface, "ITestInterface")]
    [InlineData("  enum Status\n    Active", IdKind.Enum, "Status")]
    public void ScanFindsEffectiveSidecarIdentity(string members, IdKind kind, string name)
    {
        var source = "module TestModule\n" + members + "\n";
        WithIdentity(source, (module, state) =>
        {
            var entries = new IdScanner().Scan(module, "test.calr");
            var expected = Assert.Single(state.Declarations.Where(d => d.Kind == kind && d.Name == name));
            Assert.Contains(entries, e => e.Id == expected.Id && e.Kind == kind && e.Name == name);
            Assert.All(entries, e => Assert.True(IdValidator.IsCanonicalId(e.Id)));
            foreach (var declaration in state.Declarations)
                Assert.DoesNotContain(declaration.Id, new CalorEmitter().Emit(module));
        });
    }

    [Fact]
    public void ScanRecordsRequestedFilePath()
    {
        WithIdentity("module TestModule", (module, _) =>
        {
            var entries = new IdScanner().Scan(module, "/path/to/test.calr");
            Assert.Single(entries);
            Assert.All(entries, e => Assert.Equal("/path/to/test.calr", e.FilePath));
        });
    }

    [Fact]
    public void ScanFindsEveryDeclarationExactlyOnceInComplexFile()
    {
        const string source = """
            module TestModule
              func Func1 () → void
                !
              class Class1
                func Method1 () → void
                  !
                property int:Prop1
                  get
                ctor ()
              interface ITest
              enum Status
                Active
            """;
        WithIdentity(source, (module, state) =>
        {
            var entries = new IdScanner().Scan(module, "test.calr");
            Assert.Equal(8, entries.Count);
            Assert.Equal(8, entries.Select(e => e.Id).Distinct().Count());
            Assert.Equal(state.Declarations.Select(d => d.Id).Order(), entries.Select(e => e.Id).Order());
            Assert.Equal(
                new[] { IdKind.Module, IdKind.Function, IdKind.Class, IdKind.Method, IdKind.Property,
                    IdKind.Constructor, IdKind.Interface, IdKind.Enum }.Order(),
                entries.Select(e => e.Kind).Order());
        });
    }

    private static void WithIdentity(
        string source,
        Action<ModuleNode, DeclarationIdentityStore.State> assertions)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-scanner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "active.calr");
            File.WriteAllText(file, source);
            var diagnostics = new DiagnosticBag();
            var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            var store = new DeclarationIdentityStore(root);
            var state = store.Sync([file]);
            store.Attach(file, source, module);
            assertions(module, state);
            Assert.Equal(source, File.ReadAllText(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
