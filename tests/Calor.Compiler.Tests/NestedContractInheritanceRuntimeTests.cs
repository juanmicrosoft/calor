using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NestedContractInheritanceRuntimeTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void InheritedGuards_ExecuteAtEveryNestingDepth(int depth, bool verify)
    {
        var implementation = """
            §CL{c1:Inner:pub}
              §IMPL{IPositive}
              §MT{mt1:Get:pub} (i32:value) -> i32
                §E{}
                §R (? (== value INT:13) INT:-1 value)
            """;
        for (var level = depth - 1; level >= 0; level--)
            implementation = $"§CL{{outer{level}:Outer{level}:pub}}\n" + Indent(implementation);
        var source = "§M{m1:NestedContracts}\n" + Indent(PositiveInterface)
            + "\n" + Indent(implementation);
        var assembly = Compile(source, verify);
        var typeName = "NestedContracts." + string.Concat(
            Enumerable.Range(0, depth).Select(level => $"Outer{level}+")) + "Inner";
        AssertGuards(assembly.GetType(typeName)!, 1, -1, 13);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedClassAndInterfaceChains_ResolveInTheirDeclarationScopes(bool verify)
    {
        var source = """
            §M{m1:NestedContracts}
              §CL{c0:Outer:pub}
                §IFACE{i1:IPositive}
                  §MT{im1:Get} (i32:x) -> i32
                    §Q (> x INT:0)
                    §S (> result INT:0)
                §IFACE{i2:IChild}
                  §EXT{IPositive}
                §CL{c1:Base:pub:abs}
                  §IMPL{IChild}
                  §MT{bm1:Get:pub:abs} (i32:x) -> i32
                    §E{}
                §CL{c2:Middle:pub}
                  §CL{c3:Inner:pub}
                    §EXT{Base}
                    §MT{mt1:Get:pub:over} (i32:value) -> i32
                      §E{}
                      §R (? (== value INT:13) INT:-1 value)
            """;
        AssertGuards(Compile(source, verify).GetType("NestedContracts.Outer+Middle+Inner")!,
            1, -1, 13);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameNamedNestedTypes_DoNotShareContracts(bool verify)
    {
        var positive = """
            §CL{c1:Positive:pub}
              §IFACE{i1:IValue}
                §MT{im1:Get} (i32:x) -> i32
                  §Q (> x INT:0)
                  §S (> result INT:0)
              §CL{c2:Inner:pub}
                §IMPL{IValue}
                §MT{mt1:Get:pub} (i32:x) -> i32
                  §E{}
                  §R x
            """;
        var negative = positive.Replace("Positive", "Negative")
            .Replace("c1", "c3").Replace("c2", "c4")
            .Replace("i1", "i2").Replace("im1", "im2").Replace("mt1", "mt2")
            .Replace("(> ", "(< ");
        var assembly = Compile("§M{m1:NestedContracts}\n" + Indent(positive)
            + "\n" + Indent(negative), verify);
        AssertGuards(assembly.GetType("NestedContracts.Positive+Inner")!, 1, -1);
        AssertGuards(assembly.GetType("NestedContracts.Negative+Inner")!, -1, 1);
    }

    [Theory]
    [InlineData("IPositive.Get")]
    [InlineData("Outer.IPositive.Get")]
    public void ExplicitNestedInterfaceNames_ResolveWithoutLosingGuards(string method)
    {
        var source = """
            §M{m1:NestedContracts}
              §CL{c1:Outer:pub}
            """ + "\n" + Indent(Indent(PositiveInterface)) + "\n" + Indent(Indent($$"""
            §CL{c2:Inner:pub}
              §IMPL{IPositive}
              §MT{mt1:{{method}}:pub} (i32:value) -> i32
                §E{}
                §R value
            """));
        AssertGuards(Compile(source, false).GetType("NestedContracts.Outer+Inner")!, 1, -1);
    }

    private static Assembly Compile(string source, bool verify)
    {
        var result = Program.Compile(source, "nested-contracts.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = true,
            VerifyContracts = verify,
            ContractMode = ContractMode.Debug,
            ElideProvenGuards = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("NestedContracts_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static void AssertGuards(Type type, int valid, params int[] invalid)
    {
        var instance = Activator.CreateInstance(type);
        var method = type.GetMethod("Get")
            ?? Assert.Single(type.GetInterfaces()).GetMethod("Get")!;
        Assert.Equal(valid, method.Invoke(instance, [valid]));
        foreach (var input in invalid)
        {
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [input]));
            Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
        }
    }

    private static string Indent(string source) =>
        string.Join("\n", source.Split('\n').Select(line => "  " + line));

    private const string PositiveInterface = """
        §IFACE{i1:IPositive}
          §MT{im1:Get} (i32:x) -> i32
            §Q (> x INT:0)
            §S (> result INT:0)
        """;
}
