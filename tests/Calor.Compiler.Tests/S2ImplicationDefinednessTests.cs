using Calor.Compiler.Diagnostics;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1413 (0.24 S2) regression witnesses for the S1 sweep rows IMPL-ASSUMPTION-FORMS
/// (F-B1/N1-027..029) and IMPL-DIVISION-TOTALIZED (F-B1/N1-030). The inheritance checker's
/// implication prover reported "Precondition weakening proven" (Calor0815) for implementer
/// preconditions that throw or are false on inputs the interface accepts: a string
/// operation on a null string (the solver's strings are never null) and a modulo by zero
/// (the solver's bvsrem is total). The registered S1 sources are used verbatim; the C#
/// assertions reproduce the independent oracle's witness. The tests need Z3 and fail,
/// not skip, without it.
/// </summary>
public sealed class S2ImplicationDefinednessTests
{
    private const string StringSourceTemplate = """
        §M{m1:R1Case}
          §IFACE{i1:IProbe}
            §MT{m1:Run}
              §I{i32:x}
              §I{str:s}
              §O{i32}
              §Q (>= x INT:0)
          §CL{c1:Impl:pub}
            §IMPL{IProbe}
            §MT{mt1:Run:pub}
              §I{i32:x}
              §I{str:s}
              §O{i32}
              §Q IMPLPRE
              §R INT:0
        """;

    private const string DivisionSource = """
        §M{m1:R1Case}
          §IFACE{i1:IProbe}
            §MT{m1:Run}
              §I{i32:x}
              §O{i32}
              §I{i32:y}
              §Q (== x INT:3)
          §CL{c1:Impl:pub}
            §IMPL{IProbe}
            §MT{mt1:Run:pub}
              §I{i32:x}
              §I{i32:y}
              §O{i32}
              §Q (> (% x y) INT:-2)
              §R INT:0
        """;

    private static List<Diagnostic> Compile(string source)
    {
        var options = new CompilationOptions { ContractMode = ContractMode.Debug };
        var result = Program.Compile(source, "case.calr", options);
        return result.Diagnostics.ToList();
    }

    private static void AssertPreconditionWeakeningAssumed(List<Diagnostic> diagnostics, string assumption)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
        var assumed = Assert.Single(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
        Assert.Equal(DiagnosticSeverity.Warning, assumed.Severity);
        Assert.StartsWith("Precondition weakening for 'Impl.Run' is Assumed, not proven", assumed.Message);
        Assert.Contains(assumption, assumed.Message);
    }

    [Theory]
    [InlineData("(>= (len s) INT:0)")]                    // R1-IMPL-ASSUMPTION-FORMS-001 and -005
    [InlineData("(|| (! (isempty s)) (== s STR:\"\"))")]   // R1-IMPL-ASSUMPTION-FORMS-003
    public void NullableStringPrecondition_IsAssumedNotProven(string implementerPrecondition)
    {
        // Oracle witness (x = 0, s = null): the interface accepts it; the implementer's
        // precondition throws (s.Length) or is false (!IsNullOrEmpty(null) || null == "").
        string? s = null;
        Assert.Throws<NullReferenceException>(() => s!.Length >= 0);
        Assert.False(!string.IsNullOrEmpty(s) || s == "");

        var diagnostics = Compile(StringSourceTemplate.Replace("IMPLPRE", implementerPrecondition));
        AssertPreconditionWeakeningAssumed(diagnostics, "string-model");
    }

    [Fact]
    public void ModuloByPossiblyZeroDivisor_IsAssumedNotProven()
    {
        // R1-IMPL-DIVISION-TOTALIZED-001, oracle witness (x = 3, y = 0): x % y throws.
        int x = 3, y = 0;
        Assert.Throws<DivideByZeroException>(() => x % y > -2);

        var diagnostics = Compile(DivisionSource);
        AssertPreconditionWeakeningAssumed(diagnostics, "exceptional-paths:contract-division");
    }

    private const string IntegerTemplate = """
        §M{m1:M}
          §IFACE{i1:IProbe}
            §MT{m1:Run}
              §I{i32:x}
              §I{i32:y}
              §O{i32}
              §Q IFACEPRE
          §CL{c1:Impl:pub}
            §IMPL{IProbe}
            §MT{mt1:Run:pub}
              §I{i32:x}
              §I{i32:y}
              §O{i32}
              §Q IMPLPRE
              §R INT:0
        """;

    private static List<Diagnostic> CompileInteger(string ifacePre, string implPre, string? overflow = null)
    {
        var source = IntegerTemplate.Replace("IFACEPRE", ifacePre).Replace("IMPLPRE", implPre);
        if (overflow != null)
            source = source.Replace("§M{m1:M}", $"§M{{m1:M:overflow={overflow}}}");
        return Compile(source);
    }

    [Fact]
    public void Control_IntegerWeakening_StillProven()
    {
        var diagnostics = CompileInteger("(>= x INT:1)", "(>= x INT:0)");
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ContractInheritanceValid);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }

    [Fact]
    public void Control_DivisionWhoseDivisorTheInterfaceRulesOut_StillProven()
    {
        // The interface admits only y > 0 and x >= 0: x % y cannot throw, and is >= 0.
        var diagnostics = CompileInteger("(&& (> y INT:0) (>= x INT:0))", "(>= (% x y) INT:0)");
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }

    [Fact]
    public void CheckedOverflowInImplementerPrecondition_IsAssumedNotProven()
    {
        // x * 2 is always even under wrapping, so the solver alone "proves" (x*2 != 1);
        // under the default checked module x * 2 throws for x > int.MaxValue / 2.
        int x = int.MaxValue;
        Assert.Throws<OverflowException>(() => checked(x * 2) != 1);

        var diagnostics = CompileInteger("(> x INT:0)", "(!= (* x INT:2) INT:1)");
        AssertPreconditionWeakeningAssumed(diagnostics, "checked-arithmetic");
    }

    [Fact]
    public void UncheckedModule_WrappingArithmetic_StillProven()
    {
        // Under overflow=unchecked the same contract wraps and never throws: proven.
        var diagnostics = CompileInteger("(> x INT:0)", "(!= (* x INT:2) INT:1)", overflow: "unchecked");
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ImplicationProvenByZ3
            && d.Message.StartsWith("Precondition weakening proven", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.ImplicationAssumed);
    }
}
