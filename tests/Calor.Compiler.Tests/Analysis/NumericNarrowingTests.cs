using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

/// <summary>
/// 0.30 i1: the type checker rejects the implicit numeric conversions C# rejects (C# §10.2.3,
/// §10.2.11), at the Calor expression, with Calor0202.
///
/// <para>Before this, every integer width compared equal to <c>int</c> and both binary floating
/// types to <c>double</c>, so <c>let float:x = someDouble</c> and <c>let int:y = someLong</c>
/// passed the checker. The error surfaced only as C# CS0266 from the generated code, and inside
/// the same function as an "unresolved addition" charged <c>unknown</c> (Calor0410).</para>
///
/// <para>The accepted half compiles WITH generated-C# validation, so Roslyn agrees with every
/// widening, constant and literal case the checker lets through.</para>
/// </summary>
public class NumericNarrowingTests
{
    private const string Parameters =
        "double:d, long:l, decimal:m, int:i, float:f, uint:u, char:c, byte:b, ulong:ul, short:s";

    private static CompilationResult Compile(string source, bool validateGeneratedCSharp)
        => Program.Compile(source, "t.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            DeferGeneratedOutputValidation = !validateGeneratedCSharp,
        });

    private static string InFunction(string statement, string returnType = "void")
        => "module N\n"
            + $"  func Probe ({Parameters}) → {returnType}\n"
            + "    !\n"
            + $"    {statement}\n"
            + (returnType == "void" ? "    return\n" : "");

    private static Diagnostic SingleNarrowing(CompilationResult result)
    {
        var errors = result.Diagnostics.Errors.ToList();
        Assert.True(errors.Count == 1,
            "expected exactly one error, got:\n" + string.Join("\n", errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.Equal(DiagnosticCode.TypeMismatch, errors[0].Code);
        Assert.Contains("Cannot implicitly convert", errors[0].Message);
        return errors[0];
    }

    [Theory]
    [InlineData("let float:x = d", "double", "float")]
    [InlineData("let int:x = l", "long", "int")]
    [InlineData("let int:x = 5L", "long", "int")]
    [InlineData("let float:x = 1.5", "double", "float")]
    [InlineData("let float:x = f * 2.0", "double", "float")]
    [InlineData("let float:x = f + d", "double", "float")]
    [InlineData("let byte:x = i", "int", "byte")]
    [InlineData("let byte:x = b + b", "int", "byte")]
    [InlineData("let int:x = u", "uint", "int")]
    [InlineData("let uint:x = i", "int", "uint")]
    [InlineData("let int:x = -u", "long", "int")]
    [InlineData("let short:x = c", "char", "short")]
    [InlineData("let int:x = ul", "ulong", "int")]
    [InlineData("let long:x = ul", "ulong", "long")]
    [InlineData("let short:x = s * 2", "int", "short")]
    public void NarrowingBinding_IsCalor0202AtTheInitializer(string statement, string from, string to)
    {
        var source = InFunction(statement);
        var error = SingleNarrowing(Compile(source, validateGeneratedCSharp: false));

        Assert.Contains($"Cannot implicitly convert {from} to {to}", error.Message);
        Assert.Contains($"({to})value", error.Message);
        // Line 4 is the binding; the span starts at the initializer, after `= `.
        Assert.Equal(4, error.Span.Line);
        Assert.Equal(statement.IndexOf("= ", StringComparison.Ordinal) + 2 + 5, error.Span.Column);
    }

    [Theory]
    [InlineData("let byte:x = 300", "300", "byte")]
    [InlineData("let sbyte:x = 128", "128", "sbyte")]
    [InlineData("let uint:x = -1", "-1", "uint")]
    [InlineData("let ushort:x = 65536", "65536", "ushort")]
    [InlineData("let ulong:x = -1L", "-1", "ulong")]
    public void IntegerConstantOutOfRange_IsCalor0202(string statement, string value, string to)
    {
        var error = SingleNarrowing(Compile(InFunction(statement), validateGeneratedCSharp: false));
        Assert.Contains($"the constant {value} is outside the range of {to}", error.Message);
    }

    [Theory]
    [InlineData("let int:x = d")]
    [InlineData("let decimal:x = d")]
    [InlineData("let double:x = m")]
    [InlineData("let float:x = m")]
    public void CrossFamilyBinding_StaysRejected(string statement)
    {
        // Already rejected before i1 (different families); pinned so the width rule cannot
        // reopen them.
        var errors = Compile(InFunction(statement), validateGeneratedCSharp: false).Diagnostics.Errors;
        Assert.Contains(errors, error => error.Code == DiagnosticCode.TypeMismatch);
    }

    [Theory]
    [InlineData("return d", "float", "double", "float")]
    [InlineData("return l", "int", "long", "int")]
    [InlineData("return d", "int", "double", "int")]
    [InlineData("return m", "double", "decimal", "double")]
    [InlineData("return d", "decimal", "double", "decimal")]
    [InlineData("return f * 2.5", "float", "double", "float")]
    public void NarrowingReturn_IsCalor0202AtTheExpression(
        string statement, string returnType, string from, string to)
    {
        var error = SingleNarrowing(Compile(InFunction(statement, returnType), validateGeneratedCSharp: false));
        Assert.Contains($"Cannot implicitly convert {from} to {to} in a return", error.Message);
        Assert.Equal(4, error.Span.Line);
        Assert.Equal(12, error.Span.Column);
    }

    [Fact]
    public void NarrowingAssignment_IsCalor0202AtTheValue()
    {
        var source = "module N\n"
            + "  func Probe (long:l) → int\n"
            + "    !\n"
            + "    var int:total = 0\n"
            + "    set total l\n"
            + "    return total\n";
        var error = SingleNarrowing(Compile(source, validateGeneratedCSharp: false));
        Assert.Contains("Cannot implicitly convert long to int in an assignment", error.Message);
        Assert.Equal(5, error.Span.Line);
        Assert.Equal(15, error.Span.Column);
    }

    [Fact]
    public void NarrowingCallArgument_IsCalor0202AtTheArgument()
    {
        var source = "module N\n"
            + "  func Scale (float:x) → float\n"
            + "    !\n"
            + "    return x\n"
            + "\n"
            + "  func Probe (double:d) → float\n"
            + "    !\n"
            + "    return Scale(d)\n";
        var error = SingleNarrowing(Compile(source, validateGeneratedCSharp: false));
        Assert.Contains("Cannot implicitly convert double to float in argument 1 of 'Scale'", error.Message);
        Assert.Equal(8, error.Span.Line);
        Assert.Equal(18, error.Span.Column);
    }

    /// <summary>
    /// The shape of the fixed fixtures: a float function in a nested module (a class to the
    /// checker) returning <c>celsius + 273.15</c>. Class members were checked only for refinement
    /// errors, so this reached C# as CS0266 or, through the effect projection, as Calor0410.
    /// </summary>
    [Fact]
    public void NarrowingInClassMember_IsCalor0202NotAnEffectError()
    {
        var source = "module N\n"
            + "  module Temperatures\n"
            + "    func ToKelvin public (float:celsius) → float\n"
            + "      !\n"
            + "      return celsius + 273.15\n";
        var result = Compile(source, validateGeneratedCSharp: true);
        var error = SingleNarrowing(result);
        Assert.Contains("Cannot implicitly convert double to float in a return", error.Message);
        Assert.Equal(5, error.Span.Line);
        Assert.Equal(14, error.Span.Column);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void NarrowingInsideForeachBody_SurvivesTheFlowCache()
    {
        var source = "module N\n"
            + "  func Probe (double[]:values) → void\n"
            + "    !\n"
            + "    each double:value in values\n"
            + "      let float:x = value\n"
            + "    return\n";
        var error = SingleNarrowing(Compile(source, validateGeneratedCSharp: false));
        Assert.Contains("Cannot implicitly convert double to float", error.Message);
        Assert.Equal(5, error.Span.Line);
    }

    [Theory]
    [InlineData("let float:x = 1")]
    [InlineData("let float:x = 1.5f")]
    [InlineData("let float:x = i")]
    [InlineData("let float:x = l")]
    [InlineData("let float:x = ul")]
    [InlineData("let float:x = f * 2")]
    [InlineData("let float:x = f + i")]
    [InlineData("let float:x = f * c")]
    [InlineData("let double:x = f")]
    [InlineData("let double:x = f * 2")]
    [InlineData("let double:x = 2 * 3")]
    [InlineData("let byte:x = 200")]
    [InlineData("let byte:x = 0xFF")]
    [InlineData("let sbyte:x = -128")]
    [InlineData("let short:x = 1 + 2")]
    [InlineData("let ushort:x = c")]
    [InlineData("let int:x = c")]
    [InlineData("let long:x = i")]
    [InlineData("let long:x = u")]
    [InlineData("let ulong:x = u")]
    [InlineData("let ulong:x = 5")]
    [InlineData("let ulong:x = 5L")]
    [InlineData("let uint:x = 7")]
    [InlineData("let uint:x = u + 1")]
    [InlineData("let ulong:x = ul + 1")]
    [InlineData("let long:x = u - i")]
    [InlineData("let int:x = b + b")]
    [InlineData("let int:x = s * 2")]
    [InlineData("let int:x = b << 2")]
    [InlineData("let int:x = -b")]
    [InlineData("let long:x = -u")]
    [InlineData("let decimal:x = i")]
    [InlineData("let decimal:x = l")]
    [InlineData("let decimal:x = c")]
    [InlineData("let decimal:x = 1.5m")]
    [InlineData("let decimal:x = m * 2")]
    [InlineData("let double:x = i / 2")]
    public void WideningConstantAndLiteralBindings_CompileThroughCSharp(string statement)
    {
        var result = Compile(InFunction(statement), validateGeneratedCSharp: true);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    /// <summary>
    /// The binder's canonical identity read the C# keyword <c>float</c> as double (<c>FLOAT</c>),
    /// while the type checker, the emitter and the docs read it as single precision. A
    /// <c>float</c> parameter then rejected a <c>1.5f</c> argument (Calor0208) and accepted a
    /// double. Only the exact lowercase keyword is single precision: <c>FLOAT</c> is the
    /// canonical double and must stay one.
    /// </summary>
    [Theory]
    [InlineData("float", "FLOAT[bits=32]")]
    [InlineData("float?", "FLOAT[bits=32]?")]
    [InlineData("float[]", "FLOAT[bits=32][]")]
    [InlineData("f32", "FLOAT[bits=32]")]
    [InlineData("FLOAT[bits=32]", "FLOAT[bits=32]")]
    [InlineData("double", "FLOAT")]
    [InlineData("FLOAT", "FLOAT")]
    [InlineData("f64", "FLOAT")]
    public void TypeIdentity_FloatKeywordIsSinglePrecision(string spelling, string canonical)
        => Assert.Equal(canonical, Calor.Compiler.Binding.TypeIdentity.Canonicalize(spelling));

    [Fact]
    public void SinglePrecisionLiteral_MatchesFloatParameter()
    {
        var source = "module N\n"
            + "  func Scale (float:x) → float\n"
            + "    !\n"
            + "    return x * 2\n"
            + "\n"
            + "  func Probe () → float\n"
            + "    !\n"
            + "    return Scale(1.5f) + Scale(2)\n";
        var result = Compile(source, validateGeneratedCSharp: true);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void ConstFieldThatFits_ConvertsToNarrowerType()
    {
        // `const int` is a constant expression: C# converts it to byte when the value fits.
        var source = "module N\n"
            + "  class Holder\n"
            + "    field int:Limit public const = 10\n"
            + "    func Get public () → byte\n"
            + "      !\n"
            + "      let byte:value = Limit\n"
            + "      return value\n";
        var result = Compile(source, validateGeneratedCSharp: true);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void WideningReturnAssignmentAndArgument_CompileThroughCSharp()
    {
        var source = "module N\n"
            + "  func Scale (double:x) → double\n"
            + "    !\n"
            + "    return x\n"
            + "\n"
            + "  func Probe (float:f, int:i) → double\n"
            + "    !\n"
            + "    var double:total = 0\n"
            + "    set total f\n"
            + "    set total total + i\n"
            + "    return Scale(f) + Scale(i) + total\n";
        var result = Compile(source, validateGeneratedCSharp: true);
        Assert.False(result.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }
}
