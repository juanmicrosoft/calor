using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for operator overload support: parsing, code generation, and C# conversion.
/// </summary>
public class OperatorOverloadTests
{
    #region Parsing Tests

    [Fact]
    public void Parse_BinaryOperator_CreatesCorrectAstNode()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{MyType:left}
    §I{MyType:right}
    §O{MyType}
    §R left
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Equal("op001", op.Id);
        Assert.Equal("+", op.OperatorToken);
        Assert.Equal(OperatorOverloadKind.Add, op.Kind);
        Assert.True(op.IsBinary);
        Assert.False(op.IsUnary);
        Assert.False(op.IsConversion);
        Assert.Equal(2, op.Parameters.Count);
        Assert.NotNull(op.Output);
        Assert.Equal("MyType", op.Output!.TypeName);
    }

    [Fact]
    public void Parse_UnaryOperator_CreatesCorrectAstNode()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:-:pub}
    §I{MyType:value}
    §O{MyType}
    §R value
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Equal("-", op.OperatorToken);
        Assert.Equal(OperatorOverloadKind.UnaryNegate, op.Kind);
        Assert.True(op.IsUnary);
        Assert.False(op.IsBinary);
        Assert.Single(op.Parameters);
    }

    [Fact]
    public void Parse_ImplicitConversion_CreatesCorrectAstNode()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:implicit:pub}
    §I{MyType:value}
    §O{i32}
    §R 0
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Equal("implicit", op.OperatorToken);
        Assert.Equal(OperatorOverloadKind.Implicit, op.Kind);
        Assert.True(op.IsConversion);
        Assert.False(op.IsBinary);
        Assert.False(op.IsUnary);
    }

    [Fact]
    public void Parse_ExplicitConversion_CreatesCorrectAstNode()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:explicit:pub}
    §I{i32:value}
    §O{MyType}
    §R value
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Equal("explicit", op.OperatorToken);
        Assert.Equal(OperatorOverloadKind.Explicit, op.Kind);
        Assert.True(op.IsConversion);
    }

    [Fact]
    public void Parse_OperatorWithContracts_PreservesContracts()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{MyType:left}
    §I{MyType:right}
    §O{MyType}
    §Q (>= left 0)
    §S (>= result 0)
    §R left
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Single(op.Preconditions);
        Assert.Single(op.Postconditions);
    }

    [Fact]
    public void Parse_MismatchedId_ReportsDiagnostic()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{MyType:left}
    §I{MyType:right}
    §O{MyType}
    §R left
  §/OP{op002}
