using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Built-in additions are charged <c>unknown</c> unless the module's C# projection proves
/// which operator they select. A C# error confined to one member's body cannot change how
/// any other body binds, so it voids only that member's proofs. Before, one ill-typed
/// function made every integer addition in the module "unresolved addition", and the false
/// Calor0410 hid the real C# error. The ill-typed body here returns an <c>int[]</c> as a
/// <c>long[]</c>: Calor's type checker does not compare array element widths yet, so only
/// C# reports it (CS0029). (Calor0202 now catches the original double → float example
/// before the projection is built.) If the checker learns element widths, pick another
/// error that only C# reports.
/// </summary>
public sealed class ProjectionErrorScopeTests
{
    private const string Sum = """

          func Sum public (int:a, int:b) → int
            !
            return a + b
        """;

    [Fact]
    public void ErrorInAnotherMembersBody_DoesNotVoidThisMembersAdditionProof()
    {
        var result = Program.Compile("""
            module M
              func Widen public (int[]:values) → long[]
                !
                return values
            """ + Sum, "projection-scope.calr");

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.ForbiddenEffect or DiagnosticCode.UnknownExternalCall);
        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.CodeGenCompilationError, error.Code);
        Assert.Contains("CS0029", error.Message, StringComparison.Ordinal);
        Assert.Equal(4, error.Span.Line);
    }

    [Fact]
    public void WellTypedModule_WithTheSameCastsCompiles()
    {
        var result = Program.Compile("""
            module M
              func Widen public (int[]:values) → int[]
                !
                return values
            """ + Sum, "projection-scope-clean.calr");

        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.NotEmpty(result.GeneratedCode);
    }

    [Fact]
    public void ErrorInTheSameMembersBody_KeepsItsAdditionUnknown()
    {
        // Within one body an error can retype later operations, so the proof is void.
        var result = Program.Compile("""
            module M
              func Widen public (int[]:values, int:extra) → long[]
                !
                let int:count = extra + 1
                return values
            """ + Sum, "projection-scope-same.calr");

        var forbidden = Assert.Single(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains("'Widen'", forbidden.Message, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall
            && diagnostic.Message.Contains("unresolved addition", StringComparison.Ordinal)
            && diagnostic.Span.Line == 4);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("'Sum'", StringComparison.Ordinal));
    }

    [Fact]
    public void ErrorOutsideAnyBody_StillVoidsEveryAdditionProof()
    {
        // A field initializer is not a method body: its error voids the whole image.
        var result = Program.Compile("""
            module M
              class Rates public
                field float:Rate public = 0.5
            """ + Sum, "projection-scope-field.calr");

        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'Sum'", StringComparison.Ordinal));
    }
}
