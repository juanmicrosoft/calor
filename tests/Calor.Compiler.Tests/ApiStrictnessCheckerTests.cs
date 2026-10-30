using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ApiStrictnessCheckerTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void DocumentationComments_SatisfyRequireDocsAndStrictApi_AndSurviveFormatting(string newline)
    {
        var source = string.Join(newline,
            "/// Module documentation.",
            "§M{m1:Docs}",
            "  /// Function documentation.",
            "  /// A second line.",
            "  §F{f1:Run:pub} () -> void",
            "  /// Class documentation.",
            "  §CL{c1:Widget:pub}",
            "    /// Method documentation.",
            "    §MT{mt1:Work:pub} () -> void",
            "    /// Property documentation.",
            "    §PROP{p1:Value:i32:pub:get}",
            "  /// Interface documentation.",
            "  §IFACE{i1:IWidget}");
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal("Module documentation.", module.DocComment);
        Assert.Equal("Function documentation.\nA second line.", module.Functions.Single().DocComment);
        var checker = new ApiStrictnessChecker(diagnostics,
            new ApiStrictnessOptions { RequireDocs = true, StrictApi = true });
        checker.Check(module);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.MissingDocComment);

        var formatted = new Calor.Compiler.Migration.CalorEmitter().Emit(module);
        var reparsed = Parse(formatted, out var reparsedDiagnostics);
        Assert.False(reparsedDiagnostics.HasErrors, string.Join("\n", reparsedDiagnostics));
        Assert.Equal(module.DocComment, reparsed.DocComment);
        Assert.Equal(module.Functions.Single().DocComment, reparsed.Functions.Single().DocComment);
        Assert.Equal(module.Classes.Single().Methods.Single().DocComment,
            reparsed.Classes.Single().Methods.Single().DocComment);
    }

    [Theory]
    [InlineData("// ordinary comment\n")]
    [InlineData("///\n")]
    [InlineData("/// detached documentation\n\n")]
    [InlineData("/// detached documentation\n// ordinary comment\n")]
    public void DocumentationComments_DoNotAcceptEmptyOrdinaryOrDetachedComments(string comment)
    {
        var module = Parse(comment + "§M{m1:Docs}", out var diagnostics);
        new ApiStrictnessChecker(diagnostics, new ApiStrictnessOptions { RequireDocs = true }).Check(module);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.MissingDocComment);
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    #region Default Mode

    [Fact]
    public void Check_DefaultMode_NoWarningsOnPublicFunction()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Test:pub}
      §O{void}
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
§M{m001:Test}
  §F{f001:Test:priv}
      §O{void}
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
§M{m001:Test}
  §F{f001:Test:pub}
      §O{void}
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
§M{m001:Test}
  §F{f001:PrivateHelper:priv}
      §O{void}
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
§M{m001:Test}
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
§M{m001:Test}
  §F{f001:Test:pub}
      §O{void}
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
§M{m001:Test}
  §F{f001:Divide:pub}
      §I{i32:a}
      §I{i32:b}
      §O{i32}
      §Q (!= b 0)
      §R (/ a b)
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
§M{m001:Test}
  §F{f001:Test:pub}
      §O{void}
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
§M{m001:Test}
  §F{f001:Test:pub}
      §O{void}
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
§M{m001:Test}
  §F{f001:Test:pub}
      §I{i32:x}
      §O{i32}
      §R x
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
§M{m001:Test}
  §F{f001:Test:pub}
      §I{i32:x}
      §O{i32}
      §R x
";
        var newSource = @"
§M{m001:Test}
  §F{f001:Test:pub}
      §I{str:x}
      §O{i32}
      §R 0
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
§M{m001:Test}
  §F{f001:Test:pub}
      §I{i32:x}
      §O{i32}
      §R x
";
        var newSource = @"
§M{m001:Test}
  §F{f001:Test:pub}
      §I{i32:x}
      §I{i32:y}
      §O{i32}
      §R (+ x y)
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
§M{m001:Test}
  §F{f001:Test:pub}
      §O{void}
  §F{f002:Helper:pub}
      §O{void}
";
        var newSource = @"
§M{m001:Test}
  §F{f001:Test:pub}
      §O{void}
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
§M{m001:Test}
  §F{f001:Test:pub}
      §O{void}
";
        var newSource = @"
§M{m001:Test}
  §F{f001:Test:pub}
      §O{void}
  §F{f002:NewFunction:pub}
      §O{void}
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
§M{m001:Test}
  §F{f001:Test:pub}
      §O{i32}
      §R 0
";
        var newSource = @"
§M{m001:Test}
  §F{f001:Test:pub}
      §O{str}
      §R ""hello""
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
