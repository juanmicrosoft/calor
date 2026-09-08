using System.Reflection;
using Calor.Compiler;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Verification.Tests;

public class ParameterPoststateTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ParameterMutation_KeepsFailingRuntimeGuard(bool isVoid, bool verify)
    {
        foreach (var mutation in new[]
        {
            "§ASSIGN x INT:-1",
            "§IF{i1} (> x 0)\n      §ASSIGN x INT:-1",
            "§B{first:i32} (pre-dec x)\n    §B{second:i32} (pre-dec x)",
            "§B{first:i32} (post-dec x)\n    §B{second:i32} (post-dec x)"
        })
        {
            var result = Compile(Source(isVoid, mutation), verify);
            Assert.DoesNotContain(result.Diagnostics, d =>
                d.Verification is { Status: ProofStatus.Proven, IsVacuous: false });
            if (verify)
                Assert.Contains(result.Diagnostics, d => d.Verification?.Status == ProofStatus.Unsupported);
            var exception = Assert.Throws<TargetInvocationException>(() => Invoke(result));
            Assert.Equal("ContractViolationException", exception.InnerException!.GetType().Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedParameter_ProvesAndExecutes(bool isVoid)
    {
        var result = Compile(Source(isVoid, ""), true);
        Assert.Contains(result.Diagnostics, d =>
            d.Verification is { Status: ProofStatus.Proven, IsVacuous: false });
        Assert.Equal(isVoid ? null : (object)1, Invoke(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmCache_BodyMutationCannotReuseEntryStateProof(bool isVoid)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "poststate-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var original = Source(isVoid, "");
            var cold = Compile(original, true, directory);
            Assert.Contains(cold.Diagnostics, d =>
                d.Verification is { Status: ProofStatus.Proven, IsVacuous: false });
            Assert.NotEmpty(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
            var warm = Compile(original, true, directory);
            Assert.Equal(isVoid ? null : (object)1, Invoke(warm));

            var edited = Compile(Source(isVoid, "§ASSIGN x INT:-1"), true, directory);
            Assert.Contains(edited.Diagnostics, d => d.Verification?.Status == ProofStatus.Unsupported);
            var exception = Assert.Throws<TargetInvocationException>(() => Invoke(edited));
            Assert.Equal("ContractViolationException", exception.InnerException!.GetType().Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PropertyGetterMutatingRefAlias_KeepsRuntimeGuard()
    {
        const string source = """
            §M{m1:HiddenMutation}
              §CL{c1:Counter:pub}
                §FLD{i32:Value:pub}
                §PROP{p1:Mutate:i32:pub}
                  §GET
                    §ASSIGN Value INT:-1
                    §R Value
                §MT{mt1:Change:pub}
                  §I{i32:x:ref}
                  §O{i32}
                  §E{}
                  §Q (>= x 0)
                  §S (>= x 0)
                  §B{ignored:i32} Mutate
                  §R x
            """;
        const string harness = """
            public static class Caller
            {
                public static int Run()
                {
                    var counter = new HiddenMutation.Counter { Value = 1 };
                    return counter.Change(ref counter.Value);
                }
            }
            """;
        foreach (var verify in new[] { false, true })
        {
            var result = Compile(source, verify);
            if (verify)
                Assert.Contains(result.Diagnostics, d => d.Verification?.Status == ProofStatus.Unsupported);
            var exception = Assert.Throws<TargetInvocationException>(() => Invoke(result, harness));
            Assert.Equal("ContractViolationException", exception.InnerException!.GetType().Name);
        }
    }

    [Fact]
    public void PreviousCacheFormat_IsRejected()
    {
        var entry = new VerificationCacheEntry { Version = "1.14" };
        Assert.False(entry.IsValidFor(null));
    }

    private static string Source(bool isVoid, string mutation) => $$"""
        §M{m1:ParameterPoststate}
          §F{f1:Change:pub} (i32:x) -> {{(isVoid ? "void" : "i32")}}
            §E{}
            §Q (>= x 0)
            §S (>= x 0)
            {{mutation}}
            §R{{(isVoid ? "" : " x")}}
        """;

    private static CompilationResult Compile(string source, bool verify, string? cacheDirectory = null)
    {
        var result = Program.Compile(source, "poststate.calr", new Calor.Compiler.CompilationOptions
        {
            VerifyContracts = verify,
            Verbose = true,
            ContractMode = ContractMode.Debug,
            ElideProvenGuards = true,
            EnforceEffects = true,
            EnableTypeChecking = true,
            StatusWriter = TextWriter.Null,
            VerificationCacheOptions = new VerificationCacheOptions
            {
                Enabled = cacheDirectory != null,
                ProjectDirectory = cacheDirectory
            }
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        return result;
    }

    private static object? Invoke(CompilationResult result, string? harness = null)
    {
        var compilation = CSharpCompilation.Create(
            "Poststate_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode + harness)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        if (harness != null)
            return assembly.GetType("Caller")!.GetMethod("Run")!.Invoke(null, null);
        var type = Assert.Single(assembly.GetTypes(), t => t.Name == "ParameterPoststateModule");
        return type.GetMethod("Change")!.Invoke(null, [1]);
    }
}
