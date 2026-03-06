using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests verifying that C# 12 primary constructors are correctly converted
/// to Calor (with synthesized fields and constructor) and round-trip cleanly.
/// </summary>
public class PrimaryConstructorTests
{
    private readonly CSharpToCalorConverter _converter = new();

    [Fact]
    public void SimplePrimaryConstructor_SynthesizesFieldsAndConstructor()
    {
        var csharp = """
            public class Temperature(double degrees, string unit)
            {
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, FormatIssues(result));

        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Equal("Temperature", cls.Name);

        // Primary ctor params become private readonly fields
        Assert.Equal(2, cls.Fields.Count);
        Assert.Contains(cls.Fields, f => f.Name == "degrees" && f.TypeName == "f64");
        Assert.Contains(cls.Fields, f => f.Name == "unit" && f.TypeName == "str");

        // A constructor is synthesized
        var ctor = Assert.Single(cls.Constructors);
        Assert.Equal(2, ctor.Parameters.Count);
        Assert.Equal(2, ctor.Body.Count); // two assignment statements
    }

    [Fact]
    public void PrimaryConstructor_RoundTrip_EmitsValidCSharp()
    {
        var csharp = """
            public class Temperature(double degrees, string unit)
            {
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, FormatIssues(result));

        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(result.Ast!);

        // Should contain field declarations
        Assert.Contains("private readonly double degrees;", regenerated);
        Assert.Contains("private readonly string unit;", regenerated);

        // Should contain a constructor with parameters
        Assert.Contains("Temperature(double degrees, string unit)", regenerated);

        // Should contain field assignments
        Assert.Contains("this.degrees = degrees;", regenerated);
        Assert.Contains("this.unit = unit;", regenerated);
    }

    [Fact]
    public void PrimaryConstructor_WithProperties_ReferencingParams()
    {
        var csharp = """
            public class Temperature(double degrees, string unit)
            {
                public double Degrees => degrees;
                public string Unit => unit;
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, FormatIssues(result));

        var cls = Assert.Single(result.Ast!.Classes);

        // Fields synthesized for primary ctor params
        Assert.Equal(2, cls.Fields.Count);

        // Explicit properties exist
        Assert.Equal(2, cls.Properties.Count);
        Assert.Contains(cls.Properties, p => p.Name == "Degrees");
        Assert.Contains(cls.Properties, p => p.Name == "Unit");

        // Constructor still synthesized
        Assert.Single(cls.Constructors);

        // Round-trip
        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(result.Ast!);
        Assert.Contains("Temperature(double degrees, string unit)", regenerated);
        Assert.Contains("this.degrees = degrees;", regenerated);
    }

    [Fact]
    public void PrimaryConstructor_WithBaseClass_SynthesizesBaseCall()
    {
        var csharp = """
            public class Base
            {
                public Base(int x) { }
            }

            public class Derived(int x, string y) : Base(x)
            {
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, FormatIssues(result));

        // Find the Derived class
        var derived = result.Ast!.Classes.First(c => c.Name == "Derived");
        var ctor = Assert.Single(derived.Constructors);

        // Should have base call initializer
        Assert.NotNull(ctor.Initializer);
        Assert.True(ctor.Initializer.IsBaseCall);

        // Should also have field assignments in body
        Assert.True(ctor.Body.Count > 0);

        // Round-trip
        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(result.Ast!);
        Assert.Contains("Derived(int x, string y)", regenerated);
        Assert.Contains(": base(x)", regenerated);
    }

    [Fact]
    public void PrimaryConstructor_ExplicitMemberSkipped()
    {
        var csharp = """
            public class Foo(int x, string y)
            {
                private readonly int x = x;
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, FormatIssues(result));

        var cls = Assert.Single(result.Ast!.Classes);

        // 'x' is an explicit member, so only 'y' should be synthesized as a field
        // plus the explicit 'x' field
        Assert.Equal(2, cls.Fields.Count);

        // Constructor should only assign 'y' (the synthesized field)
        var ctor = Assert.Single(cls.Constructors);
        Assert.Single(ctor.Body); // only y assignment
    }

    [Fact]
    public void PrimaryConstructor_WithExplicitCtor_NoConflict()
    {
        var csharp = """
            public class Bar(int x)
            {
                public int Value => x;

                public Bar() : this(0) { }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, FormatIssues(result));

        var cls = Assert.Single(result.Ast!.Classes);

        // Should have the synthesized field for 'x'
        Assert.Contains(cls.Fields, f => f.Name == "x");

        // Should have two constructors: synthesized primary + explicit
        Assert.Equal(2, cls.Constructors.Count);
    }

    [Fact]
    public void Record_PrimaryConstructor_ProducesProperties()
    {
        var csharp = """
            public record Person(string Name, int Age);
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, FormatIssues(result));

        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Equal("Person", cls.Name);

        // Record primary ctor params become public properties (not fields)
        Assert.Empty(cls.Fields);
        Assert.Equal(2, cls.Properties.Count);
        Assert.Contains(cls.Properties, p => p.Name == "Name");
        Assert.Contains(cls.Properties, p => p.Name == "Age");
    }

    [Fact]
    public void Struct_PrimaryConstructor_SynthesizesFieldsAndCtor()
    {
        var csharp = """
            public struct Point(double x, double y)
            {
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, FormatIssues(result));

        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Equal("Point", cls.Name);

        // Fields synthesized
        Assert.Equal(2, cls.Fields.Count);
        Assert.Contains(cls.Fields, f => f.Name == "x");
        Assert.Contains(cls.Fields, f => f.Name == "y");

        // Constructor synthesized with assignments
        var ctor = Assert.Single(cls.Constructors);
        Assert.Equal(2, ctor.Parameters.Count);
        Assert.Equal(2, ctor.Body.Count);

        // Round-trip
        var emitter = new CSharpEmitter();
        var regenerated = emitter.Emit(result.Ast!);
        Assert.Contains("Point(double x, double y)", regenerated);
        Assert.Contains("this.x = x;", regenerated);
        Assert.Contains("this.y = y;", regenerated);
    }

    private static string FormatIssues(ConversionResult result)
    {
        if (result.Issues.Count > 0)
            return string.Join("\n", result.Issues.Select(i => i.Message));
        return "Conversion failed with no specific error message";
    }
}