§/CL{c1}
§/M{m1}
";

        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        parser.Parse();

        // Should have a mismatched ID diagnostic
        Assert.True(diagnostics.HasErrors);
    }

    [Theory]
    [InlineData("==", OperatorOverloadKind.Equality)]
    [InlineData("!=", OperatorOverloadKind.Inequality)]
    [InlineData("*", OperatorOverloadKind.Multiply)]
    [InlineData("/", OperatorOverloadKind.Divide)]
    [InlineData("%", OperatorOverloadKind.Modulo)]
    [InlineData("<", OperatorOverloadKind.LessThan)]
    [InlineData(">", OperatorOverloadKind.GreaterThan)]
    [InlineData("<=", OperatorOverloadKind.LessThanOrEqual)]
    [InlineData(">=", OperatorOverloadKind.GreaterThanOrEqual)]
    [InlineData("&", OperatorOverloadKind.BitwiseAnd)]
    [InlineData("|", OperatorOverloadKind.BitwiseOr)]
    [InlineData("^", OperatorOverloadKind.BitwiseXor)]
    [InlineData("<<", OperatorOverloadKind.LeftShift)]
    [InlineData(">>", OperatorOverloadKind.RightShift)]
    public void Parse_AllBinaryOperators(string operatorSymbol, OperatorOverloadKind expectedKind)
    {
        var source = $@"
§M{{m1:Test}}
§CL{{c1:MyType}}
  §OP{{op001:{operatorSymbol}:pub}}
    §I{{MyType:left}}
    §I{{MyType:right}}
    §O{{MyType}}
    §R left
  §/OP{{op001}}
§/CL{{c1}}
§/M{{m1}}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Equal(operatorSymbol, op.OperatorToken);
        Assert.Equal(expectedKind, op.Kind);
        Assert.True(op.IsBinary);
    }

    [Theory]
    [InlineData("!", OperatorOverloadKind.LogicalNot)]
    [InlineData("~", OperatorOverloadKind.BitwiseNot)]
    public void Parse_UnaryOperators_LogicalNotAndBitwiseNot(string operatorSymbol, OperatorOverloadKind expectedKind)
    {
        var source = $@"
§M{{m1:Test}}
§CL{{c1:MyType}}
  §OP{{op001:{operatorSymbol}:pub}}
    §I{{MyType:value}}
    §O{{MyType}}
    §R value
  §/OP{{op001}}
§/CL{{c1}}
§/M{{m1}}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Equal(operatorSymbol, op.OperatorToken);
        Assert.Equal(expectedKind, op.Kind);
        Assert.True(op.IsUnary);
        Assert.Single(op.Parameters);
    }

    [Theory]
    [InlineData("true", OperatorOverloadKind.True)]
    [InlineData("false", OperatorOverloadKind.False)]
    public void Parse_TrueFalseOperators(string operatorSymbol, OperatorOverloadKind expectedKind)
    {
        var source = $@"
§M{{m1:Test}}
§CL{{c1:MyType}}
  §OP{{op001:{operatorSymbol}:pub}}
    §I{{MyType:value}}
    §O{{bool}}
    §R {operatorSymbol}
  §/OP{{op001}}
§/CL{{c1}}
§/M{{m1}}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Equal(operatorSymbol, op.OperatorToken);
        Assert.Equal(expectedKind, op.Kind);
        Assert.True(op.IsUnary);
        Assert.Single(op.Parameters);
    }

    [Theory]
    [InlineData("++", OperatorOverloadKind.Increment)]
    [InlineData("--", OperatorOverloadKind.Decrement)]
    public void Parse_IncrementDecrementOperators(string operatorSymbol, OperatorOverloadKind expectedKind)
    {
        var source = $@"
§M{{m1:Test}}
§CL{{c1:MyType}}
  §OP{{op001:{operatorSymbol}:pub}}
    §I{{MyType:value}}
    §O{{MyType}}
    §R value
  §/OP{{op001}}
§/CL{{c1}}
§/M{{m1}}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Equal(operatorSymbol, op.OperatorToken);
        Assert.Equal(expectedKind, op.Kind);
        Assert.True(op.IsUnary);
        Assert.Single(op.Parameters);
    }

    #endregion

    #region Code Generation Tests

    [Fact]
    public void CodeGen_BinaryAdd_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{MyType:left}
    §I{MyType:right}
    §O{MyType}
    §R left
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static MyType operator +(MyType left, MyType right)", result);
    }

    [Fact]
    public void CodeGen_UnaryNegate_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:-:pub}
    §I{MyType:value}
    §O{MyType}
    §R value
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static MyType operator -(MyType value)", result);
    }

    [Fact]
    public void CodeGen_Equality_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:==:pub}
    §I{MyType:left}
    §I{MyType:right}
    §O{bool}
    §R true
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static bool operator ==(MyType left, MyType right)", result);
    }

    [Fact]
    public void CodeGen_ImplicitConversion_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:implicit:pub}
    §I{MyType:value}
    §O{i32}
    §R 0
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static implicit operator int(MyType value)", result);
    }

    [Fact]
    public void CodeGen_ExplicitConversion_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:explicit:pub}
    §I{i32:value}
    §O{MyType}
    §R value
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static explicit operator MyType(int value)", result);
    }

    [Fact]
    public void CodeGen_OperatorWithPreconditions_EmitsGuards()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{i32:left}
    §I{i32:right}
    §O{i32}
    §Q (>= left 0)
    §R (+ left right)
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("operator +(int left, int right)", result);
        // Precondition should be emitted as a contract violation check
        Assert.Contains("ContractViolationException", result);
    }

    [Fact]
    public void CodeGen_MultipleOperators_InClass()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{MyType:left}
    §I{MyType:right}
    §O{MyType}
    §R left
  §/OP{op001}
  §OP{op002:-:pub}
    §I{MyType:left}
    §I{MyType:right}
    §O{MyType}
    §R left
  §/OP{op002}
  §OP{op003:==:pub}
    §I{MyType:left}
    §I{MyType:right}
    §O{bool}
    §R true
  §/OP{op003}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("operator +(MyType left, MyType right)", result);
        Assert.Contains("operator -(MyType left, MyType right)", result);
        Assert.Contains("operator ==(MyType left, MyType right)", result);
    }

    [Fact]
    public void CodeGen_OperatorWithPostconditions_EmitsResultCapture()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{i32:left}
    §I{i32:right}
    §O{i32}
    §S (>= result 0)
    §R (+ left right)
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("operator +(int left, int right)", result);
        // Postcondition should capture result in __result__ variable
        Assert.Contains("__result__", result);
        Assert.Contains("ContractViolationException", result);
    }

    [Fact]
    public void CodeGen_LogicalNot_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:!:pub}
    §I{MyType:value}
    §O{MyType}
    §R value
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static MyType operator !(MyType value)", result);
    }

    [Fact]
    public void CodeGen_BitwiseNot_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:~:pub}
    §I{MyType:value}
    §O{MyType}
    §R value
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static MyType operator ~(MyType value)", result);
    }

    [Fact]
    public void CodeGen_TrueFalseOperator_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:true:pub}
    §I{MyType:value}
    §O{bool}
    §R true
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("operator true(MyType value)", result);
    }

    [Fact]
    public void CodeGen_UnaryPlus_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{MyType:value}
    §O{MyType}
    §R value
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        // With 1 parameter, + resolves to UnaryPlus
        Assert.Equal(OperatorOverloadKind.UnaryPlus, op.Kind);
        Assert.True(op.IsUnary);

        var result = ParseAndEmit(source);
        Assert.Contains("public static MyType operator +(MyType value)", result);
    }

    [Fact]
    public void CodeGen_PrivateOperator_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:priv}
    §I{MyType:left}
    §I{MyType:right}
    §O{MyType}
    §R left
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        var op = Assert.Single(cls.OperatorOverloads);

        Assert.Equal(Ast.Visibility.Private, op.Visibility);

        // C# operators are always public static, but we should still emit correctly
        var result = ParseAndEmit(source);
        Assert.Contains("operator +(MyType left, MyType right)", result);
    }

    [Fact]
    public void CodeGen_IncrementOperator_GeneratesValidCSharp()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:++:pub}
    §I{MyType:value}
    §O{MyType}
    §R value
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var result = ParseAndEmit(source);

        Assert.Contains("public static MyType operator ++(MyType value)", result);
    }

    #endregion

    #region C# to Calor Conversion Tests

    [Fact]
    public void Convert_ClassWithBinaryOperator_ProducesOpTag()
    {
        var csharpCode = @"
public class Money
{
    public decimal Amount { get; }

    public static Money operator +(Money left, Money right)
    {
        return new Money();
    }
}";

        var result = ConvertCSharpToCalor(csharpCode);

        Assert.Contains("§OP{", result);
        Assert.Contains(":+:", result);
        Assert.Contains("§/OP{", result);
    }

    [Fact]
    public void Convert_ClassWithUnaryOperator_ProducesOpTag()
    {
        var csharpCode = @"
public class Money
{
    public decimal Amount { get; }

    public static Money operator -(Money value)
    {
        return new Money();
    }
}";

        var result = ConvertCSharpToCalor(csharpCode);

        Assert.Contains("§OP{", result);
        Assert.Contains(":-:", result);
        Assert.Contains("§/OP{", result);
    }

    [Fact]
    public void Convert_ClassWithImplicitConversion_ProducesOpTag()
    {
        var csharpCode = @"
public class Money
{
    public decimal Amount { get; }

    public static implicit operator decimal(Money value)
    {
        return value.Amount;
    }
}";

        var result = ConvertCSharpToCalor(csharpCode);

        Assert.Contains("§OP{", result);
        Assert.Contains(":implicit:", result);
        Assert.Contains("§/OP{", result);
    }

    [Fact]
    public void Convert_ClassWithExplicitConversion_ProducesOpTag()
    {
        var csharpCode = @"
public class Money
{
    public decimal Amount { get; }

    public static explicit operator Money(decimal value)
    {
        return new Money();
    }
}";

        var result = ConvertCSharpToCalor(csharpCode);

        Assert.Contains("§OP{", result);
        Assert.Contains(":explicit:", result);
        Assert.Contains("§/OP{", result);
    }

    [Fact]
    public void Convert_ClassWithIncrementOperator_ProducesOpTag()
    {
        var csharpCode = @"
public class Counter
{
    public int Value { get; set; }

    public static Counter operator ++(Counter c)
    {
        return new Counter();
    }
}";

        var result = ConvertCSharpToCalor(csharpCode);

        Assert.Contains("§OP{", result);
        Assert.Contains(":++:", result);
        Assert.Contains("§/OP{", result);
    }

    [Fact]
    public void Convert_ClassWithTrueOperator_ProducesOpTag()
    {
        var csharpCode = @"
public class MyBool
{
    public bool Value { get; set; }

    public static bool operator true(MyBool b)
    {
        return b.Value;
    }

    public static bool operator false(MyBool b)
    {
        return !b.Value;
    }
}";

        var result = ConvertCSharpToCalor(csharpCode);

        Assert.Contains("§OP{", result);
        Assert.Contains(":true:", result);
        Assert.Contains(":false:", result);
    }

    [Fact]
    public void Convert_ClassWithLogicalNotOperator_ProducesOpTag()
    {
        var csharpCode = @"
public class MyBool
{
    public bool Value { get; set; }

    public static MyBool operator !(MyBool b)
    {
        return new MyBool();
    }
}";

        var result = ConvertCSharpToCalor(csharpCode);

        Assert.Contains("§OP{", result);
        Assert.Contains(":!:", result);
        Assert.Contains("§/OP{", result);
    }

    [Fact]
    public void Convert_ExpressionBodiedOperator_ProducesBody()
    {
        var csharpCode = @"
public class Vector
{
    public int X { get; set; }

    public static Vector operator +(Vector a, Vector b) => new Vector();
}";

        var result = ConvertCSharpToCalor(csharpCode);

        Assert.Contains("§OP{", result);
        Assert.Contains(":+:", result);
        // Expression body should be converted to a return statement
        Assert.Contains("§R", result);
        Assert.Contains("§/OP{", result);
    }

    [Fact]
    public void Convert_ExpressionBodiedConversion_ProducesBody()
    {
        var csharpCode = @"
public class Wrapper
{
    public int Value { get; set; }

    public static implicit operator int(Wrapper w) => w.Value;
}";

        var result = ConvertCSharpToCalor(csharpCode);

        Assert.Contains("§OP{", result);
        Assert.Contains(":implicit:", result);
        // Expression body should be converted to a return statement
        Assert.Contains("§R", result);
        Assert.Contains("§/OP{", result);
    }

    #endregion

    #region Roundtrip Tests

    [Fact]
    public void Roundtrip_ParseEmitReparse_PreservesOperatorOverload()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{MyType:left}
    §I{MyType:right}
    §O{MyType}
    §R left
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        // Parse original
        var module1 = ParseModule(source);
        var op1 = Assert.Single(Assert.Single(module1.Classes).OperatorOverloads);

        // Emit back to Calor using CalorEmitter
        var emitter = new Calor.Compiler.Migration.CalorEmitter();
        var emitted = emitter.Emit(module1);

        // Reparse emitted output
        var module2 = ParseModule(emitted);
        var op2 = Assert.Single(Assert.Single(module2.Classes).OperatorOverloads);

        // Verify structural equivalence
        Assert.Equal(op1.OperatorToken, op2.OperatorToken);
        Assert.Equal(op1.Kind, op2.Kind);
        Assert.Equal(op1.IsBinary, op2.IsBinary);
        Assert.Equal(op1.Parameters.Count, op2.Parameters.Count);
        Assert.Equal(op1.Output?.TypeName, op2.Output?.TypeName);
    }

    [Fact]
    public void Roundtrip_WithContracts_PreservesContracts()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{i32:left}
    §I{i32:right}
    §O{i32}
    §Q (>= left 0)
    §S (>= result 0)
    §R (+ left right)
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        // Parse original
        var module1 = ParseModule(source);
        var op1 = Assert.Single(Assert.Single(module1.Classes).OperatorOverloads);
        Assert.Single(op1.Preconditions);
        Assert.Single(op1.Postconditions);

        // Emit back to Calor
        var emitter = new Calor.Compiler.Migration.CalorEmitter();
        var emitted = emitter.Emit(module1);

        // Verify contracts present in emitted output
        Assert.Contains("§Q", emitted);
        Assert.Contains("§S", emitted);

        // Reparse emitted output
        var module2 = ParseModule(emitted);
        var op2 = Assert.Single(Assert.Single(module2.Classes).OperatorOverloads);

        Assert.Single(op2.Preconditions);
        Assert.Single(op2.Postconditions);
    }

    [Fact]
    public void Roundtrip_ImplicitConversion_PreservesKind()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:implicit:pub}
    §I{MyType:value}
    §O{i32}
    §R 0
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module1 = ParseModule(source);
        var emitter = new Calor.Compiler.Migration.CalorEmitter();
        var emitted = emitter.Emit(module1);

        var module2 = ParseModule(emitted);
        var op2 = Assert.Single(Assert.Single(module2.Classes).OperatorOverloads);

        Assert.Equal(OperatorOverloadKind.Implicit, op2.Kind);
        Assert.True(op2.IsConversion);
    }

    #endregion

    #region CalorFormatter Tests

    [Fact]
    public void Formatter_OperatorWithContracts_EmitsContracts()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{i32:left}
    §I{i32:right}
    §O{i32}
    §Q (>= left 0)
    §S (>= result 0)
    §R (+ left right)
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module = ParseModule(source);
        var formatter = new Calor.Compiler.Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        Assert.Contains("§OP{", formatted);
        Assert.Contains("§Q", formatted);
        Assert.Contains("§S", formatted);
        Assert.Contains("§/OP{", formatted);
    }

    [Fact]
    public void Formatter_Output_CanBeReparsed()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyType}
  §OP{op001:+:pub}
    §I{i32:left}
    §I{i32:right}
    §O{i32}
    §Q (>= left 0)
    §S (>= result 0)
    §R (+ left right)
  §/OP{op001}
§/CL{c1}
§/M{m1}
";

        var module = ParseModule(source);
        var formatter = new Calor.Compiler.Formatting.CalorFormatter();
        var formatted = formatter.Format(module);

        // The formatted output should be reparseable
        var module2 = ParseModule(formatted);
        var op2 = Assert.Single(Assert.Single(module2.Classes).OperatorOverloads);

        Assert.Equal("+", op2.OperatorToken);
        Assert.Equal(OperatorOverloadKind.Add, op2.Kind);
        Assert.Equal(2, op2.Parameters.Count);
        Assert.Single(op2.Preconditions);
        Assert.Single(op2.Postconditions);
    }

    #endregion

    #region Helpers

    private static ModuleNode ParseModule(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        return module;
    }

    private static string ParseAndEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static string ConvertCSharpToCalor(string csharpCode)
    {
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharpCode, "test.cs");

        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.ToString())));
        Assert.NotNull(result.CalorSource);

        return result.CalorSource!;
    }

    #endregion
}
