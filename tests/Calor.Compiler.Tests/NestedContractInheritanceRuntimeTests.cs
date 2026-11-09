using System.Reflection;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
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
            class Inner public
              implements IPositive
              func Get public (int:value) → int
                !
                return value == 13 ? -1 : value
            """;
        for (var level = depth - 1; level >= 0; level--)
            implementation = $"class Outer{level} public\n" + Indent(implementation);
        var source = "module NestedContracts NestedContracts block\n" + Indent(NativePositiveInterface)
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
            module NestedContracts NestedContracts block
              class Outer public
                interface IPositive
                  func Get (int:x) → int
                    requires x > 0
                    => result > 0
                interface IChild
                  extends IPositive
                class Base public abstract
                  implements IChild
                  func Get public abstract (int:x) → int
                    !
                class Middle public
                  class Inner public
                    extends Base
                    func Get public override (int:value) → int
                      !
                      return value == 13 ? -1 : value
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
            class Positive public
              interface IValue
                func Get (int:x) → int
                  requires x > 0
                  => result > 0
              class Inner public
                implements IValue
                func Get public (int:x) → int
                  !
                  return x
            """;
        var negative = positive.Replace("Positive", "Negative")
            .Replace("c1", "c3").Replace("c2", "c4")
            .Replace("i1", "i2").Replace("im1", "im2").Replace("mt1", "mt2")
            .Replace(" > ", " < ");
        var assembly = Compile("module NestedContracts NestedContracts block\n" + Indent(positive)
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
            §M{NestedContracts}
              §CL{Outer:pub}
            """ + "\n" + Indent(Indent(PositiveInterface)) + "\n" + Indent(Indent($$"""
            §CL{Inner:pub}
              §IMPL{IPositive}
              §MT{{{method}}:pub} (i32:value) -> i32
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
            module NestedContracts NestedContracts block
              interface IValue
                func Get (int:x) → int
                  requires x > 0
              class Outer public
                class Inner public
                  implements IValue
                  interface IValue
                    func Get (int:x) → int
                      requires x < 0
                  func Get public (int:value) → int
                    !
                    return value
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
            module NestedContracts NestedContracts block
              class Base public
                interface IPositive
                  func Get (int:x) → int
                    requires x > 0
                    => result > 0
              class Outer public
                extends Base
                class Inner public
                  implements IPositive
                  func Get public (int:value) → int
                    !
                    return value == 13 ? -1 : value
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
            module NestedContracts NestedContracts block
              class Outer<T> public
                interface IPositive
                  func Get (int:x) → int
                    requires x > 0
                    => result > 0
                interface IValue
                  func Check (object:x) → object
                    requires x is T
                    => result is T
              class Inner public
                implements Outer<int>.IPositive
                func Get public (int:value) → int
                  !
                  return value == 13 ? -1 : value
              class Typed public
                implements Outer<int>.IValue
                func Check public (object:value) → object
                  !
                  return value
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
            class Outer public
              interface IValue
                func Get (int:x) → int
                  requires x > 0
                  => result > 0
              class Inner public
                implements IValue
                func Get public (int:x) → int
                  !
                  return x
            """;
        var negative = positive.Replace("class Outer public", "class Outer<T> public")
            .Replace("c2", "c4").Replace("i1", "i2")
            .Replace("im1", "im2").Replace("mt1", "mt2").Replace(" > ", " < ");
        var assembly = Compile("module NestedContracts NestedContracts block\n" + Indent(positive)
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
        foreach (var contracts in new[] { "requires x is U", "=> result is U", "requires x is U\n=> result is U" })
        foreach (var outerParameter in new[] { "T", "V" })
        {
            var source = $$"""
                module NestedContracts NestedContracts block
                  class Outer<{{outerParameter}}> public
                    interface IValue<U>
                      func Check (object:x) → object
                {{Indent(Indent(Indent(Indent(contracts))))}}
                  class Impl<T> public
                    implements Outer<int>.IValue<T>
                    func Check public (object:value) → object
                      !
                      return value
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
        foreach (var contracts in new[] { "requires x is Marker", "=> result is Marker", "requires x is Marker\n=> result is Marker" })
        foreach (var generic in new[] { false, true })
        {
            var source = $$"""
                module NestedContracts NestedContracts block
                  class Outer public
                    class Marker public
                      func Tag public () → int
                        !
                        return 1
                    interface IValue
                      func Check (object:x) → object
                {{Indent(Indent(Indent(Indent(contracts))))}}
                  class Marker public
                    func Tag public () → int
                      !
                      return 2
                  class Impl public
                    implements Outer.IValue
                    func Check public (object:value) → object
                      !
                      return value
                """;
            if (generic)
            {
                source = source.Replace("class Outer public", "class Outer<T> public")
                    .Replace("    class Marker public", "    class Marker<T> public")
                    .Replace("Outer.IValue", "Outer<int>.IValue")
                    .Replace(" is Marker", " is Marker<string>");
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
        foreach (var contracts in new[] { "requires x is Box<Marker>", "=> result is Box<Marker>", "requires x is Box<Marker>\n=> result is Box<Marker>" })
        {
            var source = $$"""
                module NestedContracts NestedContracts block
                  class Outer public
                    class Marker public
                      func Tag public () → int
                        !
                        return 1
                    class Box<T> public
                      func Tag public () → int
                        !
                        return 2
                    interface IValue
                      func Check (object:x) → object
                {{Indent(Indent(Indent(Indent(contracts))))}}
                  class Marker public
                    func Tag public () → int
                      !
                      return 3
                  class Impl public
                    implements Outer.IValue
                    func Check public (object:value) → object
                      !
                      return value
                """;
            var assembly = Compile(source, verify);
            var box = assembly.GetType("NestedContracts.Outer+Box`1")!;
            var valid = Activator.CreateInstance(box.MakeGenericType(assembly.GetType("NestedContracts.Outer+Marker")!));
            var invalid = Activator.CreateInstance(box.MakeGenericType(assembly.GetType("NestedContracts.Marker")!));
            AssertObjectGuard(assembly.GetType("NestedContracts.Impl")!, valid, invalid);

            var headerScoped = source.Replace("class Outer public", "class Outer<V> public")
                .Replace("Box<Marker>", "Box<V>")
                .Replace("implements Outer.IValue", "implements Outer<Marker>.IValue\n    class Marker public\n      func Tag public () → int\n        !\n        return 4");
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
        foreach (var contracts in new[] { "requires x is Box<T>", "=> result is Box<T>", "requires x is Box<T>\n=> result is Box<T>" })
        foreach (var parameter in new[] { "T", "W" })
        {
            var source = $$"""
                module NestedContracts NestedContracts block
                  class Outer<T> public
                    class Box<U> public
                      func Tag public () → int
                        !
                        return 1
                    interface IValue
                      func Check<{{parameter}}> (object:x) → object
                {{Indent(Indent(Indent(Indent(contracts.Replace("Box<T>", $"Box<{parameter}>")))))}}
                  class Impl public
                    implements Outer<int>.IValue
                    func Check<V> public (object:value) → object
                      !
                      return value
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
        foreach (var contracts in new[] { "requires x != null", "=> result != null", "requires x != null\n=> result != null" })
        foreach (var parameterType in new[] { "Marker", "object" })
        {
            var declarations = """
                class Marker public
                  func Tag public () → int
                    !
                    return 1
                """;
            if (path != "base")
            {
                declarations += "\n" + """
                    interface IValue<T>
                      func Check (T:x) → T
                    """ + "\n" + Indent(Indent(contracts));
            }
            if (path != "interface")
            {
                declarations += "\n" + "class Base<T> public abstract\n"
                    + (path == "interface-base" ? "  implements IValue<T>\n" : "")
                    + "  func Check public abstract (T:x) → T\n    !"
                    + (path == "base" ? "\n" + Indent(Indent(contracts)) : "");
            }
            declarations += "\n" + $$"""
                class Impl public
                  {{(path == "interface" ? $"implements IValue<{parameterType}>" : $"extends Base<{parameterType}>")}}
                  func Check public{{(path == "interface" ? "" : " override")}} ({{parameterType}}:value) → {{parameterType}}
                    !
                    return value
                """;
            for (var level = depth - 1; level >= 0; level--)
                declarations = $"class Outer{level} public\n" + Indent(declarations);
            var assembly = Compile("module NestedContracts NestedContracts block\n" + Indent(declarations), verify);
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
            module NestedContracts NestedContracts block
              interface IValue<U>
                func Check<T> (U:x) → U
                  requires x != null
                  => result != null
              class Impl<T> public
                implements IValue<T>
                func Check<V> public (T:value) → T
                  !
                  return value
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
        foreach (var shape in new[] { "[T]", "T[]", "(T,i32)", "(T[,],i32)", "?T",
                     "List<T[]>[,]", "List<T>[,]", "List<T[]>[]" })
        {
            var predicate = shape.StartsWith('(') ? "x.Item1 != null" : "x != null";
            var contracts = string.Join("\n", new[]
            {
                mode != "post" ? "requires " + predicate : null,
                mode != "pre" ? "=> " + predicate.Replace("x", "result") : null
            }.Where(line => line != null));
            var surfaceShape = TypeMapper.ToSurfaceType(shape);
            var declarations = "";
            if (path != "base")
            {
                declarations = $"interface IValue\n  func Check<T> ({surfaceShape}:x) → {surfaceShape}\n    where T : class\n"
                    + Indent(Indent(contracts));
            }
            if (path != "interface")
            {
                declarations += "\nclass Base public abstract\n"
                    + (path == "interface-base" ? "  implements IValue\n" : "")
                    + $"  func Check<T> public abstract ({surfaceShape}:x) → {surfaceShape}\n    where T : class\n    !"
                    + (path == "base" ? "\n" + Indent(Indent(contracts)) : "");
            }
            var implementationShape = surfaceShape.Replace("T", "U");
            declarations += "\n" + $$"""
                class Impl public
                  {{(path == "interface" ? "implements IValue" : "extends Base")}}
                  func Check<U> public{{(path == "interface" ? "" : " override")}} ({{implementationShape}}:value) → {{implementationShape}}
                    where U : class
                    !
                    return value
                """;
            for (var level = depth - 1; level >= 0; level--)
                declarations = $"class Outer{level} public\n" + Indent(declarations);
            var assembly = Compile("module NestedContracts NestedContracts block\n" + Indent(declarations), verify);
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
                "List<T[]>[,]" => new List<string[]>[1, 1],
                "List<T>[,]" => new List<string>[1, 1],
                "List<T[]>[]" => new List<string[]>[1],
                _ => new[] { "valid" }
            };
            object? invalid = shape switch
            {
                "(T,i32)" => ((string?)null, 1),
                "(T[,],i32)" => ((string[,]?)null, 1),
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
                module NestedContracts {{(moduleName == "_global" ? "" : moduleName + " block")}}
                  class Outer public
                    interface IValue
                      func Check (object:x) → object
                        requires x != null
                        => result != null
                    class Impl public
                      implements {{(global ? "global::" : "")}}{{namespacePrefix}}Outer.IValue
                      func Check public (object:value) → object
                        !
                        return value
                """;
            var assembly = Compile(source, verify);
            AssertObjectGuard(assembly.GetType(namespacePrefix + "Outer+Impl")!, "valid", null);
        }

        const string shadowed = """
            module Scope Scope block
              class Outer public
                interface IValue
                  func Get (int:x) → int
                    requires x > 0
              class Container public
                class Scope public
                  class Outer public
                    interface IValue
                      func Get (int:x) → int
                        requires x < 0
                class Relative public
                  implements Scope.Outer.IValue
                  func Get public (int:x) → int
                    !
                    return x
                class Absolute public
                  implements global::Scope.Outer.IValue
                  func Get public (int:x) → int
                    !
                    return x
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
        foreach (var contract in new[] { "requires x > 0", "=> result > 0" })
        {
            var source = $$"""
                module NestedContracts N.Deep.Leaf block
                  class Outer public
                    interface IValue
                      func Get (int:x) → int
                        {{contract}}
                    class Impl public
                      implements {{reference}}
                      func Get public (int:x) → int
                        !
                        return x
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
        foreach (var contract in new[] { "requires x > Limits.Min", "=> result > Limits.Min" })
        {
            var implementation = """
                class Impl public
                  implements Outer.IValue
                  func Get public (int:value) → int
                    !
                    return value
                """;
            var typeName = "Scope.";
            for (var level = 0; level < depth; level++)
            {
                implementation = $"class Container{level} public\n" + Indent(implementation);
                typeName = "Scope." + $"Container{level}+" + typeName["Scope.".Length..];
            }
            var source = $$"""
                module Scope Scope block
                  class Outer public
                    class Limits public
                      field int:Min public static = 0
                    interface IValue
                      func Get (int:x) → int
                        {{contract}}
                  class Limits public
                    field int:Min public static = -10
                {{Indent(implementation)}}
                """;
            AssertGuards(Compile(source, verify).GetType(typeName + "Impl")!, 1, -1, -11);
        }

        foreach (var parameter in new[] { "Limits", "value" })
        {
            var source = $$"""
                module Scope Scope block
                  class Outer public
                    class Limits public
                      field int:Min public static = -10
                    interface IValue
                      func Check (Payload:Limits) → int
                        requires Limits.Min > 0
                  class Payload public
                    field int:Min public = 0
                  class Impl public
                    implements Outer.IValue
                    func Check public (Payload:{{parameter}}) → int
                      !
                      return {{parameter}}.Min
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
    public void ConditionalAndImplicationPatternReceivers_RemainValuesInInheritedGuards(bool verify)
    {
        foreach (var predicate in new[]
                 {
                     "x is Payload Limits ? Limits.Min > 0 : false",
                     "!(x is Payload Limits) ? false : Limits.Min > 0",
                     "!(x is Payload Limits) || Limits.Min > 0"
                 })
        foreach (var post in new[] { false, true })
        {
            var contract = post ? "=> " + predicate.Replace("x is", "result is") : "requires " + predicate;
            var source = $$"""
                module Scope Scope block
                  class Outer public
                    class Limits public
                      field int:Min public static = 10
                    interface IValue
                      func Check (object:x) → object
                        {{contract}}
                  class Payload public
                    field int:Min public = 0
                  class Impl public
                    implements Outer.IValue
                    func Check public (object:value) → object
                      !
                      return value
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
        foreach (var contract in new[] { "requires Limits.Min > 0", "=> Limits.Min > 0" })
        {
            var source = $$"""
                module Scope Scope block
                  class Outer public
                    class Limits public
                      field int:Min public static = 10
                    class Storage public
                      field Payload:Limits public
                    class Base public
                      {{(inheritedField ? "extends Storage" : "field Payload:Limits public")}}
                      func Get public virtual (int:x) → int
                        !
                        {{contract}}
                        return x
                  class Payload public
                    field int:Min public = 0
                  class Container public
                    class Impl public
                      extends Outer.Base
                      func Get public override (int:value) → int
                        !
                        return value
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
        foreach (var contract in new[] { "requires x > Limits.Min", "=> result > Limits.Min" })
        {
            const string limits = """
                class Limits public
                  field int:Min public static = 0
                """;
            var declarations = inheritedType
                ? "class Storage public\n" + Indent(limits) + "\nclass Base public\n  extends Storage"
                : "class Base public\n" + Indent(limits);
            declarations += "\n" + Indent($$"""
                func Get public virtual (int:x) → int
                  !
                  {{contract}}
                  return x
                """);
            var source = $$"""
                module Scope Scope block
                  class Outer public
                {{(outerField ? "    field int:Limits public static = 10\n" : "")}}{{Indent(Indent(declarations))}}
                  class Container public
                    class Impl public
                      extends Outer.Base
                      class Limits public
                        field int:Min public static = -10
                      func Get public override (int:value) → int
                        !
                        return value
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
        foreach (var inheritedShadow in new[] { "none", "parameter", "private-type" })
        foreach (var nestedName in new[] { "Limits", "Other" })
        foreach (var contract in new[] { "requires x > Limits.Min", "=> result > Limits.Min" })
        {
            var source = $$"""
                module Scope Scope block
                  class Limits public
                    field int:Min public static = -10
                  class Payload public
                    field int:Min public = 0
                  class Outer public
                    field Payload:Limits public static
                    class Storage<{{(inheritedShadow == "parameter" ? nestedName : "T")}}> public
                      func Tag public () → int
                        !
                        return 1
                {{(inheritedShadow == "private-type" ? $"      class {nestedName} private\n        field int:Min public static = -20" : "")}}
                    class Base public
                      extends Storage<int>
                      class {{nestedName}}<T> public
                        func Tag public () → int
                          !
                          return 1
                      func Get public virtual (int:x) → int
                        !
                        {{contract}}
                        return x
                    class Impl public
                      extends Base
                      func Get public override (int:value) → int
                        !
                        return value
                  class Container public
                    class Impl public
                      extends Outer.Base
                      func Get public override (int:value) → int
                        !
                        return value
                """;
            var assembly = Compile(source, verify);
            assembly.GetType("Scope.Outer")!.GetField("Limits")!.SetValue(null,
                Activator.CreateInstance(assembly.GetType("Scope.Payload")!));
            AssertGuards(assembly.GetType("Scope.Outer+Base")!, 1, -1, -11);
            AssertGuards(assembly.GetType("Scope.Outer+Impl")!, 1, -1, -11);
            AssertGuards(assembly.GetType("Scope.Container+Impl")!, 1, -1, -11);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InaccessibleInheritedTypes_DoNotHideLexicalTypes(bool verify)
    {
        foreach (var privateName in new[] { "Limits", "Other" })
        foreach (var contract in new[] { "requires x > Limits.Min", "=> result > Limits.Min" })
        {
            var source = $$"""
                module Scope Scope block
                  class Limits public
                    field int:Min public static = 0
                  class Storage public
                    class {{privateName}} private
                      field int:Min public static = -10
                  class Outer public
                    class Base public
                      extends Storage
                      func Get public virtual (int:x) → int
                        !
                        {{contract}}
                        return x
                    class Impl public
                      extends Base
                      func Get public override (int:value) → int
                        !
                        return value
                """;
            var assembly = Compile(source, verify);
            AssertGuards(assembly.GetType("Scope.Outer+Base")!, 1, -1);
            AssertGuards(assembly.GetType("Scope.Outer+Impl")!, 1, -1);
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("N.Deep", false)]
    [InlineData("N.Deep", true)]
    public void NativeNamespaceIdentity_IsIndependentOfTheModuleLabel(string ns, bool verify)
    {
        var prefix = ns.Length == 0 ? "" : ns + ".";
        var source = $$"""
            module Wrapper{{(ns.Length == 0 ? "" : " " + ns + " block")}}
              class Outer public
                interface IValue
                  func Get (int:x) → int
                    requires x > 0
                    => result > 0
                class Impl public
                  implements global::{{prefix}}Outer.IValue
                  func Get public (int:value) → int
                    !
                    return value == 13 ? -1 : value
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var implementation = Assert.Single(Assert.Single(module.Classes).NestedClasses);
        var method = Assert.Single(implementation.Methods);
        Assert.NotNull(method.Effects);
        Assert.Empty(method.Effects.Effects);
        Assert.Null(method.Output!.Row);
        foreach (var input in new[] { source, new CalorEmitter().Emit(module) })
        {
            var assembly = Compile(input, verify);
            var type = assembly.GetType(prefix + "Outer+Impl")!;
            Assert.Equal(assembly.GetType(prefix + "Outer+IValue"), Assert.Single(type.GetInterfaces()));
            AssertGuards(type, 1, -1, 13);
            Assert.Null(assembly.GetType("Wrapper.Outer"));
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

    private const string NativePositiveInterface = """
        interface IPositive
          func Get (int:x) → int
            requires x > 0
            => result > 0
        """;

    private const string PositiveInterface = """
        §IFACE{IPositive}
          §MT{Get} (i32:x) -> i32
            §Q (> x INT:0)
            §S (> result INT:0)
        """;
}
