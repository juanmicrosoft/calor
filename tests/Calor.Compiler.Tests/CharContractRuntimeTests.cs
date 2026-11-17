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
    // The retired Lisp char operators are ordinary System.Char calls in native syntax.
    // `! unknown` keeps unrelated effect findings out of the contract checks under test.
    [InlineData("char.GetNumericValue(input)", DiagnosticCode.TypeMismatch)]
    [InlineData("char.ToUpper(input)", DiagnosticCode.TypeMismatch)]
    [InlineData("char.IsDigit(missing)", DiagnosticCode.UndefinedReference)]
    public void CharacterContracts_StillRejectNonBooleanAndUnknownReferences(string expression, string code)
    {
        var result = Program.Compile($$"""
            module CharContracts CharContracts block
              func Probe (char:input) → char
                ! unknown
                requires {{expression}}
                return input
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
