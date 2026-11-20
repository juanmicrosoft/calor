using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
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

        var module = Parse("module Disposed", out var diagnostics);
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
module Test
  interface IValidator
    func Validate (string:input | (# != null)) → bool
      !
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
module Test
  interface IRepository
    func GetById (int:id) → string
      !
      => result != null
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
module Test
  interface ICalculator
    func Divide (int:a | (# > 0), int:b | (# != 0)) → int
      !
      => result >= 0
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
module Test
  interface IRepository
    func GetById (int:id | (# > 0)) → string
      !
      => result != null
  class SqlRepository public
    implements IRepository
    func GetById public (int:id) → string
      !
      return ""found""
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
module Test
  interface IRepository
    func GetById (int:id | (# > 0)) → string
      !
  class SqlRepository public
    implements IRepository
    func GetById public (int:id | (# > 0)) → string
      !
      return ""found""
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
module Test
  interface IRepository
    func GetById (int:id | (# > 0)) → string
      !
  class SqlRepository public
    implements IRepository
    func GetById public (int:id | (# >= 0)) → string
      !
      return ""found""
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
module Test
  interface IRepository
    func GetById (int:id | (# >= 0)) → string
      !
  class SqlRepository public
    implements IRepository
    func GetById public (int:id | (# > 0)) → string
      !
      return ""found""
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
module Test
  interface IRepository
    func GetCount () → int
      !
      => result > 0
  class SqlRepository public
    implements IRepository
    func GetCount public () → int
      !
      => result >= 0
      return 1
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
module Test
  interface IRepository
    func GetById (int:id) → string
      !
  class SqlRepository public
    implements IRepository
    func GetById public (int:id) → string
      !
      return ""found""
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
module Test
  interface IRepository
    func GetById (int:id | (# > 0)) → string
      !
  class SqlRepository public
    implements IRepository
    func GetById public (int:id) → string
      !
      return ""found""
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
module Test
  interface IRepository
    func GetById (int:id) → string
      !
      => result != null
  class SqlRepository public
    implements IRepository
    func GetById public (int:id) → string
      !
      return ""found""
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
module Test
  interface IRepository
    func GetById (int:id | (# > 0)) → string
      !
      => result != null
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
module Test
  interface IRepository
    func GetById (int:id | (# > 0)) → string
      !
  class SqlRepository public
    implements IRepository
    func GetById public (int:id | (# > 0)) → string
      !
      return ""found""
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
module Test
  interface IReader
    func Read () → string
      !
      => result != null
  interface IWriter
    func Write (string:data | (# != null)) → void
      !
  class FileHandler public
    implements IReader
    implements IWriter
    func Read public () → string
      !
      return ""data""
    func Write public (string:data) → void
      !
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
module Test
  class MyClass public
    implements IExternalInterface
    func DoSomething public () → void
      !
      return ""done""
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
module Test
  interface IRepository
    func GetById (int:id | (# > 0)) → string
      !
  class SqlRepository public
    implements IRepository
    func GetById public (int:id) → string
      !
      return ""found""
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
module Test
  interface IService
    func Process (int:x | (# >= 0)) → int
      !
  class Service public
    implements IService
    func Process public (int:x | ((# >= -10) && (# < 1000))) → int
      !
      return x
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
module Test
  interface IService
    func Process (int:x) → int
      !
      => result > 0
  class Service public
    implements IService
    func Process public (int:x) → int
      !
      => result >= 10
      => result != 999
      return x + 10
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
module Test
  interface IService
    func Process (int:x | (# >= 0)) → int
      !
  class Service public
    implements IService
    func Process public (int:x | ((# >= 10) && (# < 1000))) → int
      !
      return x
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
module Test
  interface IService
    func Process (int:x) → int
      !
      => result >= 100
  class Service public
    implements IService
    func Process public (int:x) → int
      !
      => result > 0
      => result != 0
      return x
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

        // Interface: requires id >= 1
        // Implementer: requires id >= 0  // weaker - accepts more values - VALID
        var source = @"
module Test
  interface IService
    func Process (int:id | (# >= 1)) → int
      !
  class ValidService public
    implements IService
    func Process public (int:id | (# >= 0)) → int
      !
      return id
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

        // Interface: requires id >= 0
        // Implementer: requires id >= 10  // stronger - rejects valid inputs - VIOLATION
        var source = @"
module Test
  interface IService
    func Process (int:id | (# >= 0)) → int
      !
  class InvalidService public
    implements IService
    func Process public (int:id | (# >= 10)) → int
      !
      return id
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

        // Interface: requires x > 0
        // Implementer: requires x > 0 && x < 100  // stronger - adds restriction - VIOLATION
        var source = @"
module Test
  interface IService
    func Process (int:x | (# > 0)) → int
      !
  class InvalidService public
    implements IService
    func Process public (int:x | (# > 0 && # < 100)) → int
      !
      return x
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

        // Interface: => result > 0
        // Implementer: => result >= 10  // stronger - guarantees more - VALID
        var source = @"
module Test
  interface IService
    func Process (int:value) → int
      !
      => result > 0
  class ValidService public
    implements IService
    func Process public (int:value) → int
      !
      => result >= 10
      return value + 10
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

        // Interface: => result >= 10
        // Implementer: => result > 0  // weaker - guarantees less - VIOLATION
        var source = @"
module Test
  interface IService
    func Process (int:value) → int
      !
      => result >= 10
  class InvalidService public
    implements IService
    func Process public (int:value) → int
      !
      => result > 0
      return value + 1
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

        // Interface: requires x > 0
        // Implementer: requires x >= 1  // equivalent for integers - VALID
        // Z3 should prove that x >= 1 implies x > 0 for integers
        var source = @"
module Test
  interface IService
    func Process (int:x | (# > 0)) → int
      !
  class ValidService public
    implements IService
    func Process public (int:x | (# >= 1)) → int
      !
      return x
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
module Test
  interface IService
    func Process (int:id | (# > 0)) → int
      !
  class Service public
    implements IService
    func Process public (int:id | (# > 0)) → int
      !
      return id
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
module Test
  interface IService
    func Process (int:x) → int
      !
  class Service public
    implements IService
    func Process public (int:x | (# > 0)) → int
      !
      return x
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
module Test
  interface IParser
    func Parse (int:value | (# > 0)) → int
      !
    func Parse (string:value | (# != null)) → string
      !
  class Parser public
    implements IParser
    func Parse public (int:value) → int
      !
      return value
    func Parse public (string:value) → string
      !
      return value
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
module Test
  interface IPositive
    func Use (int:value | (# > 0)) → int
      !
  interface INegative
    func Use (int:value | (# < 0)) → int
      !
  class Service public
    implements IPositive
    implements INegative
    func Use public (int:value) → int
      !
      return value
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
module Test
  class Base public
    func Use public virtual (int:value | (# > 0)) → int
      !
      return value
  class Derived public
    extends Base
    func Use public override (int:value) → int
      !
      return value
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
module Test
  interface IService
    func Use (int:x | (# > 0)) → int
      !
  class Service public
    implements IService
    func Use public (int:value) → int
      !
      return value
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
module Test
  interface IService
    func Use (int:value | (# > 0)) → int
      !
  class Base public
    implements IService
    func Use public virtual (int:value) → int
      !
      return value
  class Derived public
    extends Base
    func Use public override (int:value) → int
      !
      return value
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
module Test
  interface IRepository<T>
    func Save (T:value | (# != null)) → T
      !
  class IntRepository public
    implements IRepository<int>
    func Save public (int:value) → int
      !
      return value
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
module Test
  class Base<T> public
    func Save public virtual (T:value | (# != null)) → T
      !
      return value
  class IntRepository public
    extends Base<int>
    func Save public override (int:value) → int
      !
      return value
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
module Test
  interface IService
    func Touch (int:value | (# > 0)) → int
      !
    func Touch (ref int:value | (# >= 0)) → int
      !
  class Service public
    implements IService
    func Touch public (int:value) → int
      !
      return value
    func Touch public (ref int:value) → int
      !
      return value
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
module Test
  interface ITransformer
    func Map<T> (T:item | (# is T), int:limit | (# > 0)) → T
      !
  class Transformer public
    implements ITransformer
    func Map<U> public (U:item, int:limit) → U
      !
      return item
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
module Test
  interface IService
    func Use (int:x | (IsValid(#))) → int
      !
  class Service public
    implements IService
    func Use public (int:value) → int
      !
      return value
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
module Test
  interface IService
    func Use (string:x | ((# ?? ""fallback"") != ""invalid"")) → string
      !
  class Service public
    implements IService
    func Use public (string:value) → string
      !
      return value
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
module Test
  interface IService
    func Use (string:x | (""id:${#}"" != """")) → string
      !
  class Service public
    implements IService
    func Use public (string:value) → string
      !
      return value
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
module Test
  interface IService
    func Use (object:x | (# is int value && value > 0)) → bool
      !
  class Service public
    implements IService
    func Use public (object:value) → bool
      !
      return true
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
module Test
  interface IFoo<T>
    func Use (T:x | (# is T)) → T
      !
  class Base<U> public
    implements IFoo<U>
    func Use public virtual (U:x) → U
      !
      return x
  class Derived public
    extends Base<int>
    func Use public override (int:value) → int
      !
      return value
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

        Assert.Equal("int", pattern.TargetType);
    }

    [Fact]
    public void GenericCallTypeArguments_AreSubstituted()
    {
        var source = @"
module Test
  interface IService<T>
    func Use (T:x | (Check<T>(#))) → T
      !
  class IntService public
    implements IService<int>
    func Use public (int:value) → int
      !
      return value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags);
        var result = checker.Check(module);
        var inherited = Assert.Single(result.InheritedContracts).Value;
        var call = Assert.IsType<CallExpressionNode>(
            Assert.Single(inherited.Preconditions).Condition);

        Assert.Equal("int", Assert.Single(call.TypeArguments!));
    }

    [Fact]
    public void GenericConstructorTypeArguments_AreSubstituted()
    {
        var source = @"
module Test
  interface IService<T>
    func Use (T:x) → T
      !
      requires new Box<T>() != null
  class IntService public
    implements IService<int>
    func Use public (int:value) → int
      !
      return value
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

        Assert.Equal("Box<int>", creation.TypeName);
        Assert.Equal("new Box<int>()", creation.Accept(new CSharpEmitter()));
        var probe = "public class Box<T> {} public static class Probe { public static string Run() { return ("
            + creation.Accept(new CSharpEmitter()) + ").GetType().GenericTypeArguments[0].FullName!; } }";
        Assert.Equal("System.Int32", InteropScope.RefOutImplementationTests.Run(probe));
    }

    [Fact]
    public void ExplicitInterfaceImplementation_InheritsContract()
    {
        var source = @"
module Test
  interface IService
    func Use (int:value | (# > 0)) → int
      !
  class Service public
    implements IService
    func Use private (int:value) → int
      return value
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors);
        // Explicit-interface spelling is not admitted native syntax; preserve this checker-only AST witness.
        var type = Assert.Single(module.Classes);
        var method = Assert.Single(type.Methods);
        var explicitMethod = new MethodNode(method.Span, method.LocalId, "IService.Use", method.Visibility,
            method.Modifiers, method.TypeParameters, method.Parameters, method.Output, method.Effects,
            method.Preconditions, method.Postconditions, method.Body, method.Attributes);
        var explicitType = new ClassDefinitionNode(type.Span, type.LocalId, type.Name,
            type.IsAbstract, type.IsSealed, type.IsPartial, type.IsStatic, type.BaseClass,
            type.ImplementedInterfaces, type.TypeParameters, type.Fields, type.Properties, type.Constructors,
            [explicitMethod], type.Events, type.OperatorOverloads, type.Attributes, type.CSharpAttributes,
            visibility: type.Visibility);
        module = new ModuleNode(module.Span, module.Id, module.Name, module.Usings, module.Interfaces,
            [explicitType], module.Functions, module.Attributes);
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
module Test
  interface IService
    func Use (int:limit | (exists int:i where i >= 0 : i < #)) → int
      !
  class Service public
    implements IService
    func Use public (int:i) → int
      !
      return i
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
module Test
  interface IService
    func Use (int:value | (# > 0)) → int
      !
  class Base public
    func Use public virtual (int:value) → int
      !
      return value
  class Derived public
    extends Base
    implements IService
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
module Test
  interface IPositive
    func Use (int:value | (# > 0)) → int
      !
      => result > 0
  interface INegative
    func Use (int:value | (# < 0)) → int
      !
      => result < 0
  class Service public
    implements IPositive
    implements INegative
    func Use public (int:value) → int
      !
      return value
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
module Test
  interface IPositive
    func Get () → int
      !
      => result > 0
  interface INegative
    func Get () → int
      !
      => result < 0
  class Service public
    implements IPositive
    implements INegative
    func Get public () → int
      !
      return 1
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

    [Theory]
    [InlineData(0, "Requires", false)]
    [InlineData(0, "Requires", true)]
    [InlineData(1, "Ensures", false)]
    [InlineData(1, "Ensures", true)]
    [InlineData(2, "1", false)]
    [InlineData(2, "1", true)]
    public void NativeInheritedContracts_ExecuteReboundGuardsOnTheClr(int value, string expected, bool roundtrip)
    {
        const string source = """
            module Inheritance
              interface IService
                func Use (int:x | (# > 0)) → int
                  !
                  => result > 0
              class Service public
                implements IService
                func Use public (int:value) → int
                  !
                  return value - 1
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        if (roundtrip)
        {
            module = Parse(new CalorEmitter().Emit(module), out diagnostics);
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        }
        var checkDiagnostics = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiagnostics);
        var inherited = checker.Check(module);
        Assert.False(inherited.HasViolations, string.Join("\n", checkDiagnostics));
        var contracts = Assert.Single(inherited.InheritedContracts).Value;
        Assert.Single(contracts.Preconditions);
        Assert.Single(contracts.Postconditions);
        var generated = new CSharpEmitter(ContractMode.Debug, null, inherited).Emit(module);
        var probe = $$"""

            public static class Probe
            {
                public static string Run()
                {
                    try { return ((IService)new Service()).Use({{value}}).ToString(); }
                    catch (Calor.Runtime.ContractViolationException error) { return error.Kind.ToString(); }
                }
            }
            """;
        Assert.Equal(expected, InteropScope.RefOutImplementationTests.Run(generated + probe));
    }
}
