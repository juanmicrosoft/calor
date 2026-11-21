using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.30 triage (g1): an implicit static initializer has no row surface, so its effects must
/// fit the intrinsic contract (<c>mut</c>, <c>alloc</c>) or the type is Calor0422 and the
/// converter preserves the whole file. Three bound shapes were reported as Unknown although
/// their effects are resolvable: source property accessors (object-initializer setters,
/// static property reads), field updates (<c>++Probe.N</c>, <c>x += v</c>) and the created
/// object's own fixed-dispatch metadata members (a collection initializer's <c>Add</c>, an
/// object initializer's setter). Each now resolves to what the callee does, and an effect
/// beyond the contract is still rejected.
/// </summary>
public class StaticInitializerResolutionTests
{
    private static IReadOnlyList<Diagnostic> Errors(string source)
        => Program.Compile(source, "init.calr").Diagnostics.Errors.ToList();

    private static void AssertClean(string source)
    {
        var errors = Errors(source);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    private static void AssertRejected(string source, string effect)
    {
        var error = Assert.Single(Errors(source), e => e.Code == DiagnosticCode.ConstructorEffectContractUnavailable);
        Assert.Contains("implicit static initializer 'Holder..cctor'", error.Message);
        Assert.Contains($"'{effect}'", error.Message);
    }

    private static string Point(string setterBody) => $$"""
        module Init
          class Point
            field string:Log static = ""
            field int:_x private
            property int:X
              get public
                return _x
              set public
                {{setterBody}}
                set _x value
            property string:Name = ""
              get public
              set public
        """;

    [Fact]
    public void ObjectInitializerSetters_ResolveToTheSourceAccessors()
    {
        AssertClean(Point("set Log (Log + \"x\")") + """

              class Holder
                field Point:Shared static = new Point() { X = 1, Name = "s" }
            """);
    }

    [Fact]
    public void AnEffectfulSourceSetter_IsStillChargedToTheInitializer()
    {
        AssertRejected(Point("System.Console.WriteLine(value)") + """

              class Holder
                field Point:Shared static = new Point() { X = 1 }
            """, "cw");
    }

    [Fact]
    public void StaticPropertyReads_ResolveToTheSourceGetter()
    {
        AssertClean("""
            module Init
              class Config
                property int:Size static = 3
                  get public
              class Holder
                field int:Doubled static = (Config.Size * 2)
            """);
    }

    [Theory]
    [InlineData("private static int _value = ++Counter.N;")]
    [InlineData("private static int _value = Counter.N++;")]
    [InlineData("private static int _value = Counter.N += 2;")]
    public void FieldUpdates_AreMutation(string field)
    {
        // Native Calor has no expression-position update, so the converter keeps this field
        // as C# member text; the type's initializer is still checked from its bound operations.
        var conversion = new Migration.CSharpToCalorConverter().Convert($$"""
            public static class Counter { public static int N = 4; }
            public class Holder
            {
                {{field}}
                public static int Read() => _value;
            }
            """);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.DoesNotContain(conversion.Losses, loss => loss.Feature == "whole-file-preservation");
        Assert.Contains(conversion.Losses, loss => loss.Feature == "field");
        AssertClean(conversion.CalorSource!);
    }

    [Fact]
    public void NativeCollectionInitializer_UsesTheCreatedObjectsAddContract()
    {
        AssertClean("""
            module Init
              using System.Collections.Generic
              class Holder
                field HashSet<string>:Names static = new HashSet<string>() { "a", "b" }
            """);
    }

    [Theory]
    [InlineData("set Log (Log + \"x\")", null)]
    [InlineData("Say()", "cw")]
    public void StaticDelegateInitializers_AllocateAndChargeTheirTarget(string body, string? rejected)
    {
        // Like a native lambda, the target's effects are charged to the creator as if it ran.
        var source = $$"""
            module Init
              class Holder
                field string:Log static = ""
                field System.Action:Hook static = () =>
                  {{body}}
                func Say public static () → void
                  ! cw
                  System.Console.WriteLine("x")
            """;
        if (rejected == null)
            AssertClean(source);
        else
            AssertRejected(source, rejected);
    }

    [Fact]
    public void NativeMetadataCallOutsideConstruction_StaysUnknown()
    {
        // Only a construction member (the created object's own setter or Add) uses a metadata
        // contract; any other native metadata call in an initializer remains unproved.
        AssertRejected("""
            module Init
              class Holder
                field object:Seed static = new object()
                field string:Text static = Seed.ToString()
            """, "unknown");
    }
}
