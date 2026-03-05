using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

public class PreprocessorStripperTests
{
    [Fact]
    public void Strip_SimpleIfElse_KeepsFirstBranch()
    {
        var source = """
            using System;
            #if NET6_0_OR_GREATER
            using System.Collections.Generic;
            #else
            using System.Collections;
            #endif
            class Foo { }
            """;
        var result = PreprocessorStripper.Strip(source);
        Assert.Contains("using System.Collections.Generic;", result);
        Assert.DoesNotContain("using System.Collections;", result);
        Assert.DoesNotContain("#if", result);
    }

    [Fact]
    public void Strip_NestedIf_HandlesCorrectly()
    {
        var source = """
            #if FEATURE_A
            #if FEATURE_B
            int x = 1;
            #else
            int x = 2;
            #endif
            int y = 3;
            #else
            int z = 4;
            #endif
            """;
        var result = PreprocessorStripper.Strip(source);
        Assert.Contains("int x = 1;", result);
        Assert.Contains("int y = 3;", result);
        Assert.DoesNotContain("int x = 2;", result);
        Assert.DoesNotContain("int z = 4;", result);
    }

    [Fact]
    public void Strip_Region_Removed()
    {
        var source = "#region Methods\nvoid Foo() {}\n#endregion";
        var result = PreprocessorStripper.Strip(source);
        Assert.Contains("void Foo()", result);
        Assert.DoesNotContain("#region", result);
    }

    [Fact]
    public void Strip_NullableDirective_Removed()
    {
        var source = "#nullable enable\nclass Foo {}\n#nullable restore";
        var result = PreprocessorStripper.Strip(source);
        Assert.DoesNotContain("#nullable", result);
        Assert.Contains("class Foo {}", result);
    }

    [Fact]
    public void Strip_PragmaDirective_Removed()
    {
        var source = "#pragma warning disable CS0618\nint x = 1;\n#pragma warning restore CS0618";
        var result = PreprocessorStripper.Strip(source);
        Assert.DoesNotContain("#pragma", result);
        Assert.Contains("int x = 1;", result);
    }

    [Fact]
    public void Strip_NoDirectives_PreservesSource()
    {
        var source = "using System;\nclass Foo\n{\n    int x = 1;\n}";
        var result = PreprocessorStripper.Strip(source);
        Assert.Equal(source, result);
    }

    [Fact]
    public void Strip_ElifBranch_Skipped()
    {
        var source = "#if A\nint a = 1;\n#elif B\nint b = 2;\n#else\nint c = 3;\n#endif";
        var result = PreprocessorStripper.Strip(source);
        Assert.Contains("int a = 1;", result);
        Assert.DoesNotContain("int b = 2;", result);
        Assert.DoesNotContain("int c = 3;", result);
    }

    [Fact]
    public void Strip_LineDirective_Removed()
    {
        var source = "#line 100 \"foo.cs\"\nclass Bar {}";
        var result = PreprocessorStripper.Strip(source);
        Assert.DoesNotContain("#line", result);
        Assert.Contains("class Bar {}", result);
    }
}
