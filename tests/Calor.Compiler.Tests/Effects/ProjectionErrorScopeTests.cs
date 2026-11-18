using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Built-in additions are charged <c>unknown</c> unless the module's C# projection proves
/// which operator they select. A C# error confined to one member's body cannot change how
/// any other body binds, so it voids only that member's proofs. Before, one ill-typed
/// function (here an implicit double → float return) made every integer addition in the
/// module "unresolved addition", and the false Calor0410 hid the real C# error.
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
              func Avg public (int:count, int:total) → float
                !
                return (double)total / (double)count
            """ + Sum, "projection-scope.calr");

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code is DiagnosticCode.ForbiddenEffect or DiagnosticCode.UnknownExternalCall);
        var error = Assert.Single(result.Diagnostics.Errors);
        Assert.Equal(DiagnosticCode.CodeGenCompilationError, error.Code);
        Assert.Contains("CS0266", error.Message, StringComparison.Ordinal);
        Assert.Equal(4, error.Span.Line);
    }

    [Fact]
    public void WellTypedModule_WithTheSameCastsCompiles()
    {
        var result = Program.Compile("""
            module M
              func Avg public (int:count, int:total) → double
                !
                return (double)total / (double)count
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
              func ToKelvin public (float:celsius) → float
                !
                return celsius + 273.15
            """ + Sum, "projection-scope-same.calr");

        var forbidden = Assert.Single(result.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Contains("'ToKelvin'", forbidden.Message, StringComparison.Ordinal);
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
