using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.TypeChecking;
using Calor.Compiler.Verification.Obligations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// Two refinement soundness fixes (epic #1592, "no silent wrong answers").
///
/// <para><b>#1529.</b> An unannotated local inferred from a refined value (<c>§B{~x} a</c>, with
/// <c>a: Nat</c>) is typed <c>Nat</c> by the type checker, but the emitter and the obligation
/// generator only looked at the bind's annotation. So <c>§ASSIGN x INT:-5</c> compiled with no
/// guard and no obligation. The inferred local now carries the refinement: it is declared
/// exactly as <c>§B{~x:Nat} a</c> would be, and every later write is guarded.</para>
///
/// <para><b>#1530.</b> Every named refinement's <c>PredicateText</c> was <c>#&lt;base&gt;</c>, so
/// <c>Nat</c> and <c>Pos</c> over <c>i32</c> compared equal and either was accepted where the
/// other was expected. Named refinements are now nominal: binding, passing, returning or
/// assigning one where a different one is expected is <c>Calor0202</c>. Runtime guards are
/// unchanged.</para>
/// </summary>
public class RefinementIdentityAndInferenceTests
{
    private const string Header = """
        §M{Rt}
          §RTYPE{Nat:i32} (>= # INT:0)
          §RTYPE{Pos:i32} (> # INT:0)
        """;

    private static CompilationResult Compile(string body, CompilationOptions? options = null)
        => Program.Compile(Header + "\n" + body, "refinement-identity.calr", options ?? new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            EnableTypeChecking = true,
        });

    private static string Errors(CompilationResult result)
        => string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}"));

    private static Assembly CompileAndLoad(
        string body, out string generatedCode, CompilationOptions? options = null)
    {
        var result = Compile(body, options);
        Assert.False(result.HasErrors, Errors(result));
        generatedCode = result.GeneratedCode;

        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + generatedCode);
        var compilation = CSharpCompilation.Create(
            $"RefinementIdentity_{Guid.NewGuid():N}",
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

    private static void AssertDistinctRefinementError(CompilationResult result, string source, string target)
        => Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.TypeMismatch
            && (d.Message.Contains($"Cannot use {source} as {target}")
                || d.Message.Contains($"Cannot assign {source} to variable of type {target}")));

    // ───────────────────────── #1530: named refinements are nominal ─────────────────────────

    [Fact]
    public void RefinedType_IdentityIncludesTheRefinementName()
    {
        var predicate = new Ast.IntLiteralNode(new Parsing.TextSpan(0, 0, 1, 1), 0);
        var nat = new RefinedType(PrimitiveType.Int, "#i32", predicate, "Nat");
        var pos = new RefinedType(PrimitiveType.Int, "#i32", predicate, "Pos");
        var natAgain = new RefinedType(PrimitiveType.Int, "#i32", predicate, "Nat");

        Assert.NotEqual<CalorType>(nat, pos);
        Assert.Equal<CalorType>(nat, natAgain);
        Assert.Equal(nat.GetHashCode(), natAgain.GetHashCode());
        Assert.Equal("Nat", nat.SurfaceName);
    }

    // ───────────────────────── Review round 2 findings ─────────────────────────

    private static Assembly CompileAndLoadVerified(string body, bool enableTypeChecking = true)
    {
        var options = new CompilationOptions
        {
            ContractMode = ContractMode.Debug,
            EnableTypeChecking = enableTypeChecking,
            VerifyRefinements = true,
            ObligationPolicy = ObligationPolicy.Permissive,
        };
        var result = Compile(body, options);
        Assert.False(result.HasErrors, Errors(result));
        var syntaxTree = CSharpSyntaxTree.ParseText(
            GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode);
        var compilation = CSharpCompilation.Create(
            $"RefinementIdentityVerified_{Guid.NewGuid():N}",
            [syntaxTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }
}
