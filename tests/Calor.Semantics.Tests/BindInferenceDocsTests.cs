using Calor.Compiler;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Semantics.Tests;

/// <summary>
/// Characterization tests pinning the binder's current handling of
/// <c>§B{name}</c> without a type annotation. These tests document
/// the behavior referenced by the v0.6 bind-inference RFC
/// (<c>docs/plans/v0.6-bind-inference-formalization.md</c>, §1.2 table).
///
/// The "no type and no initializer" case currently silently defaults
/// to <c>INT</c> (see <c>Binder.cs:259</c>). RFC §3.2 promotes this
/// to a hard error (<c>Calor0250</c>). The corresponding test below
/// (<see cref="NoTypeNoInitializer_SilentlyDefaultsToInt"/>) will be
/// updated to expect <c>Calor0250</c> when that change lands.
/// </summary>
public class BindInferenceDocsTests
{
    private static BoundModule Bind(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        Assert.False(diagnostics.HasErrors,
            $"Source failed to parse: {string.Join("; ", diagnostics.Errors.Select(e => e.Message))}");
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
§M{m001:Test}
  §F{f001:Foo:pub}
      §O{i32}
      §B{x} INT:42
      §R x
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
§M{m001:Test}
  §F{f001:Foo:str}
      §O{str}
      §B{s} STR:"hello"
      §R s
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
§M{m001:Test}
  §F{f001:Foo:pub}
      §O{bool}
      §B{b} BOOL:true
      §R b
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
§M{m001:Test}
  §F{f001:Foo:pub}
      §O{f64}
      §B{x} FLOAT:3.14
      §R x
""";
        var bound = Bind(source, out var diags);
        Assert.False(diags.HasErrors);
        var bind = FirstBind(bound);
        Assert.Equal("FLOAT", bind.Variable.TypeName);
    }

    [Fact]
    public void ExplicitTypeAnnotation_OverridesInference()
    {
        // §B{x:i32} INT:0 — explicit annotation wins, AND the binder
        // currently normalizes the lowercase form ("i32") to the
        // uppercase canonical name ("INT") that bound literals use.
        // This is the behavior the RFC §1.2 table footnote calls out.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §O{i32}
      §B{x:i32} INT:0
      §R x
""";
        var bound = Bind(source, out var diags);
        Assert.False(diags.HasErrors);
        var bind = FirstBind(bound);
        Assert.Equal("INT", bind.Variable.TypeName);
    }

    /// <summary>
    /// CURRENT BEHAVIOR (latent bug): <c>§B{x}</c> with no type and no
    /// initializer silently binds <c>x</c> as <c>INT</c>. RFC
    /// <c>docs/plans/v0.6-bind-inference-formalization.md</c> §3.2 proposes
    /// promoting this to a hard error (<c>Calor0250 BindRequiresTypeOrInitializer</c>).
    ///
    /// Calor0250 IS NOW IMPLEMENTED — see the companion test
    /// <see cref="NoTypeNoInitializer_ReportsCalor0250"/> below.
    /// This test is kept to document the pre-RFC behavior for review history;
    /// it now asserts the new error code.
    /// </summary>
    [Fact]
    public void NoTypeNoInitializer_SilentlyDefaultsToInt()
    {
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §O{i32}
      §B{x}
      §R INT:0
""";
        var bound = Bind(source, out var diags);

        // After Calor0250: the binder now reports an error for this case.
        // The bound tree is still produced (with INT fallback) so downstream
        // passes can continue surfacing diagnostics, but compilation fails
        // because of the new error.
        Assert.True(diags.HasErrors,
            "Calor0250 must fire for §B{x} with no type and no initializer.");
        Assert.Contains(diags.Errors, d => d.Code == DiagnosticCode.BindRequiresTypeOrInitializer);
        var bind = FirstBind(bound);
        // Fallback type is still INT so the rest of the bound tree remains usable.
        Assert.Equal("INT", bind.Variable.TypeName);
    }

    [Fact]
    public void NoTypeNoInitializer_ReportsCalor0250()
    {
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §O{i32}
      §B{counter}
      §R INT:0
""";
        var bound = Bind(source, out var diags);

        Assert.True(diags.HasErrors);
        var err = Assert.Single(diags.Errors,
            d => d.Code == DiagnosticCode.BindRequiresTypeOrInitializer);
        Assert.Contains("counter", err.Message);
        Assert.Contains(":i32", err.Message);  // suggested-fix hint in message
    }

    [Fact]
    public void MutableBinding_WithInferredType_IsMutable()
    {
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §O{i32}
      §B{~counter} INT:0
      §R counter
""";
        var bound = Bind(source, out var diags);
        Assert.False(diags.HasErrors);
        var bind = FirstBind(bound);
        Assert.Equal("INT", bind.Variable.TypeName);
        Assert.True(bind.Variable.IsMutable);
    }
}
