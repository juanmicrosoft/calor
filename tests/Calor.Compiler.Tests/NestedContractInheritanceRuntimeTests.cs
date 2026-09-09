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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassHeader_DoesNotBindToItsOwnNestedInterface(bool verify)
    {
        const string source = """
            §M{m1:NestedContracts}
              §IFACE{i1:IValue}
                §MT{im1:Get} (i32:x) -> i32
                  §Q (> x INT:0)
              §CL{c1:Outer:pub}
                §CL{c2:Inner:pub}
                  §IMPL{IValue}
                  §IFACE{i2:IValue}
                    §MT{im2:Get} (i32:x) -> i32
                      §Q (< x INT:0)
                  §MT{mt1:Get:pub} (i32:value) -> i32
                    §E{}
                    §R value
            """;
        var assembly = Compile(source, verify);
        var type = assembly.GetType("NestedContracts.Outer+Inner")!;
        Assert.Equal(assembly.GetType("NestedContracts.IValue"), Assert.Single(type.GetInterfaces()));
        AssertGuards(type, 1, -1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnclosingBaseClass_ContributesInheritedNestedInterfaces(bool verify)
    {
        const string source = """
            §M{m1:NestedContracts}
              §CL{c1:Base:pub}
                §IFACE{i1:IPositive}
                  §MT{im1:Get} (i32:x) -> i32
                    §Q (> x INT:0)
                    §S (> result INT:0)
              §CL{c2:Outer:pub}
                §EXT{Base}
                §CL{c3:Inner:pub}
                  §IMPL{IPositive}
                  §MT{mt1:Get:pub} (i32:value) -> i32
                    §E{}
                    §R (? (== value INT:13) INT:-1 value)
            """;
        var assembly = Compile(source, verify);
        var type = assembly.GetType("NestedContracts.Outer+Inner")!;
        Assert.Equal(assembly.GetType("NestedContracts.Base+IPositive"), Assert.Single(type.GetInterfaces()));
        AssertGuards(type, 1, -1, 13);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedEnclosingType_PreservesContractsAndTypeSubstitutions(bool verify)
    {
        const string source = """
            §M{m1:NestedContracts}
              §CL{c1:Outer:pub}<T>
                §IFACE{i1:IPositive}
                  §MT{im1:Get} (i32:x) -> i32
                    §Q (> x INT:0)
                    §S (> result INT:0)
                §IFACE{i2:IValue}
                  §MT{im2:Check} (object:x) -> object
                    §Q (is x T)
                    §S (is result T)
              §CL{c2:Inner:pub}
                §IMPL{Outer<i32>.IPositive}
                §MT{mt1:Get:pub} (i32:value) -> i32
                  §E{}
                  §R (? (== value INT:13) INT:-1 value)
              §CL{c3:Typed:pub}
                §IMPL{Outer<i32>.IValue}
                §MT{mt2:Check:pub} (object:value) -> object
                  §E{}
                  §R value
            """;
        var assembly = Compile(source, verify);
        AssertGuards(assembly.GetType("NestedContracts.Inner")!, 1, -1, 13);
        var type = assembly.GetType("NestedContracts.Typed")!;
        var instance = Activator.CreateInstance(type);
        var method = type.GetMethod("Check")!;
        Assert.Equal(1, method.Invoke(instance, [1]));
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, ["wrong type"]));
        Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericEnclosingArity_DistinguishesOtherwiseIdenticalNestedTypes(bool verify)
    {
        const string positive = """
            §CL{c1:Outer:pub}
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
        var negative = positive.Replace("§CL{c1:Outer:pub}", "§CL{c3:Outer:pub}<T>")
            .Replace("c2", "c4").Replace("i1", "i2")
            .Replace("im1", "im2").Replace("mt1", "mt2").Replace("(> ", "(< ");
        var assembly = Compile("§M{m1:NestedContracts}\n" + Indent(positive)
            + "\n" + Indent(negative), verify);
        AssertGuards(assembly.GetType("NestedContracts.Outer+Inner")!, 1, -1);
        AssertGuards(assembly.GetType("NestedContracts.Outer`1+Inner")!.MakeGenericType(typeof(int)),
            -1, 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifiedGenericArguments_StayInTheCallersLexicalScope(bool verify)
    {
        foreach (var contracts in new[] { "§Q (is x U)", "§S (is result U)", "§Q (is x U)\n§S (is result U)" })
        foreach (var outerParameter in new[] { "T", "V" })
        {
            var source = $$"""
                §M{m1:NestedContracts}
                  §CL{c1:Outer:pub}<{{outerParameter}}>
                    §IFACE{i1:IValue}<U>
                      §MT{im1:Check} (object:x) -> object
                {{Indent(Indent(Indent(Indent(contracts))))}}
                  §CL{c2:Impl:pub}<T>
                    §IMPL{Outer<i32>.IValue<T>}
                    §MT{mt1:Check:pub} (object:value) -> object
                      §E{}
                      §R value
                """;
            var assembly = Compile(source, verify);
            var type = assembly.GetType("NestedContracts.Impl`1")!.MakeGenericType(typeof(string));
            Assert.Equal(new[] { typeof(int), typeof(string) }, Assert.Single(type.GetInterfaces()).GetGenericArguments());
            var instance = Activator.CreateInstance(type);
            var method = type.GetMethod("Check")!;
            Assert.Equal("valid", method.Invoke(instance, ["valid"]));
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [1]));
            Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedPredicateTypes_KeepTheDeclaringScope(bool verify)
    {
        foreach (var contracts in new[] { "§Q (is x Marker)", "§S (is result Marker)", "§Q (is x Marker)\n§S (is result Marker)" })
        foreach (var generic in new[] { false, true })
        {
            var source = $$"""
                §M{m1:NestedContracts}
                  §CL{c1:Outer:pub}
                    §CL{c2:Marker:pub}
                      §MT{mm1:Tag:pub} () -> i32
                        §E{}
                        §R INT:1
                    §IFACE{i1:IValue}
                      §MT{im1:Check} (object:x) -> object
                {{Indent(Indent(Indent(Indent(contracts))))}}
                  §CL{c3:Marker:pub}
                    §MT{mm2:Tag:pub} () -> i32
                      §E{}
                      §R INT:2
                  §CL{c4:Impl:pub}
                    §IMPL{Outer.IValue}
                    §MT{mt1:Check:pub} (object:value) -> object
                      §E{}
                      §R value
                """;
            if (generic)
            {
                source = source.Replace("§CL{c1:Outer:pub}", "§CL{c1:Outer:pub}<T>")
                    .Replace("§CL{c2:Marker:pub}", "§CL{c2:Marker:pub}<T>")
                    .Replace("Outer.IValue", "Outer<i32>.IValue")
                    .Replace(" Marker)", " Marker<str>)");
            }
            var assembly = Compile(source, verify);
            var type = assembly.GetType("NestedContracts.Impl")!;
            var markerType = generic
                ? assembly.GetType("NestedContracts.Outer`1+Marker`1")!.MakeGenericType(typeof(int), typeof(string))
                : assembly.GetType("NestedContracts.Outer+Marker")!;
            var valid = Activator.CreateInstance(markerType);
            var invalid = Activator.CreateInstance(assembly.GetType("NestedContracts.Marker")!);
            var instance = Activator.CreateInstance(type);
            var method = type.GetMethod("Check")!;
            Assert.Same(valid, method.Invoke(instance, [valid]));
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [invalid]));
            Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedPredicateArguments_PreserveNamedTypeScope(bool verify)
    {
        foreach (var contracts in new[] { "§Q (is x Box<Marker>)", "§S (is result Box<Marker>)", "§Q (is x Box<Marker>)\n§S (is result Box<Marker>)" })
        {
            var source = $$"""
                §M{m1:NestedContracts}
                  §CL{c1:Outer:pub}
                    §CL{c2:Marker:pub}
                      §MT{mm1:Tag:pub} () -> i32
                        §E{}
                        §R INT:1
                    §CL{c3:Box:pub}<T>
                      §MT{mm2:Tag:pub} () -> i32
                        §E{}
                        §R INT:2
                    §IFACE{i1:IValue}
                      §MT{im1:Check} (object:x) -> object
                {{Indent(Indent(Indent(Indent(contracts))))}}
                  §CL{c4:Marker:pub}
                    §MT{mm3:Tag:pub} () -> i32
                      §E{}
                      §R INT:3
                  §CL{c5:Impl:pub}
                    §IMPL{Outer.IValue}
                    §MT{mt1:Check:pub} (object:value) -> object
                      §E{}
                      §R value
                """;
            var assembly = Compile(source, verify);
            var box = assembly.GetType("NestedContracts.Outer+Box`1")!;
            var valid = Activator.CreateInstance(box.MakeGenericType(assembly.GetType("NestedContracts.Outer+Marker")!));
            var invalid = Activator.CreateInstance(box.MakeGenericType(assembly.GetType("NestedContracts.Marker")!));
            AssertObjectGuard(assembly.GetType("NestedContracts.Impl")!, valid, invalid);

            var headerScoped = source.Replace("§CL{c1:Outer:pub}", "§CL{c1:Outer:pub}<V>")
                .Replace("Box<Marker>", "Box<V>")
                .Replace("§IMPL{Outer.IValue}", "§IMPL{Outer<Marker>.IValue}\n    §CL{c6:Marker:pub}\n      §MT{mm4:Tag:pub} () -> i32\n        §E{}\n        §R INT:4");
            assembly = Compile(headerScoped, verify);
            var outerMarker = assembly.GetType("NestedContracts.Marker")!;
            var innerMarker = assembly.GetType("NestedContracts.Impl+Marker")!;
            box = assembly.GetType("NestedContracts.Outer`1+Box`1")!;
            valid = Activator.CreateInstance(box.MakeGenericType(outerMarker, outerMarker));
            invalid = Activator.CreateInstance(box.MakeGenericType(outerMarker, innerMarker));
            AssertObjectGuard(assembly.GetType("NestedContracts.Impl")!, valid, invalid);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedPredicateArguments_RespectMethodGenericRenaming(bool verify)
    {
        foreach (var contracts in new[] { "§Q (is x Box<T>)", "§S (is result Box<T>)", "§Q (is x Box<T>)\n§S (is result Box<T>)" })
        foreach (var parameter in new[] { "T", "W" })
        {
            var source = $$"""
                §M{m1:NestedContracts}
                  §CL{c1:Outer:pub}<T>
                    §CL{c2:Box:pub}<U>
                      §MT{mm1:Tag:pub} () -> i32
                        §E{}
                        §R INT:1
                    §IFACE{i1:IValue}
                      §MT{im1:Check}<{{parameter}}> (object:x) -> object
                {{Indent(Indent(Indent(Indent(contracts.Replace("Box<T>", $"Box<{parameter}>")))))}}
                  §CL{c3:Impl:pub}
                    §IMPL{Outer<i32>.IValue}
                    §MT{mt1:Check:pub}<V> (object:value) -> object
                      §E{}
                      §R value
                """;
            var assembly = Compile(source, verify);
            var type = assembly.GetType("NestedContracts.Impl")!;
            var method = type.GetMethod("Check")!.MakeGenericMethod(typeof(string));
            var instance = Activator.CreateInstance(type);
            var box = assembly.GetType("NestedContracts.Outer`1+Box`1")!;
            var valid = Activator.CreateInstance(box.MakeGenericType(typeof(int), typeof(string)));
            var invalid = Activator.CreateInstance(box.MakeGenericType(typeof(int), typeof(int)));
            Assert.Same(valid, method.Invoke(instance, [valid]));
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [invalid]));
            Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
        }
    }

    private static void AssertObjectGuard(Type type, object? valid, object? invalid)
    {
        var instance = Activator.CreateInstance(type);
        var method = type.GetMethod("Check")!;
        Assert.Same(valid, method.Invoke(instance, [valid]));
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [invalid]));
        Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
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
