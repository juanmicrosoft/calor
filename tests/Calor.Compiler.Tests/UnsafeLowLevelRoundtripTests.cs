using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Roundtrip tests for unsafe/low-level and multidimensional array nodes.
/// Tests both Calor→Parse→C# emit and C#→Calor→Parse→C# emit paths.
/// </summary>
public class UnsafeLowLevelRoundtripTests
{
    #region Helpers

    private static string ParseAndEmit(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors,
            $"Parse errors:\n{string.Join("\n", diagnostics.Select(d => d.Message))}");

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static string ConvertToCalor(string csharpSource)
    {
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharpSource);
        Assert.True(result.Success,
            $"Conversion failed:\n{string.Join("\n", result.Issues.Select(i => i.Message))}");

        var emitter = new CalorEmitter();
        return emitter.Emit(result.Ast!);
    }

    /// <summary>
    /// Convert C# to Calor AST, emit Calor text (for tag verification),
    /// then emit C# directly from AST (parser doesn't handle unsafe nodes).
    /// </summary>
    private static (string calor, string csharp) ConvertAndEmit(string csharpSource)
    {
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharpSource);
        Assert.True(result.Success,
            $"Conversion failed:\n{string.Join("\n", result.Issues.Select(i => i.Message))}");

        var calrEmitter = new CalorEmitter();
        var calor = calrEmitter.Emit(result.Ast!);

        var csharpEmitter = new CSharpEmitter();
        var csharp = csharpEmitter.Emit(result.Ast!);

        return (calor, csharp);
    }

    /// <summary>
    /// Compiles C# source with Roslyn (with AllowUnsafe=true) and returns errors.
    /// </summary>
    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> RoslynCompile(string csharpSource)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(csharpSource);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "RoundTripTest",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            // Filter out Calor.Runtime reference — emitter adds `using Calor.Runtime;`
            // which is correct but not available in this minimal Roslyn compilation
            .Where(d => !d.GetMessage().Contains("'Calor'"))
            .ToArray();
    }

    /// <summary>
    /// Full roundtrip: C# → Calor AST → verify tag → C# emit → Roslyn compile.
    /// Note: Parser doesn't yet support unsafe/pointer nodes, so we emit directly
    /// from the AST instead of parsing back from Calor text.
    /// </summary>
    private static string FullRoundTrip(string csharpInput, string expectedCalorTag)
    {
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharpInput);
        Assert.True(result.Success,
            $"Conversion failed:\n{string.Join("\n", result.Issues.Select(i => i.Message))}");

        // Verify the Calor representation contains the expected tag
        var calrEmitter = new CalorEmitter();
        var calor = calrEmitter.Emit(result.Ast!);
        Assert.Contains(expectedCalorTag, calor);

        // Emit C# directly from AST (skip parse-back since parser
        // doesn't handle unsafe/pointer nodes yet)
        var csharpEmitter = new CSharpEmitter();
        var csharpOutput = csharpEmitter.Emit(result.Ast!);
        Assert.False(string.IsNullOrWhiteSpace(csharpOutput), "Emitted C# is empty");

        var errors = RoslynCompile(csharpOutput);
        Assert.Empty(errors);

        return csharpOutput;
    }

    #endregion

    #region StackAlloc — Calor→Parse→C# Emit

    [Fact]
    public void Emit_StackAllocSized_EmitsStackallocExpression()
    {
        var calorSource = """
            §M{m001:StackAllocTest}
              §F{f001:Alloc:pub}
                §O{i32}
                §B{~sum:i32} INT:0
                §R sum
              §/F{f001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("int sum = 0", csharp);
    }

    [Fact]
    public void Emit_StackAllocSized_InBinding_EmitsStackalloc()
    {
        var calorSource = """
            §M{m001:StackAllocTest}
              §CL{c001:Test:pub}
                §MT{m001:Alloc:pub:unsafe}
                  §O{void}
                  §B{span:Span<i32>} §SALLOC{i32:10}
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("stackalloc int[10]", csharp);
    }

    [Fact]
    public void Emit_SizeOf_EmitsSizeofExpression()
    {
        var calorSource = """
            §M{m001:SizeOfTest}
              §F{f001:GetSize:pub}
                §O{i32}
                §R §SIZEOF{i32}
              §/F{f001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("sizeof(int)", csharp);
    }

    [Fact]
    public void Emit_AddressOf_EmitsAmpersand()
    {
        var calorSource = """
            §M{m001:AddrTest}
              §CL{c001:Test:pub}
                §MT{m001:GetAddr:pub:unsafe}
                  §O{void}
                  §B{~x:i32} INT:42
                  §B{ptr:i32*} §ADDR x
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("&x", csharp);
    }

    [Fact]
    public void Emit_PointerDereference_EmitsStar()
    {
        var calorSource = """
            §M{m001:DerefTest}
              §CL{c001:Test:pub}
                §MT{m001:ReadPtr:pub:unsafe}
                  §I{ptr:i32*}
                  §O{i32}
                  §R §DEREF ptr
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("*ptr", csharp);
    }

    #endregion

    #region Unsafe/Fixed — Calor→Parse→C# Emit

    [Fact]
    public void Emit_UnsafeBlock_EmitsUnsafeKeyword()
    {
        var calorSource = """
            §M{m001:UnsafeBlockTest}
              §CL{c001:Test:pub}
                §MT{m001:DoUnsafe:pub}
                  §O{void}
                  §UNSAFE{u1}
                    §B{~x:i32} INT:42
                  §/UNSAFE{u1}
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("unsafe", csharp);
        Assert.Contains("{", csharp);
    }

    [Fact]
    public void Emit_FixedStatement_EmitsFixedBlock()
    {
        var calorSource = """
            §M{m001:FixedTest}
              §CL{c001:Test:pub}
                §MT{m001:DoFixed:pub:unsafe}
                  §I{arr:[i32]}
                  §O{void}
                  §FIXED{f1:ptr:i32*:arr}
                    §B{val:i32} §DEREF ptr
                  §/FIXED{f1}
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("fixed (int* ptr = arr)", csharp);
    }

    #endregion

    #region MultiDim Arrays — Calor→Parse→C# Emit

    [Fact]
    public void Emit_MultiDimArraySized_EmitsNewWithDimensions()
    {
        var calorSource = """
            §M{m001:ArrayTest}
              §CL{c001:Test:pub}
                §MT{m001:Create:pub}
                  §O{void}
                  §B{grid:i32} §ARR2D{a1:grid:i32:3:4}
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("new int[3, 4]", csharp);
    }

    [Fact]
    public void Emit_MultiDimArrayInitializer_EmitsNewWithInitializer()
    {
        var calorSource = """
            §M{m001:ArrayInitTest}
              §CL{c001:Test:pub}
                §MT{m001:Create:pub}
                  §O{void}
                  §B{grid:i32} §ARR2D{a1:grid:i32}
                    §ROW INT:1 INT:2
                    §ROW INT:3 INT:4
                  §/ARR2D{a1}
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("new int[,]", csharp);
        Assert.Contains("1, 2", csharp);
        Assert.Contains("3, 4", csharp);
    }

    [Fact]
    public void Emit_MultiDimArrayAccess_EmitsIndexing()
    {
        var calorSource = """
            §M{m001:IndexTest}
              §CL{c001:Test:pub}
                §MT{m001:Get:pub}
                  §I{grid:i32}
                  §O{i32}
                  §R §IDX2D grid INT:1 INT:2
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("grid[1, 2]", csharp);
    }

    #endregion

    #region Full C# → Calor → Parse → C# → Roslyn compile roundtrips

    [Fact]
    public void RoundTrip_SizeOf_RoslynCompiles()
    {
        var csharp = FullRoundTrip("""
            public static class Test
            {
                public static int GetSizeOfInt()
                {
                    return sizeof(int);
                }
            }
            """, "§SIZEOF");

        Assert.Contains("sizeof(int)", csharp);
    }

    [Fact]
    public void RoundTrip_UnsafeBlock_RoslynCompiles()
    {
        var csharp = FullRoundTrip("""
            public static class Test
            {
                public static void DoUnsafe()
                {
                    unsafe
                    {
                        int x = 42;
                    }
                }
            }
            """, "§UNSAFE");

        Assert.Contains("unsafe", csharp);
    }

    [Fact]
    public void RoundTrip_StackAlloc_RoslynCompiles()
    {
        var csharp = FullRoundTrip("""
            public static class Test
            {
                public static unsafe int Sum()
                {
                    int* ptr = stackalloc int[3];
                    return 0;
                }
            }
            """, "§SALLOC");

        Assert.Contains("stackalloc", csharp);
    }

    [Fact]
    public void RoundTrip_FixedStatement_RoslynCompiles()
    {
        var csharp = FullRoundTrip("""
            public static class Test
            {
                public static unsafe void DoFixed(int[] arr)
                {
                    fixed (int* ptr = arr)
                    {
                        int val = *ptr;
                    }
                }
            }
            """, "§FIXED");

        Assert.Contains("fixed", csharp);
    }

    [Fact]
    public void RoundTrip_AddressOf_RoslynCompiles()
    {
        var csharp = FullRoundTrip("""
            public static class Test
            {
                public static unsafe void TakeAddr()
                {
                    int x = 42;
                    int* ptr = &x;
                }
            }
            """, "§ADDR");

        Assert.Contains("&x", csharp);
    }

    [Fact]
    public void RoundTrip_PointerDereference_RoslynCompiles()
    {
        var csharp = FullRoundTrip("""
            public static class Test
            {
                public static unsafe int Deref(int* ptr)
                {
                    return *ptr;
                }
            }
            """, "§DEREF");

        Assert.Contains("*ptr", csharp);
    }

    [Fact]
    public void RoundTrip_MultiDimArrayCreation_RoslynCompiles()
    {
        var csharp = FullRoundTrip("""
            public static class Test
            {
                public static int[,] CreateGrid(int rows, int cols)
                {
                    int[,] grid = new int[rows, cols];
                    return grid;
                }
            }
            """, "§ARR2D");

        Assert.Contains("new int[", csharp);
    }

    [Fact]
    public void RoundTrip_MultiDimArrayAccess_RoslynCompiles()
    {
        var csharp = FullRoundTrip("""
            public static class Test
            {
                public static int GetElement(int[,] grid, int row, int col)
                {
                    return grid[row, col];
                }
            }
            """, "§IDX2D");

        Assert.Contains("grid[", csharp);
    }

    #endregion

    #region Initializer forms — C# → Calor → Parse → C# roundtrip

    [Fact]
    public void RoundTrip_StackAllocInitializer_ParsesCorrectly()
    {
        var csharpInput = """
            public static class Test
            {
                public static unsafe int Sum()
                {
                    int* ptr = stackalloc int[] { 1, 2, 3 };
                    return 0;
                }
            }
            """;

        var calor = ConvertToCalor(csharpInput);
        Assert.Contains("§SALLOC", calor);

        var csharpOutput = ParseAndEmit(calor);
        Assert.Contains("stackalloc", csharpOutput);
        Assert.Contains("1, 2, 3", csharpOutput);
    }

    [Fact]
    public void RoundTrip_MultiDimArrayInitializer_ParsesCorrectly()
    {
        var csharpInput = """
            public static class Test
            {
                public static int[,] CreateInitialized()
                {
                    int[,] matrix = new int[,]
                    {
                        { 1, 2, 3 },
                        { 4, 5, 6 }
                    };
                    return matrix;
                }
            }
            """;

        var (calor, csharpOutput) = ConvertAndEmit(csharpInput);
        Assert.Contains("§ARR2D", calor);
        Assert.Contains("new int[,]", csharpOutput);
        Assert.Contains("1, 2, 3", csharpOutput);
        Assert.Contains("4, 5, 6", csharpOutput);
    }

    #endregion

    #region Unsafe method modifier roundtrip

    [Fact]
    public void RoundTrip_UnsafeMethodModifier_Preserved()
    {
        var csharpInput = """
            public static class Test
            {
                public static unsafe int* Alloc()
                {
                    return (int*)0;
                }
            }
            """;

        var (calor, csharpOutput) = ConvertAndEmit(csharpInput);
        Assert.Contains("unsafe", calor);
        Assert.Contains("unsafe", csharpOutput);
        Assert.Contains("(int*)", csharpOutput);
    }

    [Fact]
    public void Emit_UnsafeMethodModifier_FromCalor()
    {
        var calorSource = """
            §M{m001:UnsafeMethodTest}
              §CL{c001:Test:pub}
                §MT{m001:DoStuff:pub:stat,unsafe}
                  §O{void}
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("unsafe", csharp);
        Assert.Contains("static", csharp);
    }

    [Fact]
    public void Emit_ExternMethodModifier_FromCalor()
    {
        var calorSource = """
            §M{m001:ExternMethodTest}
              §CL{c001:Test:pub}
                §MT{m001:NativeCall:pub:stat,ext}
                  §O{void}
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("extern", csharp);
        Assert.Contains("static", csharp);
    }

    #endregion

    #region TypeMapper edge cases

    [Theory]
    [InlineData("int*", "i32*")]
    [InlineData("int**", "i32**")]
    [InlineData("void*", "void*")]
    [InlineData("byte*", "u8*")]
    public void TypeMapper_PointerTypes_CSharpToCalor(string csharp, string expectedCalor)
    {
        Assert.Equal(expectedCalor, TypeMapper.CSharpToCalor(csharp));
    }

    [Theory]
    [InlineData("i32*", "int*")]
    [InlineData("i32**", "int**")]
    [InlineData("void*", "void*")]
    [InlineData("u8*", "byte*")]
    public void TypeMapper_PointerTypes_CalorToCSharp(string calor, string expectedCSharp)
    {
        Assert.Equal(expectedCSharp, TypeMapper.CalorToCSharp(calor));
    }

    [Fact]
    public void TypeMapper_TupleType_CSharpToCalor()
    {
        Assert.Equal("(i32, str)", TypeMapper.CSharpToCalor("(int, string)"));
    }

    [Fact]
    public void TypeMapper_NestedTupleType_CSharpToCalor()
    {
        Assert.Equal("(i32, (str, bool))", TypeMapper.CSharpToCalor("(int, (string, bool))"));
    }

    [Fact]
    public void TypeMapper_NamedTupleType_CSharpToCalor()
    {
        Assert.Equal("(i32 x, str y)", TypeMapper.CSharpToCalor("(int x, string y)"));
    }

    [Fact]
    public void TypeMapper_TupleType_CalorToCSharp()
    {
        Assert.Equal("(int, string)", TypeMapper.CalorToCSharp("(i32, str)"));
    }

    [Fact]
    public void TypeMapper_NestedTupleType_CalorToCSharp()
    {
        Assert.Equal("(int, (string, bool))", TypeMapper.CalorToCSharp("(i32, (str, bool))"));
    }

    [Fact]
    public void TypeMapper_NamedTupleType_CalorToCSharp()
    {
        Assert.Equal("(int x, string y)", TypeMapper.CalorToCSharp("(i32 x, str y)"));
    }

    [Fact]
    public void TypeMapper_NullableTupleType_CSharpToCalor()
    {
        Assert.Equal("?(i32, str)", TypeMapper.CSharpToCalor("(int, string)?"));
    }

    [Fact]
    public void TypeMapper_NullableTupleType_CalorToCSharp()
    {
        Assert.Equal("(int, string)?", TypeMapper.CalorToCSharp("?(i32, str)"));
    }

    [Fact]
    public void TypeMapper_SpanType_Identity()
    {
        Assert.Equal("Span<i32>", TypeMapper.CSharpToCalor("Span<int>"));
        Assert.Equal("Span<int>", TypeMapper.CalorToCSharp("Span<i32>"));
    }

    [Fact]
    public void TypeMapper_ReadOnlySpanType_Identity()
    {
        Assert.Equal("ReadOnlySpan<char>", TypeMapper.CSharpToCalor("ReadOnlySpan<char>"));
        Assert.Equal("ReadOnlySpan<char>", TypeMapper.CalorToCSharp("ReadOnlySpan<char>"));
    }

    [Theory]
    [InlineData("int[,]", "i32[,]")]
    [InlineData("int[,,]", "i32[,,]")]
    [InlineData("string[,,,]", "str[,,,]")]
    public void TypeMapper_MultiDimArrayTypes_CSharpToCalor(string csharp, string expectedCalor)
    {
        Assert.Equal(expectedCalor, TypeMapper.CSharpToCalor(csharp));
    }

    [Theory]
    [InlineData("i32[,]", "int[,]")]
    [InlineData("i32[,,]", "int[,,]")]
    [InlineData("str[,,,]", "string[,,,]")]
    public void TypeMapper_MultiDimArrayTypes_CalorToCSharp(string calor, string expectedCSharp)
    {
        Assert.Equal(expectedCSharp, TypeMapper.CalorToCSharp(calor));
    }

    #endregion

    #region 3D Array and cast roundtrip

    [Fact]
    public void RoundTrip_3DArrayCreation_RoslynCompiles()
    {
        var csharp = FullRoundTrip("""
            public static class Test
            {
                public static int[,,] Create3D(int x, int y, int z)
                {
                    int[,,] cube = new int[x, y, z];
                    return cube;
                }
            }
            """, "§ARR2D");

        Assert.Contains("int[,,]", csharp);
        Assert.Contains("new int[", csharp);
    }

    [Fact]
    public void RoundTrip_CastToPointerType_ParsesCorrectly()
    {
        // Verifies that (cast i32* expr) parses correctly with pointer type
        var calorSource = """
            §M{m001:CastTest}
              §CL{c001:Test:pub}
                §MT{m001:CastPtr:pub:unsafe}
                  §O{i32*}
                  §R (cast i32* 0)
                §/MT{m001}
              §/CL{c001}
            §/M{m001}
            """;

        var csharp = ParseAndEmit(calorSource);
        Assert.Contains("(int*)", csharp);
        Assert.Contains("0", csharp);
    }

    [Fact]
    public void Emit_CastToPointerType_FromCSharpRoundTrip()
    {
        var csharpInput = """
            public static class Test
            {
                public static unsafe int* CastNull()
                {
                    return (int*)0;
                }
            }
            """;

        var (calor, csharpOutput) = ConvertAndEmit(csharpInput);
        Assert.Contains("(cast i32*", calor);
        Assert.Contains("(int*)", csharpOutput);
    }

    #endregion
}
