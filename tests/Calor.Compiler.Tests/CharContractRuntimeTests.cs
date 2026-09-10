using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class CharContractRuntimeTests
{
    [Theory]
    [InlineData("is-digit", '5', 'x')]
    [InlineData("is-letter", 'é', '5')]
    [InlineData("is-whitespace", '\t', 'x')]
    [InlineData("is-upper", 'A', 'a')]
    [InlineData("is-lower", 'a', 'A')]
    public void CharacterPredicates_EnforcePreconditionsAndPostconditions(
        string operation, char valid, char invalid)
    {
        var source = $$"""
            §M{m1:CharContracts}
              §F{f1:Probe:pub} (char:input, char:output) -> char
                §E{}
                §Q ({{operation}} input)
                §S ({{operation}} result)
                §R output
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        foreach (var text in new[] { source, new CalorEmitter().Emit(module) })
        {
            var method = Compile(text).GetMethod("Probe")!;
            Assert.Equal(valid, method.Invoke(null, [valid, valid]));
            var precondition = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [invalid, valid]));
            Assert.IsType<Calor.Runtime.ContractViolationException>(precondition.InnerException);
            var postcondition = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [valid, invalid]));
            Assert.IsType<Calor.Runtime.ContractViolationException>(postcondition.InnerException);
        }
    }

    [Theory]
    [InlineData("(char-code input)", DiagnosticCode.TypeMismatch)]
    [InlineData("(char-upper input)", DiagnosticCode.TypeMismatch)]
    [InlineData("(is-digit missing)", DiagnosticCode.UndefinedReference)]
    public void CharacterContracts_StillRejectNonBooleanAndUnknownReferences(string expression, string code)
    {
        var result = Program.Compile($$"""
            §M{m1:CharContracts}
              §F{f1:Probe:pub} (char:input) -> char
                §E{}
                §Q {{expression}}
                §R input
            """, "char-contracts.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == code);
    }

    private static Type Compile(string source)
    {
        var result = Program.Compile(source, "char-contracts.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            ContractMode = ContractMode.Debug,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("CharContracts_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray()).GetType("CharContracts.CharContractsModule", throwOnError: true)!;
    }
}
