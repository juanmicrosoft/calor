using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class SignatureHelpHandlerTests
{
    private const string Calls = """
        module TestModule
          func Add private (int:a, int:b) → int
            return a + b
          func Neg private (int:x) → int
            return -x
          func Run public () → int
            let label = "Add(1, 2"
            return Add(Neg(1), 2)
        """;

    private static async Task<SignatureHelp?> HelpAtAsync(string source, int offset)
    {
        var workspace = new WorkspaceState();
        var uri = DocumentUri.From("file:///signature.calr");
        var state = workspace.GetOrCreate(uri, source, version: 1);
        Assert.NotNull(state.Snapshot.Ast);
        var (line, column) = LspTestHarness.GetLineColumn(source, offset);
        return await new SignatureHelpHandler(workspace).Handle(new SignatureHelpParams
        {
            TextDocument = new TextDocumentIdentifier(uri),
            Position = new Position(line - 1, column - 1)
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Add(Neg(", "Neg", 0)]   // inside the nested call
    [InlineData("Add(Neg(1)", "Add", 0)] // back in the outer call's first argument
    [InlineData("Add(Neg(1), ", "Add", 1)]
    [InlineData("return Add(", "Add", 0)]
    public async Task Handle_NativeCall_ShowsTheEnclosingSignatureAndActiveArgumentAsync(
        string prefix, string name, int argument)
    {
        var offset = Calls.LastIndexOf(prefix, StringComparison.Ordinal) + prefix.Length;

        var help = await HelpAtAsync(Calls, offset);

        Assert.NotNull(help);
        var signature = Assert.Single(help.Signatures);
        Assert.StartsWith(name + "(", signature.Label);
        Assert.Equal(argument, help.ActiveParameter);
        // Clients highlight the active parameter by finding its label in the signature label.
        Assert.All(signature.Parameters!, parameter =>
            Assert.Contains(parameter.Label.Label!, signature.Label));
    }

    [Theory]
    [InlineData("let label = ")]   // not in a call
    [InlineData("\"Add(1, ")]     // inside a string literal that looks like a call
    [InlineData("func Add private (int:a")] // a declaration's parameter list, not a call
    public async Task Handle_OutsideANativeCall_ReturnsNothingAsync(string prefix)
    {
        var offset = Calls.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;

        Assert.Null(await HelpAtAsync(Calls, offset));
    }

    [Theory]
    [InlineData("Add(|", "Add", 0)]
    [InlineData("Add(1, |", "Add", 1)]
    [InlineData("Math.Max(a, |", "Math.Max", 1)]
    [InlineData("obj?.Run(|", "obj.Run", 0)]
    [InlineData("Parse<int>(s, |", "Parse", 1)]
    [InlineData("Add([1, 2], {x}, |", "Add", 2)]
    [InlineData("Add(f(1, 2), |", "Add", 1)]
    [InlineData("Add('(', |", "Add", 1)]
    [InlineData("Add(1, // a, b(\n  |", "Add", 1)]
    public void NativeCallContext_FindsTheCalleeAndCountsArguments(string text, string callee, int argument)
    {
        var offset = text.IndexOf('|');

        var context = NativeCallContext.Find(text.Remove(offset, 1), offset);

        Assert.NotNull(context);
        Assert.Equal(callee, context.Callee);
        Assert.Equal(argument, context.ArgumentIndex);
    }

    [Theory]
    [InlineData("if (x|")]
    [InlineData("func Add (int:a|")]
    [InlineData("Add(1)|")]
    [InlineData("let s = \"f(|")]
    [InlineData("(1 + |")]
    public void NativeCallContext_IgnoresNonCalls(string text)
    {
        var offset = text.IndexOf('|');

        Assert.Null(NativeCallContext.Find(text.Remove(offset, 1), offset));
    }

    [Fact]
    public void RegistrationTriggers_AreNativeCallPunctuation()
    {
        var handler = new SignatureHelpHandler(new WorkspaceState());
        var options = ((OmniSharp.Extensions.LanguageServer.Protocol.Document.ISignatureHelpHandler)handler)
            .GetRegistrationOptions(new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.SignatureHelpCapability(),
                new OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities());

        Assert.Equal(["(", ","], options.TriggerCharacters!);
        Assert.DoesNotContain(options.TriggerCharacters!.Concat(options.RetriggerCharacters!), c => c.Contains('§'));
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
        Assert.Equal("int", func.Parameters[0].TypeName);
        Assert.Equal("b", func.Parameters[1].Name);
        Assert.Equal("int", func.Parameters[1].TypeName);
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
        Assert.Equal("string", func.Output.TypeName);
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
        Assert.Equal("int", func.Parameters[0].TypeName);
        Assert.Equal("string", func.Parameters[1].TypeName);
        Assert.Equal("bool", func.Parameters[2].TypeName);
        Assert.Equal("double", func.Parameters[3].TypeName);
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
