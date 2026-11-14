using Calor.Compiler;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Semantics.Tests;

/// <summary>
/// Characterization tests pinning the binder's current handling of
/// <c>let name</c> without a type annotation. These tests document
/// the behavior referenced by the v0.6 bind-inference RFC
/// (<c>docs/plans/v0.6-bind-inference-formalization.md</c>, §1.2 table).
///
/// Missing both type and initializer is a native parser error. The recovered
/// AST also exercises the binder's Calor0250 guard and INT recovery type,
/// retaining the v0.6 regression rather than accepting an invalid declaration.
/// </summary>
public class BindInferenceDocsTests
{
    private static BoundModule Bind(
        string source, out DiagnosticBag diagnostics, bool expectMissingInitializer = false)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        if (expectMissingInitializer)
        {
            // Native syntax rejects the declaration before binding; also verify the
            // existing Calor0250 recovery guard against that actual recovered AST.
            var error = Assert.Single(diagnostics.Errors);
            Assert.Equal(DiagnosticCode.UnexpectedToken, error.Code);
            Assert.Equal("An inferred binding requires '= expression'.", error.Message);
        }
        else
        {
            Assert.False(diagnostics.HasErrors,
                $"Source failed to parse: {string.Join("; ", diagnostics.Errors.Select(e => e.Message))}");
        }
        var binder = new Binder(diagnostics);
        return binder.Bind(module);
    }

    private static BoundBindStatement FirstBind(BoundModule module)
    {
        var func = module.Functions.First();
        return func.Body.OfType<BoundBindStatement>().First();
    }

    [Fact]
    public void IntLiteralInitializer_InfersInt()
    {
        var source = """
module Test Test
  func Foo () → int
    !
    let x = 42
    return x
""";
        var bound = Bind(source, out var diags);
        Assert.False(diags.HasErrors);
        var bind = FirstBind(bound);
        Assert.Equal("INT", bind.Variable.TypeName);
    }

    [Fact]
    public void StringLiteralInitializer_InfersString()
    {
        var source = """
module Test Test
  func Foo () → string
    !
    let s = "hello"
    return s
""";
        var bound = Bind(source, out var diags);
        Assert.False(diags.HasErrors);
        var bind = FirstBind(bound);
        Assert.Equal("STRING", bind.Variable.TypeName);
    }

    [Fact]
    public void BoolLiteralInitializer_InfersBool()
    {
        var source = """
module Test Test
  func Foo () → bool
    !
    let b = true
    return b
""";
        var bound = Bind(source, out var diags);
        Assert.False(diags.HasErrors);
        var bind = FirstBind(bound);
        Assert.Equal("BOOL", bind.Variable.TypeName);
    }

    [Fact]
    public void FloatLiteralInitializer_InfersFloat()
    {
        var source = """
module Test Test
  func Foo () → double
    !
    let x = 3.14
    return x
""";
        var bound = Bind(source, out var diags);
        Assert.False(diags.HasErrors);
        var bind = FirstBind(bound);
        Assert.Equal("FLOAT", bind.Variable.TypeName);
    }

    [Fact]
    public void ExplicitTypeAnnotation_OverridesInference()
    {
        // Native declarations retain the C# spelling; literal inference still uses INT.
        var source = """
module Test Test
  func Foo () → int
    !
    let int:x = 0
    return x
""";
        var bound = Bind(source, out var diags);
        Assert.False(diags.HasErrors);
        var bind = FirstBind(bound);
        Assert.Equal("int", bind.Variable.TypeName);
        var result = SemanticsTestHarness.Execute(source, "Foo");
        Assert.True(result.Succeeded, $"Execution failed: {result.Exception?.Message}");
        Assert.Equal(0, Assert.IsType<int>(result.ReturnValue));
    }

    /// <summary>
    /// Retains the historical test identity while requiring rejection by the
    /// native parser and Calor0250 from binding its recovered AST. INT is only
    /// the recovery type, not evidence that the invalid source was accepted.
    /// </summary>
    [Fact]
    public void NoTypeNoInitializer_SilentlyDefaultsToInt()
    {
        var source = """
module Test Test
  func Foo () → int
    let x
    return 0
""";
        var bound = Bind(source, out var diags, expectMissingInitializer: true);

        // After Calor0250: the binder now reports an error for this case.
        // The bound tree is still produced (with INT fallback) so downstream
        // passes can continue surfacing diagnostics, but compilation fails
        // because of the new error.
        Assert.True(diags.HasErrors,
            "Calor0250 must fire for let x with no type and no initializer.");
        Assert.Contains(diags.Errors, d => d.Code == DiagnosticCode.BindRequiresTypeOrInitializer);
        var bind = FirstBind(bound);
        // Fallback type is still INT so the rest of the bound tree remains usable.
        Assert.Equal("INT", bind.Variable.TypeName);
    }

    [Fact]
    public void NoTypeNoInitializer_ReportsCalor0250()
    {
        var source = """
module Test Test
  func Foo () → int
    let counter
    return 0
""";
        var bound = Bind(source, out var diags, expectMissingInitializer: true);

        Assert.True(diags.HasErrors);
        var err = Assert.Single(diags.Errors,
            d => d.Code == DiagnosticCode.BindRequiresTypeOrInitializer);
        Assert.Contains("counter", err.Message);
        Assert.Contains("int:name", err.Message);
        Assert.DoesNotContain("§B", err.Message);
    }

    [Fact]
    public void MutableBinding_WithInferredType_IsMutable()
    {
        var source = """
module Test Test
  func Foo () → int
    !
    var counter = 0
    return counter
""";
        var bound = Bind(source, out var diags);
        Assert.False(diags.HasErrors);
        var bind = FirstBind(bound);
        Assert.Equal("INT", bind.Variable.TypeName);
        Assert.True(bind.Variable.IsMutable);
    }
}
