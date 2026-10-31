using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for contract inheritance from interfaces to implementing classes.
/// </summary>
public class ContractInheritanceTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    [Fact]
    public void CheckerDisposal_ReleasesItsRootedNativeContext()
    {
        using var checker = new ContractInheritanceChecker(new DiagnosticBag());
        var prover = Assert.IsType<Z3ImplicationProver>(
            typeof(ContractInheritanceChecker)
                .GetField("_z3Prover", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(checker));
        var context = ReadProverContext(prover);
        Assert.NotEqual(IntPtr.Zero, ReadNativeContextHandle(context));

        checker.Dispose();
        Assert.Equal(IntPtr.Zero, ReadNativeContextHandle(context));
        checker.Dispose();
        Assert.Equal(IntPtr.Zero, ReadNativeContextHandle(context));

        var module = Parse("§M{Disposed}", out var diagnostics);
        Assert.False(diagnostics.HasErrors);
        Assert.Throws<ObjectDisposedException>(() => checker.Check(module));
        GC.KeepAlive(context);
    }

    [Fact]
    public void BorrowedProverDisposal_PreservesTheCallersContext()
    {
        using var context = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(context);
        var handle = ReadNativeContextHandle(context);
        Assert.NotEqual(IntPtr.Zero, handle);

        prover.Dispose();
        Assert.Equal(handle, ReadNativeContextHandle(context));
        using var expression = context.MkTrue();
        Assert.True(expression.IsTrue);

        var condition = new BoolLiteralNode(default, true);
        Assert.Throws<ObjectDisposedException>(() =>
            prover.ProveImplication([], condition, condition));
        Assert.Throws<ObjectDisposedException>(() =>
            prover.CheckPreconditionWeakening([], condition, condition));
        Assert.Throws<ObjectDisposedException>(() =>
            prover.CheckPostconditionStrengthening([], "bool", condition, condition));
    }

    [Fact]
    public void OwnedProverDisposal_ReleasesTheContextOnlyOnce()
    {
        using var context = Z3ContextFactory.Create();
        using var prover = new Z3ImplicationProver(context, timeoutMs: 1000, ownsContext: true);
        Assert.NotEqual(IntPtr.Zero, ReadNativeContextHandle(context));

        prover.Dispose();
        Assert.Equal(IntPtr.Zero, ReadNativeContextHandle(context));
        prover.Dispose();
        Assert.Equal(IntPtr.Zero, ReadNativeContextHandle(context));
        GC.KeepAlive(context);
    }

    private static Microsoft.Z3.Context ReadProverContext(Z3ImplicationProver prover)
        => Assert.IsType<Microsoft.Z3.Context>(
            typeof(Z3ImplicationProver)
                .GetField("_ctx", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(prover));

    private static IntPtr ReadNativeContextHandle(Microsoft.Z3.Context context)
        // Inspect the pinned binding's handle; invoking Z3 after disposal can crash the host.
        => Assert.IsType<IntPtr>(
            typeof(Microsoft.Z3.Context)
                .GetField("m_ctx", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(context));

    #region Parsing Tests

    [Fact]
    public void Parser_ParsesInterfaceMethodWithPrecondition()
    {
        var source = @"
§M{Test}
  §IFACE{IValidator}
      §MT{Validate}
          §I{str:input}
          §O{bool}
          §Q (!= input null)
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Interfaces);

        var iface = module.Interfaces[0];
        Assert.Single(iface.Methods);

        var method = iface.Methods[0];
        Assert.Single(method.Preconditions);
        Assert.Empty(method.Postconditions);
        Assert.True(method.HasContracts);
    }

    [Fact]
    public void Parser_ParsesInterfaceMethodWithPostcondition()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §S (!= result null)
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var method = module.Interfaces[0].Methods[0];
        Assert.Empty(method.Preconditions);
        Assert.Single(method.Postconditions);
        Assert.True(method.HasContracts);
    }

    [Fact]
    public void Parser_ParsesInterfaceMethodWithMultipleContracts()
    {
        var source = @"
§M{Test}
  §IFACE{ICalculator}
      §MT{Divide}
          §I{i32:a}
          §I{i32:b}
          §O{i32}
          §Q (> a INT:0)
          §Q (!= b INT:0)
          §S (>= result INT:0)
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var method = module.Interfaces[0].Methods[0];
        Assert.Equal(2, method.Preconditions.Count);
        Assert.Single(method.Postconditions);
    }

    #endregion

    #region Contract Inheritance Tests

    [Fact]
    public void ContractInheritanceChecker_InheritsContractsWhenImplementerHasNone()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
          §S (!= result null)
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetById:pub}
          §I{i32:id}
          §O{str}
          §R ""found""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        // Should have inherited contracts
        var inherited = result.GetInheritedContracts("SqlRepository", "GetById");
        Assert.NotNull(inherited);
        Assert.Equal("IRepository", inherited!.InterfaceName);
        Assert.Single(inherited.Preconditions);
        Assert.Single(inherited.Postconditions);

        // Should have info diagnostic
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.InheritedContracts);
    }

    [Fact]
    public void ContractInheritanceChecker_ValidWhenContractsMatch()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetById:pub}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
          §R ""found""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        Assert.False(result.HasViolations);
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.ContractInheritanceValid);
    }

    [Fact]
    public void ContractInheritanceChecker_ValidWithWeakerPrecondition()
    {
        // Weaker precondition (>= instead of >) is OK
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetById:pub}
          §I{i32:id}
          §O{str}
          §Q (>= id INT:0)
          §R ""found""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        // Should be valid (weaker precondition is OK per LSP)
        Assert.False(result.HasViolations);
    }

    [Fact]
    public void ContractInheritanceChecker_ErrorWithStrongerPrecondition()
    {
        // Stronger precondition (> instead of >=) is an LSP violation
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §Q (>= id INT:0)
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetById:pub}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
          §R ""found""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        // Should have an LSP violation
        Assert.True(result.HasViolations);
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.StrongerPrecondition);
    }

    [Fact]
    public void ContractInheritanceChecker_ErrorWithWeakerPostcondition()
    {
        // Weaker postcondition (>= instead of >) is an LSP violation
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetCount}
          §O{i32}
          §S (> result INT:0)
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetCount:pub}
          §O{i32}
          §S (>= result INT:0)
          §R INT:1
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        // Should have an LSP violation
        Assert.True(result.HasViolations);
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.WeakerPostcondition);
    }

    [Fact]
    public void ContractInheritanceChecker_NoContractsNoIssues()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetById:pub}
          §I{i32:id}
          §O{str}
          §R ""found""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        // Should have no issues
        Assert.False(result.HasViolations);
        Assert.Empty(result.InheritedContracts);
    }

    #endregion

    #region Emitter Tests

    [Fact]
    public void Emitter_EmitsInheritedPrecondition()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetById:pub}
          §I{i32:id}
          §O{str}
          §R ""found""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var inheritanceResult = checker.Check(module);

        var emitter = new CSharpEmitter(ContractMode.Debug, null, inheritanceResult);
        var code = emitter.Emit(module);

        // Should contain inherited contract comment
        Assert.Contains("// Inherited from IRepository.GetById", code);
        // Should contain the precondition check
        Assert.Contains("(id > 0)", code);
        Assert.Contains("ContractViolationException", code);
    }

    [Fact]
    public void Emitter_EmitsInheritedPostcondition()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §S (!= result null)
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetById:pub}
          §I{i32:id}
          §O{str}
          §R ""found""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var inheritanceResult = checker.Check(module);

        var emitter = new CSharpEmitter(ContractMode.Debug, null, inheritanceResult);
        var code = emitter.Emit(module);

        // Should contain inherited contract comment
        Assert.Contains("// Inherited from IRepository.GetById", code);
        // Should contain the postcondition check
        Assert.Contains("__calorPostconditionResult", code);
    }

    [Fact]
    public void Emitter_EmitsInterfaceMethodContractsAsXmlComments()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
          §S (!= result null)
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        // Should contain XML comments for contracts
        Assert.Contains("/// <remarks>Requires:", code);
        Assert.Contains("/// <remarks>Ensures:", code);
    }

    [Fact]
    public void Emitter_DoesNotEmitInheritedWhenMethodHasOwnContracts()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetById:pub}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
          §R ""found""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var inheritanceResult = checker.Check(module);

        var emitter = new CSharpEmitter(ContractMode.Debug, null, inheritanceResult);
        var code = emitter.Emit(module);

        // Should NOT contain inherited contract comment (method has its own)
        Assert.DoesNotContain("// Inherited from", code);
        // But should still have the precondition check
        Assert.Contains("(id > 0)", code);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ContractInheritanceChecker_HandlesMultipleInterfaces()
    {
        var source = @"
§M{Test}
  §IFACE{IReader}
      §MT{Read}
          §O{str}
          §S (!= result null)
  §IFACE{IWriter}
      §MT{Write}
          §I{str:data}
          §Q (!= data null)
  §CL{FileHandler:pub}
      §IMPL{IReader}
      §IMPL{IWriter}
      §MT{Read:pub}
          §O{str}
          §R ""data""
      §MT{Write:pub}
          §I{str:data}
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        // Should inherit from both interfaces
        var readContracts = result.GetInheritedContracts("FileHandler", "Read");
        Assert.NotNull(readContracts);
        Assert.Equal("IReader", readContracts!.InterfaceName);

        var writeContracts = result.GetInheritedContracts("FileHandler", "Write");
        Assert.NotNull(writeContracts);
        Assert.Equal("IWriter", writeContracts!.InterfaceName);
    }

    [Fact]
    public void ContractInheritanceChecker_HandlesExternalInterface()
    {
        // When interface is not in the module (external), no checking occurs
        var source = @"
§M{Test}
  §CL{MyClass:pub}
      §IMPL{IExternalInterface}
      §MT{DoSomething:pub}
          §R ""done""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        // No violations for external interface
        Assert.False(result.HasViolations);
        Assert.Empty(result.InheritedContracts);
    }

    [Fact]
    public void Emitter_ContractModeOffSkipsInheritedContracts()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}
      §MT{GetById}
          §I{i32:id}
          §O{str}
          §Q (> id INT:0)
  §CL{SqlRepository:pub}
      §IMPL{IRepository}
      §MT{GetById:pub}
          §I{i32:id}
          §O{str}
          §R ""found""
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var inheritanceResult = checker.Check(module);

        // Use ContractMode.Off
        var emitter = new CSharpEmitter(ContractMode.Off, null, inheritanceResult);
        var code = emitter.Emit(module);

        // Should not contain contract checks
        Assert.DoesNotContain("ContractViolationException", code);
    }

    #endregion

    #region Z3 Integration Tests

    [SkippableFact]
    public void Z3_MultiplePreconditions_AreCheckedAsConjunction()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // The second clause makes the complete implementation requirement stronger,
        // despite the first clause being individually weaker.
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:x}
          §O{i32}
          §Q (>= x INT:0)
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:x}
          §O{i32}
          §Q (>= x INT:-10)
          §Q (< x INT:1000)
          §R x
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        Assert.True(result.HasViolations,
            "Expected violation: the complete implementation conjunction is stronger. " +
            $"Diagnostics: {string.Join("; ", checkDiags.Select(d => d.Message))}");
        Assert.Contains(
            checkDiags,
            diagnostic => diagnostic.Code == DiagnosticCode.StrongerPrecondition);
    }

    [SkippableFact]
    public void Z3_ValidWithMultiplePostconditions_AtLeastOneMatches()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface has one postcondition, implementer has two.
        // The first implementer postcondition (>= result 10) is stronger than interface (> result 0).
        // The second implementer postcondition (!= result 999) does NOT imply the interface postcondition.
        // With correct "at least one matching" semantics, this should be VALID.
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:x}
          §O{i32}
          §S (> result INT:0)
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:x}
          §O{i32}
          §S (>= result INT:10)
          §S (!= result INT:999)
          §R (+ x INT:10)
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should be valid - the first postcondition (>= result 10) implies interface (> result 0)
        // The second postcondition (!= result 999) should NOT cause a false positive
        Assert.False(result.HasViolations,
            "Expected no violations: at least one postcondition matches. " +
            $"Diagnostics: {string.Join("; ", checkDiags.Select(d => d.Message))}");

        // Should have Z3 proven diagnostic for the matching postcondition
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.ImplicationProvenByZ3);
    }

    [SkippableFact]
    public void Z3_ViolationWithMultiplePreconditions_NoneMatch()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface requires (>= x 0)
        // Implementer has two preconditions, but NEITHER satisfies the interface:
        // - (>= x 10) is STRONGER than interface (rejects values 0-9)
        // - (< x 1000) doesn't relate to the lower bound at all
        // This should report a violation because no implementer precondition is weaker-or-equal.
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:x}
          §O{i32}
          §Q (>= x INT:0)
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:x}
          §O{i32}
          §Q (>= x INT:10)
          §Q (< x INT:1000)
          §R x
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should have LSP violation - neither precondition satisfies interface requirement
        Assert.True(result.HasViolations,
            "Expected violation: no precondition is weaker than interface. " +
            $"Diagnostics: {string.Join("; ", checkDiags.Select(d => d.Message))}");
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.StrongerPrecondition);
    }

    [SkippableFact]
    public void Z3_ViolationWithMultiplePostconditions_NoneMatch()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface requires (>= result 100)
        // Implementer has two postconditions, but NEITHER satisfies the interface:
        // - (> result 0) is WEAKER than interface (doesn't guarantee >= 100)
        // - (!= result 0) doesn't guarantee >= 100 either
        // This should report a violation because no implementer postcondition implies the interface.
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:x}
          §O{i32}
          §S (>= result INT:100)
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:x}
          §O{i32}
          §S (> result INT:0)
          §S (!= result INT:0)
          §R x
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should have LSP violation - neither postcondition implies interface requirement
        Assert.True(result.HasViolations,
            "Expected violation: no postcondition implies interface guarantee. " +
            $"Diagnostics: {string.Join("; ", checkDiags.Select(d => d.Message))}");
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.WeakerPostcondition);
    }

    [SkippableFact]
    public void Z3_ValidatesWeakerPrecondition_DifferentConstants()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface: §Q (>= id 1)
        // Implementer: §Q (>= id 0)  // weaker - accepts more values - VALID
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:id}
          §O{i32}
          §Q (>= id INT:1)
  §CL{ValidService:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:id}
          §O{i32}
          §Q (>= id INT:0)
          §R id
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should be valid - weaker precondition is OK
        Assert.False(result.HasViolations);
        // Check for Z3 proven diagnostic
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.ImplicationProvenByZ3);
    }

    [SkippableFact]
    public void Z3_DetectsStrongerPrecondition_DifferentConstants()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface: §Q (>= id 0)
        // Implementer: §Q (>= id 10)  // stronger - rejects valid inputs - VIOLATION
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:id}
          §O{i32}
          §Q (>= id INT:0)
  §CL{InvalidService:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:id}
          §O{i32}
          §Q (>= id INT:10)
          §R id
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should have LSP violation
        Assert.True(result.HasViolations);
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.StrongerPrecondition);
    }

    [SkippableFact]
    public void Z3_DetectsStrongerPrecondition_Conjunction()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface: §Q (> x 0)
        // Implementer: §Q (&& (> x 0) (< x 100))  // stronger - adds restriction - VIOLATION
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:x}
          §O{i32}
          §Q (> x INT:0)
  §CL{InvalidService:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:x}
          §O{i32}
          §Q (&& (> x INT:0) (< x INT:100))
          §R x
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should have LSP violation - conjunction is stronger than single condition
        Assert.True(result.HasViolations);
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.StrongerPrecondition);
    }

    [SkippableFact]
    public void Z3_ValidatesPostconditionStrengthening()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface: §S (> result 0)
        // Implementer: §S (>= result 10)  // stronger - guarantees more - VALID
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:value}
          §O{i32}
          §S (> result INT:0)
  §CL{ValidService:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:value}
          §O{i32}
          §S (>= result INT:10)
          §R (+ value INT:10)
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should be valid - stronger postcondition is OK
        Assert.False(result.HasViolations);
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.ImplicationProvenByZ3);
    }

    [SkippableFact]
    public void Z3_DetectsWeakerPostcondition()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface: §S (>= result 10)
        // Implementer: §S (> result 0)  // weaker - guarantees less - VIOLATION
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:value}
          §O{i32}
          §S (>= result INT:10)
  §CL{InvalidService:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:value}
          §O{i32}
          §S (> result INT:0)
          §R (+ value INT:1)
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should have LSP violation
        Assert.True(result.HasViolations);
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.WeakerPostcondition);
    }

    [SkippableFact]
    public void Z3_ProvesArithmeticImplication()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface: §Q (> x 0)
        // Implementer: §Q (>= x 1)  // equivalent for integers - VALID
        // Z3 should prove that x >= 1 implies x > 0 for integers
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:x}
          §O{i32}
          §Q (> x INT:0)
  §CL{ValidService:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:x}
          §O{i32}
          §Q (>= x INT:1)
          §R x
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should be valid - (x >= 1) is equivalent to (x > 0) for integers
        // Z3 can prove this arithmetic relationship
        Assert.False(result.HasViolations);
    }

    [Fact]
    public void FallsBack_WhenZ3Disabled()
    {
        // When Z3 is explicitly disabled, should fall back to heuristics
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:id}
          §O{i32}
          §Q (> id INT:0)
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:id}
          §O{i32}
          §Q (> id INT:0)
          §R id
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: false);
        var result = checker.Check(module);

        // Should work without Z3
        Assert.False(result.HasViolations);
        // Should report Z3 unavailable
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.Z3UnavailableForInheritance);
    }

    [SkippableFact]
    public void UnconstrainedInterface_RejectsImplementationPrecondition()
    {
        Skip.IfNot(
            Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable,
            "Z3 not available");
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Process}
          §I{i32:x}
          §O{i32}
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Process:pub}
          §I{i32:x}
          §O{i32}
          §Q (> x INT:0)
          §R x
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        var result = checker.Check(module);

        Assert.True(result.HasViolations);
        Assert.Contains(
            checkDiags,
            diagnostic => diagnostic.Code == DiagnosticCode.StrongerPrecondition);
    }

    [Fact]
    public void Overloads_KeepDistinctInheritedContracts()
    {
        var source = @"
§M{Test}
  §IFACE{IParser}
      §MT{Parse}
          §I{i32:value}
          §O{i32}
          §Q (> value INT:0)
      §MT{Parse}
          §I{str:value}
          §O{str}
          §Q (!= value null)
  §CL{Parser:pub}
      §IMPL{IParser}
      §MT{Parse:pub}
          §I{i32:value}
          §O{i32}
          §R value
      §MT{Parse:pub}
          §I{str:value}
          §O{str}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        var result = checker.Check(module);

        Assert.Equal(2, result.InheritedContracts.Count);
        Assert.NotNull(result.GetInheritedContracts(
            "Parser",
            module.Classes[0].Methods[0]));
        Assert.NotNull(result.GetInheritedContracts(
            "Parser",
            module.Classes[0].Methods[1]));
        Assert.Null(result.GetInheritedContracts("Parser", "Parse"));
    }

    [Fact]
    public void MultipleInterfaces_CombineInheritedPreconditionsAsDisjunction()
    {
        var source = @"
§M{Test}
  §IFACE{IPositive}
      §MT{Use}
          §I{i32:value}
          §O{i32}
          §Q (> value INT:0)
  §IFACE{INegative}
      §MT{Use}
          §I{i32:value}
          §O{i32}
          §Q (< value INT:0)
  §CL{Service:pub}
      §IMPL{IPositive}
      §IMPL{INegative}
      §MT{Use:pub}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        var inherited = result.GetInheritedContracts(
            "Service",
            module.Classes[0].Methods[0]);
        var precondition = Assert.Single(inherited!.Preconditions);
        var disjunction = Assert.IsType<BinaryOperationNode>(
            precondition.Condition);
        Assert.Equal(BinaryOperator.Or, disjunction.Operator);

        var code = new CSharpEmitter(
            ContractMode.Debug,
            null,
            result).Emit(module);
        Assert.Contains("value < 0 || value > 0", code);
    }

    [Fact]
    public void BaseClassContracts_AreInheritedBySignature()
    {
        var source = @"
§M{Test}
  §CL{Base:pub}
      §MT{Use:pub:virt}
          §I{i32:value}
          §O{i32}
          §Q (> value INT:0)
          §R value
  §CL{Derived:pub}
      §EXT{Base}
      §MT{Use:pub:over}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        var result = checker.Check(module);

        var derived = module.Classes.Single(classNode => classNode.Name == "Derived");
        var inherited = result.GetInheritedContracts(
            "Derived",
            Assert.Single(derived.Methods));
        Assert.NotNull(inherited);
        Assert.Equal("Base", inherited!.InterfaceName);
    }

    [Fact]
    public void InheritedContracts_RebindParameterNamesPositionally()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Use}
          §I{i32:x}
          §O{i32}
          §Q (> x INT:0)
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Use:pub}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);

        var code = new CSharpEmitter(
            ContractMode.Debug,
            null,
            result).Emit(module);

        Assert.Contains("(value > 0)", code);
        Assert.DoesNotContain("(x > 0)", code);
    }

    [Fact]
    public void IntermediateOverride_PreservesEffectiveInterfaceContract()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Use}
          §I{i32:value}
          §O{i32}
          §Q (> value INT:0)
  §CL{Base:pub}
      §IMPL{IService}
      §MT{Use:pub:virt}
          §I{i32:value}
          §O{i32}
          §R value
  §CL{Derived:pub}
      §EXT{Base}
      §MT{Use:pub:over}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var derived = module.Classes.Single(classNode => classNode.Name == "Derived");

        var inherited = result.GetInheritedContracts(
            "Derived",
            Assert.Single(derived.Methods));

        Assert.NotNull(inherited);
        Assert.Single(inherited!.Preconditions);
    }

    [Fact]
    public void GenericInterface_ResolvesClosedMethodSignature()
    {
        var source = @"
§M{Test}
  §IFACE{IRepository}<T>
      §MT{Save}
          §I{T:value}
          §O{T}
          §Q (!= value null)
  §CL{IntRepository:pub}
      §IMPL{IRepository<i32>}
      §MT{Save:pub}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(
            parseDiags.HasErrors,
            string.Join("\n", parseDiags.Select(diagnostic => diagnostic.Message)));
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        var result = checker.Check(module);

        Assert.True(
            result.InheritedContracts.Count == 1,
            $"Interfaces: {string.Join(", ", module.Classes.Single().ImplementedInterfaces)}; "
            + $"type parameters: {string.Join(", ", module.Interfaces.Single().TypeParameters.Select(parameter => parameter.Name))}; "
            + $"contract parameter: {module.Interfaces.Single().Methods.Single().Parameters.Single().TypeName}; "
            + $"implementation parameter: {module.Classes.Single().Methods.Single().Parameters.Single().TypeName}");
    }

    [Fact]
    public void GenericBaseClass_ResolvesClosedMethodSignature()
    {
        var source = @"
§M{Test}
  §CL{Base:pub}<T>
      §MT{Save:pub:virt}
          §I{T:value}
          §O{T}
          §Q (!= value null)
          §R value
  §CL{IntRepository:pub}
      §EXT{Base<i32>}
      §MT{Save:pub:over}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        var result = checker.Check(module);

        Assert.Single(result.InheritedContracts);
    }

    [Fact]
    public void ParameterModifiers_DisambiguateInheritedOverloads()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Touch}
          §I{i32:value}
          §O{i32}
          §Q (> value INT:0)
      §MT{Touch}
          §I{i32:value:ref}
          §O{i32}
          §Q (>= value INT:0)
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Touch:pub}
          §I{i32:value}
          §O{i32}
          §R value
      §MT{Touch:pub}
          §I{i32:value:ref}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        var result = checker.Check(module);

        Assert.Equal(2, result.InheritedContracts.Count);
    }

    [Fact]
    public void GenericMethodTypeParameters_MatchByPosition()
    {
        var source = @"
§M{Test}
  §IFACE{ITransformer}
      §MT{Map}<T>
          §I{T:item}
          §I{i32:limit}
          §O{T}
          §Q (> limit INT:0)
          §Q (is item T)
  §CL{Transformer:pub}
      §IMPL{ITransformer}
      §MT{Map:pub}<U>
          §I{U:item}
          §I{i32:limit}
          §O{U}
          §R item
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        var result = checker.Check(module);

        var inherited = Assert.Single(result.InheritedContracts).Value;
        var conjunction = Assert.IsType<BinaryOperationNode>(
            Assert.Single(inherited.Preconditions).Condition);
        var typeCheck = Assert.IsType<IsPatternNode>(conjunction.Right);
        Assert.Equal("U", typeCheck.TargetType);
    }

    [Fact]
    public void InheritedContractCalls_RebindArgumentNames()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Use}
          §I{i32:x}
          §O{i32}
          §Q (CALL IsValid x)
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Use:pub}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var inherited = Assert.Single(result.InheritedContracts).Value;
        var call = Assert.IsType<CallExpressionNode>(
            Assert.Single(inherited.Preconditions).Condition);

        Assert.Equal("value", Assert.IsType<ReferenceNode>(Assert.Single(call.Arguments)).Name);
    }

    [Fact]
    public void NestedNullCoalesce_RebindsParameterNames()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Use}
          §I{str:x}
          §O{str}
          §Q (!= (?? x STR:""fallback"") STR:""invalid"")
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Use:pub}
          §I{str:value}
          §O{str}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var inherited = Assert.Single(result.InheritedContracts).Value;
        var comparison = Assert.IsType<BinaryOperationNode>(
            Assert.Single(inherited.Preconditions).Condition);
        var coalesce = Assert.IsType<NullCoalesceNode>(comparison.Left);

        Assert.Equal("value", Assert.IsType<ReferenceNode>(coalesce.Left).Name);
    }

    [Fact]
    public void InterpolatedString_RebindsHoleReferences()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Use}
          §I{str:x}
          §O{str}
          §Q (!= §INTERP ""id:"" §EXP x §/INTERP STR:"""")
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Use:pub}
          §I{str:value}
          §O{str}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var inherited = Assert.Single(result.InheritedContracts).Value;
        var comparison = Assert.IsType<BinaryOperationNode>(
            Assert.Single(inherited.Preconditions).Condition);
        var interpolation = Assert.IsType<InterpolatedStringNode>(comparison.Left);
        var hole = Assert.IsType<InterpolatedStringExpressionNode>(
            interpolation.Parts.Single(part =>
                part is InterpolatedStringExpressionNode));

        Assert.Equal("value", Assert.IsType<ReferenceNode>(hole.Expression).Name);
    }

    [Fact]
    public void IsPatternBinding_AvoidsParameterCapture()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Use}
          §I{object:x}
          §O{bool}
          §Q (&& (is x i32 value) (> value INT:0))
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Use:pub}
          §I{object:value}
          §O{bool}
          §R true
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var inherited = Assert.Single(result.InheritedContracts).Value;
        var conjunction = Assert.IsType<BinaryOperationNode>(
            Assert.Single(inherited.Preconditions).Condition);
        var pattern = Assert.IsType<IsPatternNode>(conjunction.Left);
        var comparison = Assert.IsType<BinaryOperationNode>(conjunction.Right);

        Assert.NotEqual("value", pattern.VariableName);
        Assert.Equal(
            pattern.VariableName,
            Assert.IsType<ReferenceNode>(comparison.Left).Name);
    }

    [Fact]
    public void ClosedGenericBase_ComposesInheritedInterfaceSubstitutions()
    {
        var source = @"
§M{Test}
  §IFACE{IFoo}<T>
      §MT{Use}
          §I{T:x}
          §O{T}
          §Q (is x T)
  §CL{Base:pub}<U>
      §IMPL{IFoo<U>}
      §MT{Use:pub:virt}
          §I{U:x}
          §O{U}
          §R x
  §CL{Derived:pub}
      §EXT{Base<i32>}
      §MT{Use:pub:over}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var derived = module.Classes.Single(classNode => classNode.Name == "Derived");
        var inherited = result.GetInheritedContracts(
            "Derived",
            Assert.Single(derived.Methods));
        var pattern = Assert.IsType<IsPatternNode>(
            Assert.Single(inherited!.Preconditions).Condition);

        Assert.Equal("i32", pattern.TargetType);
    }

    [Fact]
    public void GenericCallTypeArguments_AreSubstituted()
    {
        var source = @"
§M{Test}
  §IFACE{IService}<T>
      §MT{Use}
          §I{T:x}
          §O{T}
          §Q §C{Check<T>} §A x §/C
  §CL{IntService:pub}
      §IMPL{IService<i32>}
      §MT{Use:pub}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var inherited = Assert.Single(result.InheritedContracts).Value;
        var call = Assert.IsType<CallExpressionNode>(
            Assert.Single(inherited.Preconditions).Condition);

        Assert.Equal("i32", Assert.Single(call.TypeArguments!));
    }

    [Fact]
    public void GenericConstructorTypeArguments_AreSubstituted()
    {
        var source = @"
§M{Test}
  §IFACE{IService}<T>
      §MT{Use}
          §I{T:x}
          §O{T}
          §Q (!= §NEW{Box<T>} §/NEW null)
  §CL{IntService:pub}
      §IMPL{IService<i32>}
      §MT{Use:pub}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var inherited = Assert.Single(result.InheritedContracts).Value;
        var comparison = Assert.IsType<BinaryOperationNode>(
            Assert.Single(inherited.Preconditions).Condition);
        var creation = Assert.IsType<NewExpressionNode>(comparison.Left);

        Assert.Equal("i32", Assert.Single(creation.TypeArguments));
    }

    [Fact]
    public void ExplicitInterfaceImplementation_InheritsContract()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Use}
          §I{i32:value}
          §O{i32}
          §Q (> value INT:0)
  §CL{Service:pub}
      §IMPL{IService}
      §MT{IService.Use:priv}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        var result = checker.Check(module);

        Assert.Single(result.InheritedContracts);
        Assert.DoesNotContain(
            checkDiags,
            diagnostic => diagnostic.Code == DiagnosticCode.InterfaceMethodNotFound);
    }

    [Fact]
    public void QuantifierRebinding_AvoidsCapturingImplementationParameter()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Use}
          §I{i32:limit}
          §O{i32}
          §Q (exists ((i i32)) (&& (>= i INT:0) (< i limit)))
  §CL{Service:pub}
      §IMPL{IService}
      §MT{Use:pub}
          §I{i32:i}
          §O{i32}
          §R i
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var inherited = Assert.Single(result.InheritedContracts).Value;
        var exists = Assert.IsType<ExistsExpressionNode>(
            Assert.Single(inherited.Preconditions).Condition);
        var body = Assert.IsType<BinaryOperationNode>(exists.Body);
        var comparison = Assert.IsType<BinaryOperationNode>(body.Right);

        Assert.NotEqual("i", Assert.Single(exists.BoundVariables).Name);
        Assert.Equal("i", Assert.IsType<ReferenceNode>(comparison.Right).Name);
        Assert.Equal(
            Assert.Single(exists.BoundVariables).Name,
            Assert.IsType<ReferenceNode>(comparison.Left).Name);
    }

    [Fact]
    public void InterfaceImplementedOnlyByBaseMethod_FailsClosed()
    {
        var source = @"
§M{Test}
  §IFACE{IService}
      §MT{Use}
          §I{i32:value}
          §O{i32}
          §Q (> value INT:0)
  §CL{Base:pub}
      §MT{Use:pub:virt}
          §I{i32:value}
          §O{i32}
          §R value
  §CL{Derived:pub}
      §EXT{Base}
      §IMPL{IService}
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        checker.Check(module);

        Assert.Contains(
            checkDiags,
            diagnostic => diagnostic.Code == DiagnosticCode.InterfaceMethodNotFound
                && diagnostic.IsError);
    }

    [Fact]
    public void DisjointSourcePostconditions_AreQualifiedByTheirPreconditions()
    {
        var source = @"
§M{Test}
  §IFACE{IPositive}
      §MT{Use}
          §I{i32:value}
          §O{i32}
          §Q (> value INT:0)
          §S (> result INT:0)
  §IFACE{INegative}
      §MT{Use}
          §I{i32:value}
          §O{i32}
          §Q (< value INT:0)
          §S (< result INT:0)
  §CL{Service:pub}
      §IMPL{IPositive}
      §IMPL{INegative}
      §MT{Use:pub}
          §I{i32:value}
          §O{i32}
          §R value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var inherited = Assert.Single(result.InheritedContracts).Value;

        Assert.Equal(2, inherited.Postconditions.Count);
        Assert.All(
            inherited.Postconditions,
            contract => Assert.IsType<ImplicationExpressionNode>(contract.Condition));
    }

    [SkippableFact]
    public void IncompatibleInheritedPostconditions_AreDiagnosed()
    {
        Skip.IfNot(
            Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable,
            "Z3 not available");
        var source = @"
§M{Test}
  §IFACE{IPositive}
      §MT{Get}
          §O{i32}
          §S (> result INT:0)
  §IFACE{INegative}
      §MT{Get}
          §O{i32}
          §S (< result INT:0)
  §CL{Service:pub}
      §IMPL{IPositive}
      §IMPL{INegative}
      §MT{Get:pub}
          §O{i32}
          §R INT:1
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);

        var result = checker.Check(module);

        Assert.True(result.HasViolations);
        Assert.Contains(
            checkDiags,
            diagnostic => diagnostic.Code
                == DiagnosticCode.IncompatibleInheritedContracts);
    }

    #endregion
}
