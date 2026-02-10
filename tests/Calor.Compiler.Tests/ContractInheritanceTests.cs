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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
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
        var checker = new ContractInheritanceChecker(checkDiags);
        var inheritanceResult = checker.Check(module);

        // Use ContractMode.Off
        var emitter = new CSharpEmitter(ContractMode.Off, null, inheritanceResult);
        var code = emitter.Emit(module);

        // Should not contain contract checks
        Assert.DoesNotContain("ContractViolationException", code);
    }

    #endregion
}
