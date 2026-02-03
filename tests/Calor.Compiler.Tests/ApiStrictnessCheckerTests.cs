using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ApiStrictnessCheckerTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Default Mode

    [Fact]
    public void Check_DefaultMode_NoWarningsOnPublicFunction()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var checker = new ApiStrictnessChecker(checkDiagnostics, ApiStrictnessOptions.Default);
        checker.Check(module);

        Assert.DoesNotContain(checkDiagnostics, d => d.Code == DiagnosticCode.MissingDocComment);
    }

    [Fact]
    public void Check_DefaultMode_NoWarningsOnPrivateFunction()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=private}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var checker = new ApiStrictnessChecker(checkDiagnostics, ApiStrictnessOptions.Default);
        checker.Check(module);

        Assert.Empty(checkDiagnostics);
    }

    #endregion

    #region RequireDocs Mode

    [Fact]
    public void Check_RequireDocs_WarnsOnUndocumentedPublicFunction()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var options = new ApiStrictnessOptions { RequireDocs = true };
        var checker = new ApiStrictnessChecker(checkDiagnostics, options);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.MissingDocComment);
    }

    [Fact]
    public void Check_RequireDocs_NoWarningOnPrivateFunction()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=PrivateHelper}{visibility=private}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var options = new ApiStrictnessOptions { RequireDocs = true };
        var checker = new ApiStrictnessChecker(checkDiagnostics, options);
        checker.Check(module);

        // Private functions should not require docs
        Assert.DoesNotContain(checkDiagnostics, d =>
            d.Code == DiagnosticCode.MissingDocComment &&
            d.Message.Contains("function 'PrivateHelper'"));
    }

    [Fact]
    public void Check_RequireDocs_WarnsOnUndocumentedModule()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§END_MODULE{id=m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var options = new ApiStrictnessOptions { RequireDocs = true };
        var checker = new ApiStrictnessChecker(checkDiagnostics, options);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d =>
            d.Code == DiagnosticCode.MissingDocComment &&
            d.Message.Contains("Module"));
    }

    #endregion

    #region StrictApi Mode

    [Fact]
    public void Check_StrictApi_WarnsOnPublicFunctionWithoutContractsOrDocs()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var options = new ApiStrictnessOptions { StrictApi = true };
        var checker = new ApiStrictnessChecker(checkDiagnostics, options);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d =>
            d.Code == DiagnosticCode.MissingDocComment &&
            d.Message.Contains("contracts"));
    }

    [Fact]
    public void Check_StrictApi_NoWarningOnFunctionWithContracts()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Divide}{visibility=public}
  §IN{name=a}{type=INT}
  §IN{name=b}{type=INT}
  §OUT{type=INT}
  §REQUIRES (!= b INT:0)
  §BODY
    §RETURN (/ a b)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var options = new ApiStrictnessOptions { StrictApi = true };
        var checker = new ApiStrictnessChecker(checkDiagnostics, options);
        checker.Check(module);

        // Function has contracts, so strict mode should not complain about missing contracts
        Assert.DoesNotContain(checkDiagnostics, d =>
            d.Code == DiagnosticCode.MissingDocComment &&
            d.Message.Contains("function 'Divide'") &&
            d.Message.Contains("contracts"));
    }

    #endregion

    #region RequireStabilityMarkers Mode

    [Fact]
    public void Check_RequireStabilityMarkers_InfoOnFunctionWithoutSince()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var options = new ApiStrictnessOptions { RequireStabilityMarkers = true };
        var checker = new ApiStrictnessChecker(checkDiagnostics, options);
        checker.Check(module);

        Assert.Contains(checkDiagnostics, d =>
            d.Code == DiagnosticCode.PublicApiChanged &&
            d.Message.Contains("version marker"));
    }

    #endregion

    #region Strict Options Preset

    [Fact]
    public void Check_StrictPreset_ChecksAllRules()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var module = Parse(source, out var parseDiagnostics);
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics.Select(d => d.Message)));

        var checkDiagnostics = new DiagnosticBag();
        var checker = new ApiStrictnessChecker(checkDiagnostics, ApiStrictnessOptions.Strict);
        checker.Check(module);

        // Should have warnings for missing docs and stability markers
        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.MissingDocComment);
        Assert.Contains(checkDiagnostics, d => d.Code == DiagnosticCode.PublicApiChanged);
    }

    #endregion

    #region Breaking Change Detection

    [Fact]
    public void Compare_NoChanges_NoBrokenChanges()
    {
        var source = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §IN{name=x}{type=INT}
  §OUT{type=INT}
  §BODY
    §RETURN x
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var oldModule = Parse(source, out var oldDiagnostics);
        var newModule = Parse(source, out var newDiagnostics);
        Assert.False(oldDiagnostics.HasErrors);
        Assert.False(newDiagnostics.HasErrors);

        var checkDiagnostics = new DiagnosticBag();
        var detector = new BreakingChangeDetector(checkDiagnostics);
        var report = detector.Compare(oldModule, newModule);

        Assert.False(report.HasBreakingChanges);
        Assert.Empty(report.BreakingChanges);
    }

    [Fact]
    public void Compare_ParameterTypeChange_ReportsBreakingChange()
    {
        var oldSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §IN{name=x}{type=INT}
  §OUT{type=INT}
  §BODY
    §RETURN x
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var newSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §IN{name=x}{type=STRING}
  §OUT{type=INT}
  §BODY
    §RETURN INT:0
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var oldModule = Parse(oldSource, out var oldDiagnostics);
        var newModule = Parse(newSource, out var newDiagnostics);
        Assert.False(oldDiagnostics.HasErrors);
        Assert.False(newDiagnostics.HasErrors);

        var checkDiagnostics = new DiagnosticBag();
        var detector = new BreakingChangeDetector(checkDiagnostics);
        var report = detector.Compare(oldModule, newModule);

        Assert.True(report.HasBreakingChanges);
        Assert.Contains(report.BreakingChanges, c => c.Contains("type changed"));
    }

    [Fact]
    public void Compare_ParameterCountChange_ReportsBreakingChange()
    {
        var oldSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §IN{name=x}{type=INT}
  §OUT{type=INT}
  §BODY
    §RETURN x
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var newSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §IN{name=x}{type=INT}
  §IN{name=y}{type=INT}
  §OUT{type=INT}
  §BODY
    §RETURN (+ x y)
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var oldModule = Parse(oldSource, out var oldDiagnostics);
        var newModule = Parse(newSource, out var newDiagnostics);
        Assert.False(oldDiagnostics.HasErrors);
        Assert.False(newDiagnostics.HasErrors);

        var checkDiagnostics = new DiagnosticBag();
        var detector = new BreakingChangeDetector(checkDiagnostics);
        var report = detector.Compare(oldModule, newModule);

        Assert.True(report.HasBreakingChanges);
        Assert.Contains(report.BreakingChanges, c => c.Contains("Parameter count"));
    }

    [Fact]
    public void Compare_FunctionRemoved_ReportsBreakingChange()
    {
        var oldSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§FUNC{id=f002}{name=Helper}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f002}
