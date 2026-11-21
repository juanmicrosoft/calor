using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// #1676: four places where the refinement checks of #1529/#1530 did not reach. Mostly a runtime
/// refinement guard already fired and only the static <c>Calor0202</c> was missing; a lambda
/// passed inside <c>§P</c> also had no runtime guard on its parameter.
///
/// <list type="number">
/// <item><c>§P</c> statements were never type-checked, so a wrong-refinement call inside one was
/// silent, and a lambda passed inside one got no refinement on its parameter.</item>
/// <item><c>§ASSIGN x &lt;match&gt;</c> inferred the match against the erased target, so a
/// <c>Pos</c> arm written into a <c>Nat</c> local was accepted.</item>
/// <item>Inside a <c>§TR</c> body, a match-arm mismatch between two named refinements was an
/// ordinary diagnostic, which the try statement's flow cache drops.</item>
/// <item>The checker did not visit class members (methods, constructors, accessors,
/// operators).</item>
/// </list>
/// Each fix has negative controls (the error), positive controls (the same refinement or a base
/// value is still accepted), and compiled runs showing the runtime guards still fire.
/// </summary>
public class RefinementStaticGapsTests
{
    private const string Header = """
        §M{Rt}
          §RTYPE{Nat:i32} (>= # INT:0)
          §RTYPE{Pos:i32} (> # INT:0)
          §F{TakePos:pub} (Pos:p) -> i32
            §R p
          §F{TakeNat:pub} (Nat:n) -> i32
            §R n
        """;

    private static CompilationOptions Options(bool typeCheck = true) => new()
    {
        ContractMode = ContractMode.Debug,
        EnableTypeChecking = typeCheck,
        // Invoking a delegate parameter is an undeclared 'unknown' effect, and §P is console
        // output; effects are not what these tests are about.
        EnforceEffects = false,
    };

    private static CompilationResult Compile(string body, bool typeCheck = true)
        => Program.Compile(Header + "\n" + body, "refinement-static-gaps.calr", Options(typeCheck));

    private static string Errors(CompilationResult result)
        => string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}"));

    private static void AssertDistinct(CompilationResult result, string source, string target, string position)
        => Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && d.Message.Contains($"Cannot use {source} as {target} in {position}"));

    private static void AssertNoRefinementError(CompilationResult result)
        => Assert.False(result.HasErrors, Errors(result));

    private static Assembly CompileAndLoad(string body, bool typeCheck = true)
    {
        var result = Compile(body, typeCheck);
        Assert.False(result.HasErrors, Errors(result));
        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode);
        var compilation = CSharpCompilation.Create(
            $"RefinementStaticGaps_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Assembly assembly, string type, string method, params object?[] args)
    {
        var declaring = Assert.Single(assembly.GetTypes(), t => t.Name == type);
        var info = declaring.GetMethod(method)!;
        var target = info.IsStatic ? null : Activator.CreateInstance(declaring);
        return info.Invoke(target, args);
    }

    private static Exception Throws(Assembly assembly, string type, string method, params object?[] args)
        => Assert.Throws<TargetInvocationException>(() => Invoke(assembly, type, method, args))
            .InnerException!;

    [Fact]
    public void Print_InAModuleWithoutRefinements_StillCompiles()
    {
        var result = Program.Compile("""
            module P P block
              func Twice (int:x) → int
                !
                return (x * 2)

              func Main () → void
                ! cw
                Console.WriteLine(Twice(21))
                Console.WriteLine(("a" + "b"))
            """, "print-plain.calr", new CompilationOptions { EnableTypeChecking = true });
        Assert.False(result.HasErrors, Errors(result));
    }
}
