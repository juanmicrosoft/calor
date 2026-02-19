using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Formatting;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for readonly struct codegen and operator overload support.
/// </summary>
public class StructAndOperatorTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Issue 1: Struct Support

    [Fact]
    public void Migration_ReadonlyStruct_SetsIsStructAndIsReadOnly()
    {
        var csharp = """
            public readonly struct Point
            {
                public double X { get; }
                public double Y { get; }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.True(cls.IsStruct, "Should be marked as struct");
        Assert.True(cls.IsReadOnly, "Should be marked as readonly");
        Assert.False(cls.IsSealed, "Struct should not be marked as sealed");
    }

    [Fact]
    public void Migration_PlainStruct_SetsIsStructOnly()
    {
        var csharp = """
            public struct Foo
            {
                public int Value;
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.True(cls.IsStruct, "Should be marked as struct");
        Assert.False(cls.IsReadOnly, "Should not be marked as readonly");
        Assert.False(cls.IsSealed, "Struct should not be marked as sealed");
    }

    [Fact]
    public void CSharpEmitter_ReadonlyStruct_EmitsReadonlyStructKeyword()
    {
        var csharp = """
            public readonly struct Point
            {
                public double X { get; }
                public double Y { get; }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("readonly struct Point", output);
        Assert.DoesNotContain("sealed", output);
        Assert.DoesNotContain("class Point", output);
    }

    [Fact]
    public void CSharpEmitter_PlainStruct_EmitsStructKeyword()
    {
        var csharp = """
            public struct Foo
            {
                public int Value;
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("struct Foo", output);
        Assert.DoesNotContain("sealed", output);
        Assert.DoesNotContain("class Foo", output);
    }

    [Fact]
    public void CalorEmitter_Struct_EmitsStructModifier()
    {
        var csharp = """
            public struct Foo
            {
                public int Value;
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        Assert.Contains("struct", calor);
    }

    [Fact]
    public void CalorEmitter_ReadonlyStruct_EmitsReadonlyModifier()
    {
        var csharp = """
            public readonly struct Point
            {
                public double X { get; }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var calor = result.CalorSource!;
        Assert.Contains("struct", calor);
        Assert.Contains("readonly", calor);
    }

    [Fact]
    public void Parser_StructModifier_ParsesCorrectly()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Foo:struct}
            §FLD{i32:Value:pub}
            §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);
        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(compilationResult.Ast!.Classes);
        Assert.True(cls.IsStruct, "Parser should set IsStruct from 'struct' modifier");
        Assert.False(cls.IsReadOnly);
    }

    [Fact]
    public void Parser_ReadonlyStructModifier_ParsesCorrectly()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Point:struct,readonly}
            §FLD{f64:X:pub}
            §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);
        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(compilationResult.Ast!.Classes);
        Assert.True(cls.IsStruct, "Parser should set IsStruct");
        Assert.True(cls.IsReadOnly, "Parser should set IsReadOnly");
    }

    [Fact]
    public void Roundtrip_ReadonlyStruct_PreservesModifiers()
    {
        var csharp = """
            public readonly struct Point
            {
                public double X { get; }
                public double Y { get; }
            }
            """;

        // C# → Calor
        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Calor → parse → C#
        var compilationResult = Program.Compile(result.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip parse failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        var cls = Assert.Single(compilationResult.Ast!.Classes);
        Assert.True(cls.IsStruct, "Roundtrip should preserve IsStruct");
        Assert.True(cls.IsReadOnly, "Roundtrip should preserve IsReadOnly");

        // Generate C# from roundtripped AST
        var emitter = new CSharpEmitter();
        var output = emitter.Emit(compilationResult.Ast!);
        Assert.Contains("readonly struct Point", output);
    }

    #endregion

    #region Issue 2: Operator Overloads

    [Fact]
    public void Migration_OperatorPlus_ConvertsToOpAddition()
    {
        var csharp = """
            public struct Point
            {
                public double X;
                public double Y;
                public static Point operator +(Point a, Point b)
                {
                    return new Point();
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var opMethod = cls.Methods.FirstOrDefault(m => m.Name == "op_Addition");
        Assert.NotNull(opMethod);
        Assert.True(opMethod.IsStatic, "Operator should be static");
        Assert.Equal(Visibility.Public, opMethod.Visibility);
    }

    [Fact]
    public void Migration_OperatorEquality_ConvertsToOpEquality()
    {
        var csharp = """
            public struct Point
            {
                public double X;
                public static bool operator ==(Point a, Point b)
                {
                    return true;
                }
                public static bool operator !=(Point a, Point b)
                {
                    return false;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Contains(cls.Methods, m => m.Name == "op_Equality");
        Assert.Contains(cls.Methods, m => m.Name == "op_Inequality");
    }

    [Fact]
    public void Migration_ImplicitConversion_ConvertsToOpImplicit()
    {
        var csharp = """
            public struct Temperature
            {
                public double Value;
                public static implicit operator double(Temperature t)
                {
                    return t.Value;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var opMethod = cls.Methods.FirstOrDefault(m => m.Name == "op_Implicit");
        Assert.NotNull(opMethod);
        Assert.True(opMethod.IsStatic);
        Assert.Equal("f64", opMethod.Output!.TypeName);
    }

    [Fact]
    public void Migration_ExplicitConversion_ConvertsToOpExplicit()
    {
        var csharp = """
            public struct Celsius
            {
                public double Value;
                public static explicit operator int(Celsius c)
                {
                    return (int)c.Value;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var opMethod = cls.Methods.FirstOrDefault(m => m.Name == "op_Explicit");
        Assert.NotNull(opMethod);
        Assert.True(opMethod.IsStatic);
        Assert.Equal("i32", opMethod.Output!.TypeName);
    }

    [Fact]
    public void CSharpEmitter_OperatorPlus_EmitsOperatorSyntax()
    {
        var csharp = """
            public struct Point
            {
                public double X;
                public double Y;
                public static Point operator +(Point a, Point b)
                {
                    return new Point();
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("operator +", output);
        Assert.DoesNotContain("op_Addition", output);
    }

    [Fact]
    public void CSharpEmitter_OperatorEquality_EmitsOperatorSyntax()
    {
        var csharp = """
            public struct Point
            {
                public double X;
                public static bool operator ==(Point a, Point b)
                {
                    return true;
                }
                public static bool operator !=(Point a, Point b)
                {
                    return false;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("operator ==", output);
        Assert.Contains("operator !=", output);
    }

    [Fact]
    public void CSharpEmitter_ImplicitConversion_EmitsImplicitOperator()
    {
        var csharp = """
            public struct Temperature
            {
                public double Value;
                public static implicit operator double(Temperature t)
                {
                    return t.Value;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("implicit operator", output);
        Assert.DoesNotContain("op_Implicit", output);
    }

    [Fact]
    public void CSharpEmitter_ExplicitConversion_EmitsExplicitOperator()
    {
        var csharp = """
            public struct Celsius
            {
                public double Value;
                public static explicit operator int(Celsius c)
                {
                    return (int)c.Value;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("explicit operator", output);
        Assert.DoesNotContain("op_Explicit", output);
    }

    [Fact]
    public void Migration_OperatorsInClass_NotDropped()
    {
        var csharp = """
            public class Vector
            {
                public double X;
                public double Y;
                public static Vector operator +(Vector a, Vector b)
                {
                    return new Vector();
                }
                public static Vector operator -(Vector a, Vector b)
                {
                    return new Vector();
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Contains(cls.Methods, m => m.Name == "op_Addition");
        Assert.Contains(cls.Methods, m => m.Name == "op_Subtraction");
    }

    [Fact]
    public void Roundtrip_OperatorOverload_PreservesOperator()
    {
        var csharp = """
            public struct Point
            {
                public double X;
                public double Y;
                public static Point operator +(Point a, Point b)
                {
                    return new Point();
                }
            }
            """;

        // C# → Calor
        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        // Calor → parse → C#
        var compilationResult = Program.Compile(result.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip parse failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        // Should have the op_Addition method preserved
        var cls = Assert.Single(compilationResult.Ast!.Classes);
        Assert.Contains(cls.Methods, m => m.Name == "op_Addition");

        // Generate C# from roundtripped AST
        var emitter = new CSharpEmitter();
        var output = emitter.Emit(compilationResult.Ast!);
        Assert.Contains("operator +", output);
        Assert.Contains("struct Point", output);
    }

    #endregion

    #region Issue 2b: Unary Operators

    [Fact]
    public void Migration_UnaryNegation_ConvertsToOpUnaryNegation()
    {
        var csharp = """
            public struct Vector
            {
                public double X;
                public double Y;
                public static Vector operator -(Vector v)
                {
                    return new Vector();
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var opMethod = cls.Methods.FirstOrDefault(m => m.Name == "op_UnaryNegation");
        Assert.NotNull(opMethod);
        Assert.Single(opMethod.Parameters);
    }

    [Fact]
    public void Migration_UnaryPlus_ConvertsToOpUnaryPlus()
    {
        var csharp = """
            public struct Vector
            {
                public double X;
                public static Vector operator +(Vector v)
                {
                    return v;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        var opMethod = cls.Methods.FirstOrDefault(m => m.Name == "op_UnaryPlus");
        Assert.NotNull(opMethod);
        Assert.Single(opMethod.Parameters);
    }

    [Fact]
    public void Migration_BinaryAndUnaryMinus_DisambiguatesCorrectly()
    {
        var csharp = """
            public struct Vector
            {
                public double X;
                public static Vector operator -(Vector a, Vector b)
                {
                    return new Vector();
                }
                public static Vector operator -(Vector v)
                {
                    return new Vector();
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Contains(cls.Methods, m => m.Name == "op_Subtraction");
        Assert.Contains(cls.Methods, m => m.Name == "op_UnaryNegation");
    }

    [Fact]
    public void CSharpEmitter_UnaryNegation_EmitsOperatorSyntax()
    {
        var csharp = """
            public struct Vector
            {
                public double X;
                public static Vector operator -(Vector v)
                {
                    return new Vector();
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("operator -", output);
        Assert.DoesNotContain("op_UnaryNegation", output);
    }

    #endregion

    #region Issue 2c: Operators in Records

    [Fact]
    public void Migration_OperatorsInRecord_NotDropped()
    {
        var csharp = """
            public record Money(decimal Amount, string Currency)
            {
                public static Money operator +(Money a, Money b)
                {
                    return new Money(a.Amount + b.Amount, a.Currency);
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Contains(cls.Methods, m => m.Name == "op_Addition");
    }

    [Fact]
    public void Migration_ConversionInRecord_NotDropped()
    {
        var csharp = """
            public record Celsius(double Value)
            {
                public static implicit operator double(Celsius c)
                {
                    return c.Value;
                }
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.Contains(cls.Methods, m => m.Name == "op_Implicit");
    }

    #endregion

    #region Issue 1b: Partial Struct and Attributed Struct

    [Fact]
    public void Migration_PartialStruct_SetsIsPartial()
    {
        var csharp = """
            public partial struct Config
            {
                public int Value;
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.True(cls.IsStruct);
        Assert.True(cls.IsPartial, "Partial struct should preserve IsPartial");
    }

    [Fact]
    public void Migration_AttributedStruct_PreservesAttributes()
    {
        var csharp = """
            [Serializable]
            public struct Data
            {
                public int Value;
            }
            """;

        var result = _converter.Convert(csharp);

        Assert.True(result.Success, GetErrorMessage(result));
        var cls = Assert.Single(result.Ast!.Classes);
        Assert.True(cls.IsStruct);
        Assert.NotEmpty(cls.CSharpAttributes);
        Assert.Contains(cls.CSharpAttributes, a => a.Name == "Serializable");
    }

    #endregion

    #region Issue 2d: Operator Contract Emission

    [Fact]
    public void CSharpEmitter_OperatorWithPrecondition_EmitsContractCheck()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Vector:struct}
            §FLD{f64:X:pub}
            §FLD{f64:Y:pub}
            §MT{m1:op_Addition:pub:static}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
              §Q (>= a INT:0)
              §R (+ a b)
            §/MT{m1}
            §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);
        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(compilationResult.Ast!);

        // Should emit operator + syntax (not op_Addition)
        Assert.Contains("operator +", code);
        Assert.DoesNotContain("op_Addition", code);

        // Should emit the precondition contract check
        Assert.Contains("(a >= 0)", code);
        Assert.Contains("ContractViolationException", code);
    }

    [Fact]
    public void CSharpEmitter_OperatorWithPostcondition_EmitsResultCheck()
    {
        var calorSource = """
            §M{m1:Test}
            §CL{c1:Counter:struct}
            §FLD{i32:Value:pub}
            §MT{m1:op_Addition:pub:static}
              §I{i32:a}
              §I{i32:b}
              §O{i32}
              §S (>= result INT:0)
              §R (+ a b)
            §/MT{m1}
            §/CL{c1}
            §/M{m1}
            """;

        var compilationResult = Program.Compile(calorSource);
        Assert.False(compilationResult.HasErrors,
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(compilationResult.Ast!);

        // Should emit operator + syntax
        Assert.Contains("operator +", code);

        // Should emit postcondition with __result__ pattern
        Assert.Contains("__result__", code);
        Assert.Contains("return __result__", code);
        Assert.Contains("ContractViolationException", code);
    }

    #endregion

    #region FeatureSupport

    [Fact]
    public void FeatureSupport_OperatorOverload_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("operator-overload"));
        Assert.True(FeatureSupport.IsFullySupported("implicit-conversion"));
        Assert.True(FeatureSupport.IsFullySupported("explicit-conversion"));
        Assert.True(FeatureSupport.IsFullySupported("equals-operator"));
        Assert.True(FeatureSupport.IsFullySupported("readonly-struct"));
    }

    #endregion

    #region Helpers

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => i.ToString()));
    }

    #endregion
}