§END_MODULE{id=m001}
";
        var newSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var oldModule = Parse(oldSource, out var oldDiagnostics);
        var newModule = Parse(newSource, out var newDiagnostics);
        Assert.False(oldDiagnostics.HasErrors);
        Assert.False(newDiagnostics.HasErrors);

        var checkDiagnostics = new DiagnosticBag();
        var detector = new BreakingChangeDetector(checkDiagnostics);
        var report = detector.Compare(oldModule, newModule);

        Assert.True(report.HasBreakingChanges);
        Assert.Contains(report.RemovedFunctions, f => f == "Helper");
    }

    [Fact]
    public void Compare_FunctionAdded_NotBreakingChange()
    {
        var oldSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var newSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f001}
§FUNC{id=f002}{name=NewFunction}{visibility=public}
  §OUT{type=VOID}
  §BODY
  §END_BODY
§END_FUNC{id=f002}
§END_MODULE{id=m001}
";
        var oldModule = Parse(oldSource, out var oldDiagnostics);
        var newModule = Parse(newSource, out var newDiagnostics);
        Assert.False(oldDiagnostics.HasErrors);
        Assert.False(newDiagnostics.HasErrors);

        var checkDiagnostics = new DiagnosticBag();
        var detector = new BreakingChangeDetector(checkDiagnostics);
        var report = detector.Compare(oldModule, newModule);

        Assert.False(report.HasBreakingChanges);
        Assert.Contains(report.AddedFunctions, f => f == "NewFunction");
    }

    [Fact]
    public void Compare_ReturnTypeChange_ReportsBreakingChange()
    {
        var oldSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=INT}
  §BODY
    §RETURN INT:0
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var newSource = @"
§MODULE{id=m001}{name=Test}
§FUNC{id=f001}{name=Test}{visibility=public}
  §OUT{type=STRING}
  §BODY
    §RETURN STR:""hello""
  §END_BODY
§END_FUNC{id=f001}
§END_MODULE{id=m001}
";
        var oldModule = Parse(oldSource, out var oldDiagnostics);
        var newModule = Parse(newSource, out var newDiagnostics);
        Assert.False(oldDiagnostics.HasErrors);
        Assert.False(newDiagnostics.HasErrors);

        var checkDiagnostics = new DiagnosticBag();
        var detector = new BreakingChangeDetector(checkDiagnostics);
        var report = detector.Compare(oldModule, newModule);

        Assert.True(report.HasBreakingChanges);
        Assert.Contains(report.BreakingChanges, c => c.Contains("Return type"));
    }

    #endregion
}
