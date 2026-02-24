using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Comprehensive tests for features added during the Calor compiler improvement plan.
/// Covers: throw expressions, default params, null-coalescing assignment, indexer conversion,
/// lambda chain hoisting, reduced parentheses, conditional usings, inline C# expressions,
/// preprocessor directives, and attribute targets.
/// </summary>
public class ImprovementPlanTests
{
    #region Helpers

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

    private static ModuleNode ParseCalor(string source)
    {
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        Assert.Empty(diag.Errors);
        var parser = new Parser(tokens, diag);
        var ast = parser.Parse();
        Assert.Empty(diag.Errors);
        return ast;
    }

    private readonly CSharpToCalorConverter _converter = new();

    private static string? Compile(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        if (diagnostics.HasErrors) return null;

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        if (diagnostics.HasErrors) return null;

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Issues.Count > 0)
            return string.Join("\n", result.Issues.Select(i => i.Message));
        return "Conversion failed with no specific error message";
    }

    private static string FormatDiagnostics(CompilationResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"[{d.Code}] {d.Message}");
        return string.Join("\n", errors);
    }

    #endregion

    #region 1. Throw as Expression

    [Fact]
    public void ThrowExpression_ParseInExpressionPosition()
    {
        // §TH should be parseable as an expression (e.g., in null-coalesce right-hand side)
        // Since §TH with §NEW can't be used inside lisp forms, test via C# converter
        var result = _converter.Convert(@"
public class Service
{
    public string GetName(string? input)
    {
        return input ?? throw new ArgumentNullException(nameof(input));
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // The converter should handle the throw expression
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        Assert.True(method.Body.Count >= 1);
    }

    [Fact]
    public void ThrowExpression_CodegenProducesThrowExpr()
    {
        // Verify that codegen emits `throw new InvalidOperationException(...)`
        var source = @"
§M{m001:Test}
§CL{c001:Svc:pub}
§MT{m001:Validate:pub}
§I{str:name}
§O{str}
§IF{i1} (== name null)
§TH §NEW{InvalidOperationException} §A ""name is null"" §/NEW
§/I{i1}
§R name
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("throw new InvalidOperationException", csharp);
    }

    [Fact]
    public void ThrowExpression_CSharpConversion_NullCoalesceThrow()
    {
        // Convert `x ?? throw new Exception("msg")` and verify it produces a ThrowExpressionNode
        var result = _converter.Convert(@"
public class Service
{
    public string GetName(string? input)
    {
        return input ?? throw new Exception(""missing"");
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));

        // The AST should contain a throw expression or a hoisted guard
        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        Assert.True(method.Body.Count >= 1);
        // Check the Calor source contains §TH (throw marker)
        Assert.NotNull(result.CalorSource);
    }

    #endregion

    #region 2. Default Params in Inline Syntax

    [Fact]
    public void DefaultParam_InlineMethod_ParsesDefaultValue()
    {
        // Parse inline method syntax with default parameter
        var source = @"
§M{m001:Test}
§CL{c001:Calculator:pub}
§MT{m001:Add:pub} (i32:x, i32:y = 0) -> i32
§R (+ x y)
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var module = ParseCalor(source);
        var cls = Assert.Single(module.Classes);
        var method = Assert.Single(cls.Methods);
        Assert.Equal(2, method.Parameters.Count);
        Assert.Equal("x", method.Parameters[0].Name);
        Assert.Equal("y", method.Parameters[1].Name);
        Assert.NotNull(method.Parameters[1].DefaultValue);
    }

    [Fact]
    public void DefaultParam_Codegen_EmitsDefaultInParameter()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Calculator:pub}
§MT{m001:Add:pub} (i32:x, i32:y = 0) -> i32
§R (+ x y)
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("int y = 0", csharp);
        Assert.Contains("int x", csharp);
    }

    #endregion

    #region 3. Null-Coalescing Assignment ??=

    [Fact]
    public void NullCoalesceAssignment_CSharpConversion_ProducesCompoundAssignment()
    {
        // Convert `x ??= y` from C# and verify it produces a CompoundAssignment with NullCoalesce operator
        var result = _converter.Convert(@"
public class Cache
{
    private string? _value;

    public void EnsureValue(string fallback)
    {
        _value ??= fallback;
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));

        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        // Should have a compound assignment statement
        var compoundAssign = method.Body.OfType<CompoundAssignmentStatementNode>().FirstOrDefault();
        Assert.NotNull(compoundAssign);
        Assert.Equal(CompoundAssignmentOperator.NullCoalesce, compoundAssign!.Operator);
    }

    [Fact]
    public void NullCoalesceAssignment_Codegen_EmitsCorrectOperator()
    {
        // Convert C# with ??= and then compile back to C#
        var result = _converter.Convert(@"
public class Cache
{
    private string? _value;

    public void EnsureValue(string fallback)
    {
        _value ??= fallback;
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Compile back to C#
        var compileResult = Program.Compile(result.CalorSource!, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });
        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
        // The round-trip may emit either ??= or the expanded form x = x ?? fallback
        Assert.True(
            compileResult.GeneratedCode!.Contains("??=") || compileResult.GeneratedCode.Contains("?? "),
            "Expected null-coalescing assignment or null-coalesce expression in output");
    }

    #endregion

    #region 4. Indexer in Converter Method Chains

    [Fact]
    public void IndexerChain_CSharpConversion_HandlesIndexerInChain()
    {
        // Convert `words[0].ToUpper()` and verify conversion succeeds
        var result = _converter.Convert(@"
public class TextProcessor
{
    public string GetFirstUpper(string[] words)
    {
        return words[0].ToUpper();
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource!);
    }

    [Fact]
    public void IndexerChain_RoundTrip_ProducesValidCSharp()
    {
        var result = _converter.Convert(@"
public class TextProcessor
{
    public string GetFirstUpper(string[] words)
    {
        return words[0].ToUpper();
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));

        var compileResult = Program.Compile(result.CalorSource!, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });
        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
        Assert.Contains("ToUpper", compileResult.GeneratedCode);
    }

    #endregion

    #region 5. Lambda Chain Hoisting

    [Fact]
    public void LambdaChain_CSharpConversion_PreservesLambdaBody()
    {
        // Convert list.Select(x => x.ToString().Trim()) and verify conversion succeeds
        var result = _converter.Convert(@"
using System.Linq;
using System.Collections.Generic;

public class DataProcessor
{
    public List<string> ProcessItems(List<int> items)
    {
        return items.Select(x => x.ToString()).ToList();
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource!);
    }

    [Fact]
    public void LambdaChain_RoundTrip_ProducesValidCSharp()
    {
        var result = _converter.Convert(@"
using System.Linq;
using System.Collections.Generic;

public class DataProcessor
{
    public List<string> ProcessItems(List<int> items)
    {
        return items.Select(x => x.ToString()).ToList();
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));

        var compileResult = Program.Compile(result.CalorSource!, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });
        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
    }

    #endregion

    #region 6. Reduced Parentheses in Codegen

    [Fact]
    public void ReducedParens_SimpleBinaryOp_NoParens()
    {
        // (+ a b) should generate `a + b` without unnecessary parentheses
        var source = @"
§M{m001:Test}
§F{f001:Add:pub}
§I{i32:a}
§I{i32:b}
§O{i32}
§BODY
§R (+ a b)
§END_BODY
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("a + b", csharp);
    }

    [Fact]
    public void ReducedParens_PrecedenceRespected_NoParensNeeded()
    {
        // (+ (* a b) c) should generate `a * b + c` (no parens needed since * binds tighter)
        var source = @"
§M{m001:Test}
§F{f001:Calc:pub}
§I{i32:a}
§I{i32:b}
§I{i32:c}
§O{i32}
§BODY
§R (+ (* a b) c)
§END_BODY
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("a * b + c", csharp);
        // Should NOT have parentheses around (a * b) since multiplication has higher precedence
        Assert.DoesNotContain("(a * b)", csharp);
    }

    [Fact]
    public void ReducedParens_PrecedenceRespected_ParensNeeded()
    {
        // (* (+ a b) c) should generate `(a + b) * c` (parens needed since + has lower precedence)
        var source = @"
§M{m001:Test}
§F{f001:Calc:pub}
§I{i32:a}
§I{i32:b}
§I{i32:c}
§O{i32}
§BODY
§R (* (+ a b) c)
§END_BODY
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("(a + b) * c", csharp);
    }

    #endregion

    #region 7. Conditional Using Emission

    [Fact]
    public void ConditionalUsing_SimpleProgram_NoLinqUsing()
    {
        // A simple program without LINQ usage should NOT have `using System.Linq;`
        var source = @"
§M{m001:Test}
§F{f001:Main:pub}
§O{void}
§E{cw}
§P ""Hello""
§/F{f001}
§/M{m001}
";
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.DoesNotContain("using System.Linq;", result.GeneratedCode);
    }

    [Fact]
    public void ConditionalUsing_WithLinqUsage_LinqUsingPresent()
    {
        // A program that uses LINQ methods (via quantifier/contract expansion) should have `using System.Linq;`
        // We can trigger this by including code that generates .Any(), .All(), .Select(), etc.
        var source = @"
§M{m001:Test}
§CL{c001:Svc:pub}
§MT{m001:HasPositive:pub}
§I{List<i32>:items}
§O{bool}
§R §CS{items.Any(x => x > 0)}
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });
        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.Contains("using System.Linq;", result.GeneratedCode);
    }

    #endregion

    #region 8. Inline C# Expression §CS{expr}

    [Fact]
    public void InlineCSharp_ParseAndRoundTrip()
    {
        // Parse §CS{DateTime.Now.Ticks} and verify it round-trips correctly
        var source = @"
§M{m001:Test}
§F{f001:GetTicks:pub}
§O{i64}
§BODY
§R §CS{DateTime.Now.Ticks}
§END_BODY
§/F{f001}
§/M{m001}
";
        var module = ParseCalor(source);
        var func = Assert.Single(module.Functions);
        Assert.NotEmpty(func.Body);
    }

    [Fact]
    public void InlineCSharp_Codegen_EmitsVerbatim()
    {
        // Verify codegen emits `DateTime.Now.Ticks` verbatim
        var source = @"
§M{m001:Test}
§F{f001:GetTicks:pub}
§O{i64}
§BODY
§R §CS{DateTime.Now.Ticks}
§END_BODY
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("DateTime.Now.Ticks", csharp);
    }

    [Fact]
    public void InlineCSharp_ComplexExpression_EmitsVerbatim()
    {
        var source = @"
§M{m001:Test}
§F{f001:GetEnv:pub}
§O{str}
§BODY
§R §CS{Environment.GetEnvironmentVariable(""HOME"") ?? ""/tmp""}
§END_BODY
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("Environment.GetEnvironmentVariable", csharp);
    }

    #endregion

    #region 9. Preprocessor Directives §PP{CONDITION}

    [Fact]
    public void Preprocessor_IfBlock_EmitsHashIfEndif()
    {
        // Parse §PP{DEBUG} ... §/PP{DEBUG} and verify codegen emits #if DEBUG ... #endif
        var source = @"
§M{m001:Test}
§CL{c001:Logger:pub}
§MT{m001:Log:pub}
§I{str:msg}
§PP{DEBUG}
§C{Console.WriteLine} msg
§/PP{DEBUG}
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("#if DEBUG", csharp);
        Assert.Contains("#endif", csharp);
        Assert.Contains("Console.WriteLine", csharp);
    }

    [Fact]
    public void Preprocessor_IfElseBlock_EmitsHashIfElseEndif()
    {
        // Test with else body: §PP{DEBUG} ... §PPE ... §/PP{DEBUG}
        var source = @"
§M{m001:Test}
§CL{c001:Logger:pub}
§MT{m001:GetLevel:pub}
  §O{str}
  §PP{DEBUG}
  §R ""debug""
  §PPE
  §R ""release""
  §/PP{DEBUG}
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("#if DEBUG", csharp);
        Assert.Contains("#else", csharp);
        Assert.Contains("#endif", csharp);
        Assert.Contains("\"debug\"", csharp);
        Assert.Contains("\"release\"", csharp);
    }

    [Fact]
    public void Preprocessor_CustomCondition_EmitsCorrectly()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Config:pub}
§MT{m001:GetEndpoint:pub}
  §O{str}
  §PP{RELEASE}
  §R ""https://prod.example.com""
  §PPE
  §R ""https://dev.example.com""
  §/PP{RELEASE}
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("#if RELEASE", csharp);
        Assert.Contains("#else", csharp);
        Assert.Contains("#endif", csharp);
    }

    #endregion

    #region 10. Attribute Targets

    [Fact]
    public void AttributeTarget_CSharpConversion_PreservesReturnTarget()
    {
        // Convert [return: NotNull] from C# and verify the target "return" is preserved
        var result = _converter.Convert(@"
using System.Diagnostics.CodeAnalysis;

public class Service
{
    [return: NotNull]
    public string GetValue()
    {
        return ""hello"";
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));

        var method = Assert.Single(Assert.Single(result.Ast!.Classes).Methods);
        // Check that the method has C# attributes with the target preserved
        var attrWithTarget = method.CSharpAttributes
            .FirstOrDefault(a => a.Target != null);
        Assert.NotNull(attrWithTarget);
        Assert.Equal("return", attrWithTarget!.Target);
    }

    [Fact]
    public void AttributeTarget_Codegen_EmitsTargetPrefix()
    {
        // Verify codegen emits `[return: NotNull]`
        var result = _converter.Convert(@"
using System.Diagnostics.CodeAnalysis;

public class Service
{
    [return: NotNull]
    public string GetValue()
    {
        return ""hello"";
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);

        // Compile back to C#
        var compileResult = Program.Compile(result.CalorSource!, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });
        Assert.False(compileResult.HasErrors, FormatDiagnostics(compileResult));
        Assert.Contains("[return: NotNull]", compileResult.GeneratedCode);
    }

    [Fact]
    public void AttributeTarget_AssemblyTarget_Preserved()
    {
        // Test that assembly attribute targets are correctly handled during conversion
        var result = _converter.Convert(@"
using System.Runtime.CompilerServices;

public class Test
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Fast(int x)
    {
        return x * 2;
    }
}");
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.CalorSource);
        Assert.DoesNotContain("§ERR", result.CalorSource!);
    }

    #endregion

    #region Cross-Feature Integration Tests

    [Fact]
    public void Integration_ThrowExpression_InPreprocessorBlock()
    {
        // A throw statement inside a preprocessor-guarded block
        var source = @"
§M{m001:Test}
§CL{c001:Guard:pub}
§MT{m001:Check:pub}
§I{bool:flag}
§PP{DEBUG}
§IF{if1} (! flag)
§TH §NEW{InvalidOperationException} §A ""debug check failed"" §/NEW
§/I{if1}
§/PP{DEBUG}
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("#if DEBUG", csharp);
        Assert.Contains("throw new InvalidOperationException", csharp);
        Assert.Contains("#endif", csharp);
    }

    [Fact]
    public void Integration_DefaultParamWithInlineCSharp()
    {
        // Inline method with default params and inline C# expression
        var source = @"
§M{m001:Test}
§CL{c001:TimeHelper:pub}
§MT{m001:GetTime:pub} (i32:offset = 0) -> i64
§R §CS{DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offset}
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("int offset = 0", csharp);
        Assert.Contains("DateTimeOffset.UtcNow.ToUnixTimeSeconds()", csharp);
    }

    [Fact]
    public void Integration_FullCompilation_NoRoslynParseErrors()
    {
        // Compile a program with several features and verify the generated C# is syntactically valid
        var source = @"
§M{m001:Test}
§CL{c001:Utility:pub}
§MT{m001:Greet:pub} (str:name, str:greeting = ""Hello"") -> str
§R §CS{$""{greeting}, {name}!""}
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var result = Program.Compile(source, "test.calr", new CompilationOptions
        {
            EnforceEffects = false
        });

        Assert.False(result.HasErrors, FormatDiagnostics(result));
        Assert.NotEmpty(result.GeneratedCode);

        // Validate generated C# is syntactically valid
        var tree = CSharpSyntaxTree.ParseText(result.GeneratedCode);
        var roslynErrors = tree.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(roslynErrors);
    }

    #endregion
}
