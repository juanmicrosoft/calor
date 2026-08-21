using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.14 §S3b integration tests (nullability workstream / issue #875).
/// Exercise the full lex→parse→bind pipeline against Calor source that mixes
/// BCL calls and §B bindings. Verifies:
///   (a) BCL-shaped calls carry MetadataBinder-resolved NullableAnnotation
///       on their BoundCallExpression.Type
///   (b) Calor0272 fires for §B{x:string} = &lt;possibly-null-source&gt;
///   (c) Calor0272 does NOT fire for :?string targets
///   (d) Calor0272 does NOT fire for Calor-local bindings (no BCL involvement)
/// </summary>
public class NullabilityIntegrationTests
{
    private static (Calor.Compiler.Binding.BoundModule Module, DiagnosticBag Diagnostics)
        BindSource(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        var bound = new Binder(diagnostics).Bind(module);
        return (bound, diagnostics);
    }

    /// <summary>
    /// The canonical D3 repro from issue #875: binding
    /// <c>Environment.GetEnvironmentVariable</c>'s Annotated string return
    /// into a non-nullable <c>:string</c> target must emit
    /// <see cref="DiagnosticCode.NullableToNonNullableBinding"/> (Calor0272)
    /// at Info severity.
    /// </summary>
    [Fact]
    public void Calor0272_FiresFor_EnvironmentGetVariable_AssignedToNonNullString()
    {
        const string source = """
            §M{m1:D3Repro}
              §F{f1:Bad:pub} () -> void
                §E{env}
                §B{bad:string} §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && d.Severity == DiagnosticSeverity.Info);
    }

    /// <summary>
    /// Change the target to <c>:?string</c> — the same possibly-null source
    /// is now safe, and Calor0272 must NOT fire.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_When_TargetIs_NullableString()
    {
        const string source = """
            §M{m1:Nullable}
              §F{f1:Ok:pub} () -> void
                §E{env}
                §B{ok:?string} §C{System.Environment.GetEnvironmentVariable} §A STR:"MISSING" §/C
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// Calor-native string literals are provably non-null. Binding a
    /// <c>STR:"..."</c> literal into <c>:string</c> must NOT fire Calor0272.
    /// Before the BoundStringLiteral NotAnnotated fix this tripped D3's
    /// conservative Oblivious=possibly-null rule and produced noise on
    /// ordinary Calor-only code that touched no BCL surface at all.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_CalorNative_StringLiteral_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:LocalLiteral}
              §F{f1:Ok:pub} () -> void
                §B{greeting:string} STR:"hello"
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    /// <summary>
    /// Interpolated strings share the same non-null guarantee as string
    /// literals — binding a <c>$"..."</c>-shaped expression into
    /// <c>:string</c> must not trip Calor0272.
    /// </summary>
    [Fact]
    public void Calor0272_DoesNotFire_For_InterpolatedString_Bound_To_NonNullString()
    {
        const string source = """
            §M{m1:Interp}
              §F{f1:Ok:pub} () -> void
                §B{name:string} STR:"world"
                §B{greeting:string} §INTERP "hello, " §EXP name §/INTERP
            """;

        var (_, diagnostics) = BindSource(source);

        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }
}
