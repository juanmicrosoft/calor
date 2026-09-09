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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedParameterSignatures_MatchAcrossInheritedDeclarationScopes(bool verify)
    {
        foreach (var depth in new[] { 0, 1, 2 })
        foreach (var path in new[] { "interface", "base", "interface-base" })
        foreach (var contracts in new[] { "§Q (!= x null)", "§S (!= result null)", "§Q (!= x null)\n§S (!= result null)" })
        foreach (var parameterType in new[] { "Marker", "object" })
        {
            var declarations = """
                §CL{c1:Marker:pub}
                  §MT{mm1:Tag:pub} () -> i32
                    §E{}
                    §R INT:1
                """;
            if (path != "base")
            {
                declarations += "\n" + """
                    §IFACE{i1:IValue}<T>
                      §MT{im1:Check} (T:x) -> T
                    """ + "\n" + Indent(Indent(contracts));
            }
            if (path != "interface")
            {
                declarations += "\n" + "§CL{c2:Base:pub:abs}<T>\n"
                    + (path == "interface-base" ? "  §IMPL{IValue<T>}\n" : "")
                    + "  §MT{mt1:Check:pub:abs} (T:x) -> T\n    §E{}"
                    + (path == "base" ? "\n" + Indent(Indent(contracts)) : "");
            }
            declarations += "\n" + $$"""
                §CL{c3:Impl:pub}
                  {{(path == "interface" ? $"§IMPL{{IValue<{parameterType}>}}" : $"§EXT{{Base<{parameterType}>}}")}}
                  §MT{mt2:Check:pub{{(path == "interface" ? "" : ":over")}}} ({{parameterType}}:value) -> {{parameterType}}
                    §E{}
                    §R value
                """;
            for (var level = depth - 1; level >= 0; level--)
                declarations = $"§CL{{outer{level}:Outer{level}:pub}}\n" + Indent(declarations);
            var assembly = Compile("§M{m1:NestedContracts}\n" + Indent(declarations), verify);
            var prefix = "NestedContracts." + string.Concat(
                Enumerable.Range(0, depth).Select(level => $"Outer{level}+"));
            var instance = Activator.CreateInstance(assembly.GetType(prefix + "Marker")!);
            AssertObjectGuard(assembly.GetType(prefix + "Impl")!, instance, null);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignatureSubstitution_DoesNotCaptureCallerClassParametersAsMethodParameters(bool verify)
    {
        const string source = """
            §M{m1:NestedContracts}
              §IFACE{i1:IValue}<U>
                §MT{im1:Check}<T> (U:x) -> U
                  §Q (!= x null)
                  §S (!= result null)
              §CL{c1:Impl:pub}<T>
                §IMPL{IValue<T>}
                §MT{mt1:Check:pub}<V> (T:value) -> T
                  §E{}
                  §R value
            """;
        var assembly = Compile(source, verify);
        var type = assembly.GetType("NestedContracts.Impl`1")!.MakeGenericType(typeof(string));
        var instance = Activator.CreateInstance(type);
        var method = type.GetMethod("Check")!.MakeGenericMethod(typeof(int));
        Assert.Equal("valid", method.Invoke(instance, ["valid"]));
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [null]));
        Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompositeGenericSignatures_PreserveInheritedGuards(bool verify)
    {
        foreach (var depth in new[] { 0, 1, 2 })
        foreach (var path in new[] { "interface", "base", "interface-base" })
        foreach (var mode in new[] { "pre", "post", "both" })
        foreach (var shape in new[] { "[T]", "T[]", "(T,i32)", "(T[,],i32)", "?T", "Result<T,str>", "Result<(T,i32),str>" })
        {
            var predicate = shape.StartsWith('(') ? "(!= x.Item1 null)"
                : shape.StartsWith("Result", StringComparison.Ordinal) ? "x.IsOk" : "(!= x null)";
            var contracts = string.Join("\n", new[]
            {
                mode != "post" ? "§Q " + predicate : null,
                mode != "pre" ? "§S " + predicate.Replace("x", "result") : null
            }.Where(line => line != null));
            var declarations = "";
            if (path != "base")
            {
                declarations = $"§IFACE{{i1:IValue}}\n  §MT{{im1:Check}}<T> ({shape}:x) -> {shape}\n    §WHERE T : class\n"
                    + Indent(Indent(contracts));
            }
            if (path != "interface")
            {
                declarations += "\n§CL{c1:Base:pub:abs}\n"
                    + (path == "interface-base" ? "  §IMPL{IValue}\n" : "")
                    + $"  §MT{{mt1:Check:pub:abs}}<T> ({shape}:x) -> {shape}\n    §WHERE T : class\n    §E{{}}"
                    + (path == "base" ? "\n" + Indent(Indent(contracts)) : "");
            }
            var implementationShape = shape.Replace("T", "U");
            declarations += "\n" + $$"""
                §CL{c2:Impl:pub}
                  {{(path == "interface" ? "§IMPL{IValue}" : "§EXT{Base}")}}
                  §MT{mt2:Check:pub{{(path == "interface" ? "" : ":over")}}}<U> ({{implementationShape}}:value) -> {{implementationShape}}
                    §WHERE U : class
                    §E{}
                    §R value
                """;
            for (var level = depth - 1; level >= 0; level--)
                declarations = $"§CL{{outer{level}:Outer{level}:pub}}\n" + Indent(declarations);
            var assembly = Compile("§M{m1:NestedContracts}\n" + Indent(declarations), verify);
            var name = "NestedContracts." + string.Concat(
                Enumerable.Range(0, depth).Select(level => $"Outer{level}+")) + "Impl";
            var type = assembly.GetType(name)!;
            var instance = Activator.CreateInstance(type);
            var method = type.GetMethod("Check")!.MakeGenericMethod(typeof(string));
            object? valid = shape switch
            {
                "(T,i32)" => ("valid", 1),
                "(T[,],i32)" => (new string[1, 1], 1),
                "?T" => "valid",
                "Result<T,str>" => Calor.Runtime.Result<string, string>.Ok("valid"),
                "Result<(T,i32),str>" => Calor.Runtime.Result<(string, int), string>.Ok(("valid", 1)),
                _ => new[] { "valid" }
            };
            object? invalid = shape switch
            {
                "(T,i32)" => ((string?)null, 1),
                "(T[,],i32)" => ((string[,]?)null, 1),
                "Result<T,str>" => Calor.Runtime.Result<string, string>.Err("invalid"),
                "Result<(T,i32),str>" => Calor.Runtime.Result<(string, int), string>.Err("invalid"),
                _ => null
            };
            Assert.Equal(valid, method.Invoke(instance, [valid]));
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [invalid]));
            Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamespaceQualifiedInterfaces_KeepGuardsAndRespectTypeShadowing(bool verify)
    {
        foreach (var moduleName in new[] { "NestedContracts", "N.Deep", "_global" })
        foreach (var global in new[] { false, true })
        {
            var namespacePrefix = moduleName == "_global" ? "" : moduleName + ".";
            var source = $$"""
                §M{m1:{{moduleName}}}
                  §CL{c1:Outer:pub}
                    §IFACE{i1:IValue}
                      §MT{im1:Check} (object:x) -> object
                        §Q (!= x null)
                        §S (!= result null)
                    §CL{c2:Impl:pub}
                      §IMPL{ {{(global ? "global::" : "")}}{{namespacePrefix}}Outer.IValue }
                      §MT{mt1:Check:pub} (object:value) -> object
                        §E{}
                        §R value
                """;
            var assembly = Compile(source, verify);
            AssertObjectGuard(assembly.GetType(namespacePrefix + "Outer+Impl")!, "valid", null);
        }

        const string shadowed = """
            §M{m1:Scope}
              §CL{c1:Outer:pub}
                §IFACE{i1:IValue}
                  §MT{im1:Get} (i32:x) -> i32
                    §Q (> x INT:0)
              §CL{c2:Container:pub}
                §CL{c3:Scope:pub}
                  §CL{c4:Outer:pub}
                    §IFACE{i2:IValue}
                      §MT{im2:Get} (i32:x) -> i32
                        §Q (< x INT:0)
                §CL{c5:Relative:pub}
                  §IMPL{Scope.Outer.IValue}
                  §MT{mt1:Get:pub} (i32:x) -> i32
                    §E{}
                    §R x
                §CL{c6:Absolute:pub}
                  §IMPL{global::Scope.Outer.IValue}
                  §MT{mt2:Get:pub} (i32:x) -> i32
                    §E{}
                    §R x
            """;
        var shadowAssembly = Compile(shadowed, verify);
        AssertGuards(shadowAssembly.GetType("Scope.Container+Relative")!, -1, 1);
        AssertGuards(shadowAssembly.GetType("Scope.Container+Absolute")!, 1, -1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelativeNamespaceInterfaces_PreserveGuards(bool verify)
    {
        foreach (var reference in new[] { "Outer.IValue", "Leaf.Outer.IValue",
                     "Deep.Leaf.Outer.IValue", "N.Deep.Leaf.Outer.IValue",
                     "global::N.Deep.Leaf.Outer.IValue" })
        foreach (var contract in new[] { "§Q (> x INT:0)", "§S (> result INT:0)" })
        {
            var source = $$"""
                §M{m1:N.Deep.Leaf}
                  §CL{c1:Outer:pub}
                    §IFACE{i1:IValue}
                      §MT{im1:Get} (i32:x) -> i32
                        {{contract}}
                    §CL{c2:Impl:pub}
                      §IMPL{ {{reference}} }
                      §MT{mt1:Get:pub} (i32:x) -> i32
                        §E{}
                        §R x
                """;
            AssertGuards(Compile(source, verify).GetType("N.Deep.Leaf.Outer+Impl")!, 1, -1);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InheritedStaticMemberPredicates_KeepSourceScope(bool verify)
    {
        foreach (var depth in new[] { 0, 1, 2 })
        foreach (var contract in new[] { "§Q (> x Limits.Min)", "§S (> result Limits.Min)" })
        {
            var implementation = """
                §CL{c4:Impl:pub}
                  §IMPL{Outer.IValue}
                  §MT{mt1:Get:pub} (i32:value) -> i32
                    §E{}
                    §R value
                """;
            var typeName = "Scope.";
            for (var level = 0; level < depth; level++)
            {
                implementation = $"§CL{{c{5 + level}:Container{level}:pub}}\n" + Indent(implementation);
                typeName = "Scope." + $"Container{level}+" + typeName["Scope.".Length..];
            }
            var source = $$"""
                §M{m1:Scope}
                  §CL{c1:Outer:pub}
                    §CL{c2:Limits:pub}
                      §FLD{i32:Min:pub:stat} INT:0
                    §IFACE{i1:IValue}
                      §MT{im1:Get} (i32:x) -> i32
                        {{contract}}
                  §CL{c3:Limits:pub}
                    §FLD{i32:Min:pub:stat} INT:-10
                {{Indent(implementation)}}
                """;
            AssertGuards(Compile(source, verify).GetType(typeName + "Impl")!, 1, -1, -11);
        }

        foreach (var parameter in new[] { "Limits", "value" })
        {
            var source = $$"""
                §M{m1:Scope}
                  §CL{c1:Outer:pub}
                    §CL{c2:Limits:pub}
                      §FLD{i32:Min:pub:stat} INT:-10
                    §IFACE{i1:IValue}
                      §MT{im1:Check} (Payload:Limits) -> i32
                        §Q (> Limits.Min INT:0)
                  §CL{c3:Payload:pub}
                    §FLD{i32:Min:pub} INT:0
                  §CL{c4:Impl:pub}
                    §IMPL{Outer.IValue}
                    §MT{mt1:Check:pub} (Payload:{{parameter}}) -> i32
                      §E{}
                      §R {{parameter}}.Min
                """;
            var assembly = Compile(source, verify);
            var payload = Activator.CreateInstance(assembly.GetType("Scope.Payload")!)!;
            var field = payload.GetType().GetField("Min")!;
            var implementation = Activator.CreateInstance(assembly.GetType("Scope.Impl")!);
            var method = implementation!.GetType().GetMethod("Check")!;
            field.SetValue(payload, 1);
            Assert.Equal(1, method.Invoke(implementation, [payload]));
            field.SetValue(payload, -1);
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(implementation, [payload]));
            Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalPatternReceivers_RemainValuesInInheritedGuards(bool verify)
    {
        foreach (var condition in new[] { "(is x Payload Limits)", "(! (is x Payload Limits))" })
        foreach (var post in new[] { false, true })
        {
            var predicate = condition.StartsWith("(!", StringComparison.Ordinal)
                ? $"(? {condition} BOOL:false (> Limits.Min INT:0))"
                : $"(? {condition} (> Limits.Min INT:0) BOOL:false)";
            var contract = post ? "§S " + predicate.Replace("is x", "is result") : "§Q " + predicate;
            var source = $$"""
                §M{m1:Scope}
                  §CL{c1:Outer:pub}
                    §CL{c2:Limits:pub}
                      §FLD{i32:Min:pub:stat} INT:10
                    §IFACE{i1:IValue}
                      §MT{im1:Check} (object:x) -> object
                        {{contract}}
                  §CL{c3:Payload:pub}
                    §FLD{i32:Min:pub} INT:0
                  §CL{c4:Impl:pub}
                    §IMPL{Outer.IValue}
                    §MT{mt1:Check:pub} (object:value) -> object
                      §E{}
                      §R value
                """;
            var assembly = Compile(source, verify);
            var payloadType = assembly.GetType("Scope.Payload")!;
            var valid = Activator.CreateInstance(payloadType)!;
            payloadType.GetField("Min")!.SetValue(valid, 1);
            var invalid = Activator.CreateInstance(payloadType)!;
            payloadType.GetField("Min")!.SetValue(invalid, -1);
            AssertObjectGuard(assembly.GetType("Scope.Impl")!, valid, invalid);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceFieldReceivers_RemainValuesInInheritedGuards(bool verify)
    {
        foreach (var inheritedField in new[] { false, true })
        foreach (var contract in new[] { "§Q (> Limits.Min INT:0)", "§S (> Limits.Min INT:0)" })
        {
            var source = $$"""
                §M{m1:Scope}
                  §CL{c1:Outer:pub}
                    §CL{c2:Limits:pub}
                      §FLD{i32:Min:pub:stat} INT:10
                    §CL{c3:Storage:pub}
                      §FLD{Payload:Limits:pub}
                    §CL{c4:Base:pub}
                      {{(inheritedField ? "§EXT{Storage}" : "§FLD{Payload:Limits:pub}")}}
                      §MT{mt1:Get:pub:virt} (i32:x) -> i32
                        §E{}
                        {{contract}}
                        §R x
                  §CL{c5:Payload:pub}
                    §FLD{i32:Min:pub} INT:0
                  §CL{c6:Container:pub}
                    §CL{c7:Impl:pub}
                      §EXT{Outer.Base}
                      §MT{mt2:Get:pub:over} (i32:value) -> i32
                        §E{}
                        §R value
                """;
            var assembly = Compile(source, verify);
            foreach (var typeName in new[] { "Scope.Outer+Base", "Scope.Container+Impl" })
            {
                var instance = Activator.CreateInstance(assembly.GetType(typeName)!)!;
                var payload = Activator.CreateInstance(assembly.GetType("Scope.Payload")!)!;
                instance.GetType().GetField("Limits")!.SetValue(instance, payload);
                var method = instance.GetType().GetMethod("Get")!;
                payload.GetType().GetField("Min")!.SetValue(payload, 1);
                Assert.Equal(1, method.Invoke(instance, [1]));
                payload.GetType().GetField("Min")!.SetValue(payload, -1);
                var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(instance, [1]));
                Assert.Equal("ContractViolationException", error.InnerException!.GetType().Name);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearerSourceTypes_ShadowEnclosingValuesInInheritedGuards(bool verify)
    {
        foreach (var outerField in new[] { false, true })
        foreach (var inheritedType in new[] { false, true })
        foreach (var contract in new[] { "§Q (> x Limits.Min)", "§S (> result Limits.Min)" })
        {
            const string limits = """
                §CL{c3:Limits:pub}
                  §FLD{i32:Min:pub:stat} INT:0
                """;
            var declarations = inheritedType
                ? "§CL{c2:Storage:pub}\n" + Indent(limits) + "\n§CL{c4:Base:pub}\n  §EXT{Storage}"
                : "§CL{c4:Base:pub}\n" + Indent(limits);
            declarations += "\n" + Indent($$"""
                §MT{mt1:Get:pub:virt} (i32:x) -> i32
                  §E{}
                  {{contract}}
                  §R x
                """);
            var source = $$"""
                §M{m1:Scope}
                  §CL{c1:Outer:pub}
                {{(outerField ? "    §FLD{i32:Limits:pub:stat} INT:10\n" : "")}}{{Indent(Indent(declarations))}}
                  §CL{c5:Container:pub}
                    §CL{c6:Impl:pub}
                      §EXT{Outer.Base}
                      §CL{c7:Limits:pub}
                        §FLD{i32:Min:pub:stat} INT:-10
                      §MT{mt2:Get:pub:over} (i32:value) -> i32
                        §E{}
                        §R value
                """;
            var assembly = Compile(source, verify);
            AssertGuards(assembly.GetType("Scope.Outer+Base")!, 1, -1, -11);
            AssertGuards(assembly.GetType("Scope.Container+Impl")!, 1, -1, -11);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericTypeNames_DoNotHideUnparameterizedValueReceivers(bool verify)
    {
        foreach (var nestedName in new[] { "Limits", "Other" })
        foreach (var contract in new[] { "§Q (> x Limits.Min)", "§S (> result Limits.Min)" })
        {
            var source = $$"""
                §M{m1:Scope}
                  §CL{c1:Limits:pub}
                    §FLD{i32:Min:pub:stat} INT:-10
                  §CL{c2:Payload:pub}
                    §FLD{i32:Min:pub} INT:0
                  §CL{c3:Outer:pub}
                    §FLD{Payload:Limits:pub:stat}
                    §CL{c4:Base:pub}
                      §CL{c5:{{nestedName}}:pub}<T>
                        §MT{mt1:Tag:pub} () -> i32
                          §E{}
                          §R INT:1
                      §MT{mt2:Get:pub:virt} (i32:x) -> i32
                        §E{}
                        {{contract}}
                        §R x
                    §CL{c6:Impl:pub}
                      §EXT{Base}
                      §MT{mt3:Get:pub:over} (i32:value) -> i32
                        §E{}
                        §R value
                """;
            var assembly = Compile(source, verify);
            assembly.GetType("Scope.Outer")!.GetField("Limits")!.SetValue(null,
                Activator.CreateInstance(assembly.GetType("Scope.Payload")!));
            AssertGuards(assembly.GetType("Scope.Outer+Base")!, 1, -1, -11);
            AssertGuards(assembly.GetType("Scope.Outer+Impl")!, 1, -1, -11);
        }
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
