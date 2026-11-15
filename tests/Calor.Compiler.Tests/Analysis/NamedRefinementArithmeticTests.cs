using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification.Obligations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// #1515: operators on a value of a NAMED refinement type (<c>§RTYPE</c>) were rejected with
/// Calor0202 because the type checker saw <c>i32{#i32}</c> and never unwrapped it to its base.
/// An inline refinement (<c>§I{i32:x} | (pred)</c>) was already typed as its base and worked.
///
/// <para>The rule these tests pin: operators see a named refinement as its base type, and the
/// RESULT is the base type, never the refinement. Writing a result back into a refined variable
/// or return is checked by the obligation engine (Subtype / RefinementReturn obligations and
/// runtime guards), exactly as for an inline refinement. The checker's existing refusal to bind
/// a plain base value into a <c>§B{x:Refined}</c> is unchanged.</para>
/// </summary>
public class NamedRefinementArithmeticTests
{
    // Generated-output validation stays ON: an accepted program must also compile as C#, so a
    // test cannot pass on a program the checker accepts but Roslyn rejects.
    private static CompilationResult Check(string source)
        => Program.Compile(source, "t.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
        });

    private static void AssertNoErrors(string source)
    {
        var result = Check(source);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Predicate(string baseType) => baseType switch
    {
        "f64" or "f32" => "(> # FLOAT:0.0)",
        _ => "(>= # INT:0)",
    };

    /// <summary>The C# result type of a binary arithmetic operator on two values of this base:
    /// sub-int widths promote to int (C# 12.4.7).</summary>
    private static string ResultOf(string baseType) => baseType switch
    {
        "i16" or "u8" => "i32",
        _ => baseType,
    };

    public static TheoryData<string, string> OperatorsByBase()
    {
        var data = new TheoryData<string, string>();
        foreach (var baseType in new[] { "i32", "i64", "i16", "u8", "u32", "f32", "f64", "decimal" })
        {
            foreach (var op in new[] { "+", "-", "*", "/", "%" })
                data.Add(baseType, op);
        }
        return data;
    }

    /// <summary>
    /// The decimal rule must not fire against an operand the checker does not model: C# may bind
    /// a user-defined operator with a different result type. This program compiled before the
    /// decimal rule existed and must still compile (found in review round 2).
    /// </summary>
    [Fact]
    public void DecimalWithUnmodeledOperand_DefersToUserDefinedOperator()
    {
        AssertNoErrors("""
            module R R block
              class Box
                property Box:Value
                  get
                    return this
                op + (decimal:d, Box:b) → double
                  !
                  return 2.0
              func Good (decimal:d, Box:b) → double
                !
                let double:x = ((d + b.Value) + 1.0)
                return x
            """);
    }

    // ---- Compile, emit and run with the type checker ON ----

    private const string RuntimeSource = """
        §M{Rt}
          §RTYPE{Nat:i32} (>= # INT:0)
          §RTYPE{Small:i32} (< # INT:100)
          §RTYPE{BigNat:i64} (>= # INT:0)
          §RTYPE{Pos:f64} (> # FLOAT:0.0)
          §F{Calc:pub} (Nat:a, Small:b, i32:c) -> i32
            §R (+ (- (* a b) (/ a INT:2)) (% (+ a c) INT:7))
          §F{Neg:pub} (Nat:a) -> i32
            §R (- a)
          §F{Less:pub} (Nat:a, Small:b) -> bool
            §R (< a b)
          §F{Wide:pub} (BigNat:a, BigNat:b) -> i64
            §R (* a b)
          §F{Halve:pub} (Pos:x) -> f64
            §R (/ x FLOAT:2.0)
          §F{Down:pub} (Nat:a) -> Nat
            §R (- a INT:3)
          §F{Step:pub} (Nat:a) -> i32
            §B{~x:Nat} a
            §ASSIGN x (- x INT:5)
            §R x
          §F{Dec:pub} (Nat:a) -> i32
            §B{~x:Nat} a
            §ASSIGN x (- x INT:1)
            §R x
        """;

    private static Assembly CompileAndLoad(string source, out string generatedCode)
    {
        var result = Program.Compile(source, "named-refinement.calr", new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            EnableTypeChecking = true,
        });
        Assert.False(result.HasErrors,
            string.Join(Environment.NewLine, result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
        generatedCode = result.GeneratedCode;

        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + generatedCode);
        var compilation = CSharpCompilation.Create(
            $"NamedRefinementArithmetic_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Assembly assembly, string method, params object?[] args)
        => Assert.Single(assembly.GetTypes(), t => t.Name == "RtModule")
            .GetMethod(method)!
            .Invoke(null, args);

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
