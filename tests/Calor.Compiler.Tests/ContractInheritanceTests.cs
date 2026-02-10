using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
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
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Parsing Tests

    [Fact]
    public void Parser_ParsesInterfaceMethodWithPrecondition()
    {
        var source = @"
§M{m001:Test}
§IFACE{i001:IValidator}
  §MT{m001:Validate}
    §I{str:input}
    §O{bool}
    §Q (!= input null)
  §/MT{m001}
§/IFACE{i001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §S (!= result null)
  §/MT{m001}
§/IFACE{i001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:ICalculator}
  §MT{m001:Divide}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §Q (> a INT:0)
    §Q (!= b INT:0)
    §S (>= result INT:0)
  §/MT{m001}
§/IFACE{i001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
    §S (!= result null)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetById:pub}
    §I{i32:id}
    §O{str}
    §R ""found""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetById:pub}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
    §R ""found""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetById:pub}
    §I{i32:id}
    §O{str}
    §Q (>= id INT:0)
    §R ""found""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §Q (>= id INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetById:pub}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
    §R ""found""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetCount}
    §O{i32}
    §S (> result INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetCount:pub}
    §O{i32}
    §S (>= result INT:0)
    §R INT:1
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetById:pub}
    §I{i32:id}
    §O{str}
    §R ""found""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetById:pub}
    §I{i32:id}
    §O{str}
    §R ""found""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §S (!= result null)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetById:pub}
    §I{i32:id}
    §O{str}
    §R ""found""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
        Assert.Contains("__result__ != null", code);
    }

    [Fact]
    public void Emitter_EmitsInterfaceMethodContractsAsXmlComments()
    {
        var source = @"
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
    §S (!= result null)
  §/MT{m001}
§/IFACE{i001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetById:pub}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
    §R ""found""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IReader}
  §MT{m001:Read}
    §O{str}
    §S (!= result null)
  §/MT{m001}
§/IFACE{i001}
§IFACE{i002:IWriter}
  §MT{m002:Write}
    §I{str:data}
    §Q (!= data null)
  §/MT{m002}
§/IFACE{i002}
§CL{c001:FileHandler:pub}
  §IMPL{IReader}
  §IMPL{IWriter}
  §MT{mt001:Read:pub}
    §O{str}
    §R ""data""
  §/MT{mt001}
  §MT{mt002:Write:pub}
    §I{str:data}
  §/MT{mt002}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§CL{c001:MyClass:pub}
  §IMPL{IExternalInterface}
  §MT{mt001:DoSomething:pub}
    §R ""done""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{str}
    §Q (> id INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:SqlRepository:pub}
  §IMPL{IRepository}
  §MT{mt001:GetById:pub}
    §I{i32:id}
    §O{str}
    §R ""found""
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
    public void Z3_ValidWithMultiplePreconditions_AtLeastOneMatches()
    {
        Skip.IfNot(Calor.Compiler.Verification.Z3.Z3ContextFactory.IsAvailable, "Z3 not available");

        // Interface has one precondition, implementer has two.
        // The first implementer precondition (>= x -10) is weaker than interface (>= x 0).
        // The second implementer precondition (< x 1000) does NOT match the interface precondition.
        // With correct "at least one matching" semantics, this should be VALID.
        // With incorrect "all pairs" semantics, this would report a false positive violation.
        var source = @"
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:x}
    §O{i32}
    §Q (>= x INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:Service:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:x}
    §O{i32}
    §Q (>= x INT:-10)
    §Q (< x INT:1000)
    §R x
  §/MT{mt001}
§/CL{c001}
§/M{m001}
";

        var module = Parse(source, out var parseDiags);
        Assert.False(parseDiags.HasErrors, string.Join("\n", parseDiags.Select(d => d.Message)));

        var checkDiags = new DiagnosticBag();
        using var checker = new ContractInheritanceChecker(checkDiags, useZ3: true);
        var result = checker.Check(module);

        // Should be valid - the first precondition (>= x -10) is weaker than interface (>= x 0)
        // The second precondition (< x 1000) should NOT cause a false positive
        Assert.False(result.HasViolations,
            "Expected no violations: at least one precondition matches. " +
            $"Diagnostics: {string.Join("; ", checkDiags.Select(d => d.Message))}");

        // Should have Z3 proven diagnostic for the matching precondition
        Assert.Contains(checkDiags, d => d.Code == DiagnosticCode.ImplicationProvenByZ3);
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:x}
    §O{i32}
    §S (> result INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:Service:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:x}
    §O{i32}
    §S (>= result INT:10)
    §S (!= result INT:999)
    §R (+ x INT:10)
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:x}
    §O{i32}
    §Q (>= x INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:Service:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:x}
    §O{i32}
    §Q (>= x INT:10)
    §Q (< x INT:1000)
    §R x
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:x}
    §O{i32}
    §S (>= result INT:100)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:Service:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:x}
    §O{i32}
    §S (> result INT:0)
    §S (!= result INT:0)
    §R x
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:id}
    §O{i32}
    §Q (>= id INT:1)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:ValidService:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:id}
    §O{i32}
    §Q (>= id INT:0)
    §R id
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:id}
    §O{i32}
    §Q (>= id INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:InvalidService:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:id}
    §O{i32}
    §Q (>= id INT:10)
    §R id
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:x}
    §O{i32}
    §Q (> x INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:InvalidService:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:x}
    §O{i32}
    §Q (&& (> x INT:0) (< x INT:100))
    §R x
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:value}
    §O{i32}
    §S (> result INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:ValidService:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:value}
    §O{i32}
    §S (>= result INT:10)
    §R (+ value INT:10)
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:value}
    §O{i32}
    §S (>= result INT:10)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:InvalidService:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:value}
    §O{i32}
    §S (> result INT:0)
    §R (+ value INT:1)
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:x}
    §O{i32}
    §Q (> x INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:ValidService:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:x}
    §O{i32}
    §Q (>= x INT:1)
    §R x
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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
§M{m001:Test}
§IFACE{i001:IService}
  §MT{m001:Process}
    §I{i32:id}
    §O{i32}
    §Q (> id INT:0)
  §/MT{m001}
§/IFACE{i001}
§CL{c001:Service:pub}
  §IMPL{IService}
  §MT{mt001:Process:pub}
    §I{i32:id}
    §O{i32}
    §Q (> id INT:0)
    §R id
  §/MT{mt001}
§/CL{c001}
§/M{m001}
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

    #endregion
}
