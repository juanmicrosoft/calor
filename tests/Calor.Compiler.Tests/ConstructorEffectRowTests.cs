using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.30: a constructor may declare an optional own effect row. Without one its body is held
/// to the intrinsic initialization contract (Calor0422); with one it may also perform the
/// declared effects. Callers of <c>new</c> are charged what the body does either way.
/// </summary>
public class ConstructorEffectRowTests
{
    private static string Box(string row, string body) => $$"""
        module Shop Shop block
          class Box
            field int:Item
            ctor (int:number)
        {{(row.Length == 0 ? "" : "      " + row + "\n")}}      {{body}}
              set Item number
          func Make public () → int
            ! cw, throw, alloc, mut
            return new Box(1).Item
        """;

    private static IReadOnlyList<string> Errors(string source)
        => Program.Compile(source, "shop.calr").Diagnostics.Errors.Select(error => error.Code).ToList();

    [Fact]
    public void WithoutARow_AnEffectBeyondInitialization_IsCalor0422WithTheRowHint()
    {
        var compiled = Program.Compile(Box("", "System.Console.WriteLine(\"made\")"), "shop.calr");

        var error = Assert.Single(compiled.Diagnostics.Errors, e => e.Code == DiagnosticCode.ConstructorEffectContractUnavailable);
        Assert.Contains("Declare them on the constructor", error.Message);
    }

    [Theory]
    [InlineData("! cw")]
    [InlineData("! unknown")]
    public void ADeclaredRow_AdmitsTheBodysEffects(string row)
    {
        Assert.Empty(Errors(Box(row, "System.Console.WriteLine(\"made\")")));
    }

    [Fact]
    public void ADeclaredRow_DoesNotAdmitAnUndeclaredEffect()
    {
        var errors = Errors(Box("! cw", "throw new System.InvalidOperationException()"));

        Assert.Contains(DiagnosticCode.ForbiddenEffect, errors);
        Assert.DoesNotContain(DiagnosticCode.ConstructorEffectContractUnavailable, errors);
    }

    [Fact]
    public void CallersOfNew_AreChargedTheConstructorsEffects()
    {
        // The caller declares only what the constructor does not: cw must still be required.
        var source = Box("! cw", "System.Console.WriteLine(\"made\")").Replace("! cw, throw, alloc, mut", "! alloc, mut");

        Assert.Contains(DiagnosticCode.ForbiddenEffect, Errors(source));
    }

    [Fact]
    public void Converter_DeclaresAConstructorRowInsteadOfPreservingTheFile()
    {
        const string csharp = """
            public static class Migrated
            {
                public static string Trace = "";
                public class Box
                {
                    public int Item;
                    public Box(int number)
                    {
                        Trace += "C";
                        if (number < 0) throw new System.InvalidOperationException();
                        Item = number;
                    }
                }
            }
            """;

        var conversion = new CSharpToCalorConverter().Convert(csharp, "Migrated.cs");

        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Matches(@"ctor \(int:number\)\r?\n\s+! throw\r?\n", conversion.CalorSource);
        Assert.False(Program.Compile(conversion.CalorSource!, "Migrated.calr").HasErrors);
    }
}
