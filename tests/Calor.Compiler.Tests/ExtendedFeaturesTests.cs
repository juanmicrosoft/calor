using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for the Calor Extended Language Features.
/// Covers all 4 phases: Quick Wins, Core Features, Enhanced Contracts, and Future Extensions.
/// </summary>
public class ExtendedFeaturesTests
{
    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static string Emit(ModuleNode module)
    {
        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    #region Phase 1: Quick Wins - Examples (§EX)

    [Fact]
    public void Lexer_RecognizesExampleKeyword()
    {
        var tokens = Tokenize("§EX", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Example, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesInlineExample()
    {
        var source = @"
§M{m001:Test}
§F{f001:Add:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §EX (+ 2 3) → 5
  §R (+ a b)
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Functions);
        var func = module.Functions[0];
        Assert.Single(func.Examples);
        Assert.IsType<ExampleNode>(func.Examples[0]);
    }

    [Fact]
    public void Parser_ParsesExampleWithId()
    {
        var source = @"
§M{m001:Test}
§F{f001:Add:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §EX{ex001} (+ 2 3) → 5
  §R (+ a b)
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var example = module.Functions[0].Examples[0];
        Assert.Equal("ex001", example.Id);
    }

    [Fact]
    public void Emitter_EmitsExampleAsDebugAssert()
    {
        var source = @"
§M{m001:Test}
§F{f001:Add:pub}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §EX (+ 2 3) → 5
  §R (+ a b)
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);
        Assert.Contains("Debug.Assert", code);
    }

    #endregion

    #region Phase 1: Quick Wins - Issues (§TODO, §FIXME, §HACK)

    [Fact]
    public void Lexer_RecognizesTodoKeyword()
    {
        var tokens = Tokenize("§TODO", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Todo, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesFixmeKeyword()
    {
        var tokens = Tokenize("§FIXME", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Fixme, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesHackKeyword()
    {
        var tokens = Tokenize("§HACK", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Hack, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesTodoIssue()
    {
        var source = @"
§M{m001:Test}
§TODO{t001:perf:high} ""Optimize for large n""
§F{f001:Calc:pub}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Issues);
        var issue = module.Issues[0];
        Assert.Equal(IssueKind.Todo, issue.Kind);
        Assert.Equal("t001", issue.Id);
        Assert.Equal("perf", issue.Category);
        Assert.Equal(IssuePriority.High, issue.Priority);
        Assert.Equal("Optimize for large n", issue.Description);
    }

    [Fact]
    public void Parser_ParsesFunctionLevelFixme()
    {
        var source = @"
§M{m001:Test}
§F{f001:Calc:pub}
  §FIXME{x001:bug:critical} ""Integer overflow""
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.Single(func.Issues);
        var issue = func.Issues[0];
        Assert.Equal(IssueKind.Fixme, issue.Kind);
        Assert.Equal(IssuePriority.Critical, issue.Priority);
    }

    [Fact]
    public void Emitter_EmitsIssueAsComment()
    {
        var source = @"
§M{m001:Test}
§TODO{t001:perf:high} ""Optimize for large n""
§F{f001:Calc:pub}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);
        Assert.Contains("// TODO[t001](perf) [High]: Optimize for large n", code);
    }

    #endregion

    #region Phase 2: Core Features - Dependencies (§USES, §USEDBY)

    [Fact]
    public void Lexer_RecognizesUsesKeyword()
    {
        var tokens = Tokenize("§USES", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Uses, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesUsedByKeyword()
    {
        var tokens = Tokenize("§USEDBY", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.UsedBy, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesUsesDeclaration()
    {
        var source = @"
§M{m001:Test}
§F{f001:ProcessOrder:pub}
  §USES{ValidateOrder}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.Uses);
        Assert.Single(func.Uses.Dependencies);
        Assert.Equal("ValidateOrder", func.Uses.Dependencies[0].Target);
    }

    [Fact]
    public void Parser_ParsesUsedByDeclaration()
    {
        var source = @"
§M{m001:Test}
§F{f001:ProcessOrder:pub}
  §USEDBY{OrderController}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.UsedBy);
        Assert.Single(func.UsedBy.Dependents);
    }

    [Fact]
    public void Emitter_EmitsDependenciesAsComments()
    {
        var source = @"
§M{m001:Test}
§F{f001:ProcessOrder:pub}
  §USES{ValidateOrder}
  §USEDBY{OrderController}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);
        Assert.Contains("// USES:", code);
        Assert.Contains("ValidateOrder", code);
        Assert.Contains("// USEDBY:", code);
    }

    #endregion

    #region Phase 2: Core Features - Assumptions (§ASSUME)

    [Fact]
    public void Lexer_RecognizesAssumeKeyword()
    {
        var tokens = Tokenize("§ASSUME", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Assume, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesAssumeDeclaration()
    {
        var source = @"
§M{m001:Test}
§ASSUME{env} ""Database connection pool initialized""
§F{f001:GetOrder:pub}
  §ASSUME{data} ""orderId exists in database""
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Assumptions);
        Assert.Equal(AssumptionCategory.Env, module.Assumptions[0].Category);

        var func = module.Functions[0];
        Assert.Single(func.Assumptions);
        Assert.Equal(AssumptionCategory.Data, func.Assumptions[0].Category);
    }

    #endregion

    #region Phase 3: Enhanced Contracts - Complexity (§COMPLEXITY)

    [Fact]
    public void Lexer_RecognizesComplexityKeyword()
    {
        var tokens = Tokenize("§COMPLEXITY", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Complexity, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesComplexityDeclaration()
    {
        // Use quoted values for complex syntax
        var source = @"
§M{m001:Test}
§F{f001:BinarySearch:pub}
  §COMPLEXITY{time=""O(logn)""}{space=""O(1)""}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.Complexity);
        Assert.Equal(ComplexityClass.OLogN, func.Complexity.TimeComplexity);
        Assert.Equal(ComplexityClass.O1, func.Complexity.SpaceComplexity);
    }

    [Fact]
    public void Emitter_EmitsComplexityAsComment()
    {
        var source = @"
§M{m001:Test}
§F{f001:BinarySearch:pub}
  §COMPLEXITY{time=""O(n)""}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);
        Assert.Contains("// COMPLEXITY:", code);
        Assert.Contains("O(n)", code);
    }

    #endregion

    #region Phase 3: Enhanced Contracts - Versioning (§SINCE, §DEPRECATED)

    [Fact]
    public void Lexer_RecognizesSinceKeyword()
    {
        var tokens = Tokenize("§SINCE", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Since, tokens[0].Kind);
    }

    [Fact]
    public void Lexer_RecognizesDeprecatedKeyword()
    {
        var tokens = Tokenize("§DEPRECATED", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Deprecated, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesSinceDeclaration()
    {
        // Use quoted values for version
        var source = @"
§M{m001:Test}
§F{f001:NewMethod:pub}
  §SINCE{version=""1.5.0""}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.Since);
        Assert.Equal("1.5.0", func.Since.Version);
    }

    [Fact]
    public void Parser_ParsesDeprecatedDeclaration()
    {
        // Use quoted values for version
        var source = @"
§M{m001:Test}
§F{f001:OldMethod:pub}
  §DEPRECATED{since=""1.5.0""}{use=NewMethod}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.Deprecated);
        Assert.Equal("1.5.0", func.Deprecated.SinceVersion);
        Assert.Equal("NewMethod", func.Deprecated.Replacement);
    }

    [Fact]
    public void Emitter_EmitsDeprecatedAsObsoleteAttribute()
    {
        var source = @"
§M{m001:Test}
§F{f001:OldMethod:pub}
  §DEPRECATED{since=""1.5.0""}{use=NewMethod}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);
        Assert.Contains("[System.Obsolete(", code);
        Assert.Contains("NewMethod", code);
    }

    #endregion

    #region Phase 4: Future Extensions - Decisions (§DECISION)

    [Fact]
    public void Lexer_RecognizesDecisionKeywords()
    {
        var tokensDecision = Tokenize("§DECISION", out var d1);
        var tokensChosen = Tokenize("§CHOSEN", out var d2);
        var tokensRejected = Tokenize("§REJECTED", out var d3);
        var tokensReason = Tokenize("§REASON", out var d4);

        Assert.Equal(TokenKind.Decision, tokensDecision[0].Kind);
        Assert.Equal(TokenKind.Chosen, tokensChosen[0].Kind);
        Assert.Equal(TokenKind.Rejected, tokensRejected[0].Kind);
        Assert.Equal(TokenKind.Reason, tokensReason[0].Kind);
    }

    [Fact]
    public void Parser_ParsesDecisionRecord()
    {
        var source = @"
§M{m001:Test}
§DECISION{d001} ""Algorithm selection""
  §CHOSEN ""QuickSort""
  §REASON ""Best average-case performance""
  §REJECTED ""MergeSort""
    §REASON ""Requires O(n) extra space""
  §CONTEXT ""Typical input: 1000-10000 items""
§/DECISION{d001}
§F{f001:Sort:pub}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Single(module.Decisions);
        var decision = module.Decisions[0];
        Assert.Equal("d001", decision.Id);
        Assert.Equal("Algorithm selection", decision.Title);
        Assert.Equal("QuickSort", decision.ChosenOption);
        Assert.Single(decision.RejectedOptions);
        Assert.Equal("MergeSort", decision.RejectedOptions[0].Name);
    }

    #endregion

    #region Phase 4: Future Extensions - Context (§CONTEXT)

    [Fact]
    public void Lexer_RecognizesContextKeywords()
    {
        var tokensContext = Tokenize("§CONTEXT", out var d1);
        var tokensVisible = Tokenize("§VISIBLE", out var d2);
        var tokensHidden = Tokenize("§HIDDEN", out var d3);
        var tokensFocus = Tokenize("§FOCUS", out var d4);

        Assert.Equal(TokenKind.Context, tokensContext[0].Kind);
        Assert.Equal(TokenKind.Visible, tokensVisible[0].Kind);
        Assert.Equal(TokenKind.HiddenSection, tokensHidden[0].Kind);
        Assert.Equal(TokenKind.Focus, tokensFocus[0].Kind);
    }

    [Fact]
    public void Parser_ParsesContextMarker()
    {
        var source = @"
§M{m001:Test}
§CONTEXT{partial}
  §VISIBLE
    §FILE{OrderService.calr}
  §/VISIBLE
  §FOCUS{OrderService.ProcessOrder}
§/CONTEXT
§F{f001:Process:pub}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.NotNull(module.Context);
        Assert.True(module.Context.IsPartial);
        Assert.Single(module.Context.VisibleFiles);
        Assert.Equal("OrderService.calr", module.Context.VisibleFiles[0].FilePath);
        Assert.Equal("OrderService.ProcessOrder", module.Context.FocusTarget);
    }

    #endregion

    #region Phase 4: Future Extensions - Properties (§PROPERTY)

    [Fact]
    public void Lexer_RecognizesPropertyKeyword()
    {
        var tokens = Tokenize("§PROPERTY", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.PropertyTest, tokens[0].Kind);
    }

    [Fact]
    public void Parser_ParsesPropertyDeclaration()
    {
        var source = @"
§M{m001:Test}
§F{f001:Reverse:pub}
  §PROPERTY (== 1 1)
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.Single(func.Properties);
    }

    #endregion

    #region Phase 4: Future Extensions - Collaboration (§LOCK, §AUTHOR, §TASK)

    [Fact]
    public void Lexer_RecognizesCollaborationKeywords()
    {
        var tokensLock = Tokenize("§LOCK", out var d1);
        var tokensAuthor = Tokenize("§AUTHOR", out var d2);
        var tokensTask = Tokenize("§TASK", out var d3);

        Assert.Equal(TokenKind.Lock, tokensLock[0].Kind);
        Assert.Equal(TokenKind.AgentAuthor, tokensAuthor[0].Kind);
        Assert.Equal(TokenKind.TaskRef, tokensTask[0].Kind);
    }

    [Fact]
    public void Parser_ParsesLockDeclaration()
    {
        // Use v1 format with = signs
        var source = @"
§M{m001:Test}
§F{f001:SharedFunc:pub}
  §LOCK{agent=agent123}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.Lock);
        Assert.Equal("agent123", func.Lock.AgentId);
    }

    [Fact]
    public void Parser_ParsesAuthorDeclaration()
    {
        // Use v1 format with = signs
        var source = @"
§M{m001:Test}
§F{f001:SharedFunc:pub}
  §AUTHOR{agent=agent456}{task=PROJ789}
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.Author);
        Assert.Equal("agent456", func.Author.AgentId);
        Assert.Equal("PROJ789", func.Author.TaskId);
    }

    [Fact]
    public void Parser_ParsesTaskRefDeclaration()
    {
        var source = @"
§M{m001:Test}
§F{f001:SharedFunc:pub}
  §TASK{PROJ789} ""Implement validation""
  §O{i32}
  §R 42
§/F{f001}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.NotNull(func.TaskRef);
        Assert.Equal("PROJ789", func.TaskRef.TaskId);
        Assert.Equal("Implement validation", func.TaskRef.Description);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Parser_ParsesComprehensiveExample()
    {
        var source = @"
§M{m001:Demo}
§TODO{t001:docs:medium} ""Add examples""

§F{f001:Add:pub}
  §SINCE{version=""1.0.0""}
  §COMPLEXITY{time=""O(1)""}{space=""O(1)""}
  §USES{ValidateInput}
  §ASSUME{data} ""Inputs are within i32 range""
  §I{i32:a} §I{i32:b}
  §O{i32}
  §EX (+ 2 3) → 5
  §EX (+ 0 0) → 0
  §R (+ a b)
§/F{f001}

§F{f002:OldAdd:pub}
  §SINCE{version=""0.5.0""}
  §DEPRECATED{since=""1.0.0""}{use=Add}
  §I{i32:x} §I{i32:y}
  §O{i32}
  §R (+ x y)
§/F{f002}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        Assert.Equal(2, module.Functions.Count);
        Assert.Single(module.Issues);

        var addFunc = module.Functions[0];
        Assert.Equal("Add", addFunc.Name);
        Assert.NotNull(addFunc.Since);
        Assert.NotNull(addFunc.Complexity);
        Assert.NotNull(addFunc.Uses);
        Assert.Single(addFunc.Assumptions);
        Assert.Equal(2, addFunc.Examples.Count);

        var oldAddFunc = module.Functions[1];
        Assert.NotNull(oldAddFunc.Deprecated);
        Assert.Equal("Add", oldAddFunc.Deprecated.Replacement);
    }

    [Fact]
    public void Emitter_GeneratesValidCSharpWithExtendedFeatures()
    {
        var source = @"
§M{m001:Demo}
§TODO{t001:docs:medium} ""Add examples""

§F{f001:Add:pub}
  §SINCE{version=""1.0.0""}
  §COMPLEXITY{time=""O(1)""}
  §I{i32:a} §I{i32:b}
  §O{i32}
  §EX (+ 2 3) → 5
  §R (+ a b)
§/F{f001}

§F{f002:OldAdd:pub}
  §DEPRECATED{since=""1.0.0""}{use=Add}
  §I{i32:x} §I{i32:y}
  §O{i32}
  §R (+ x y)
§/F{f002}
§/M{m001}
";

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var code = Emit(module);

        // Check various expected outputs
        Assert.Contains("namespace Demo", code);
        Assert.Contains("// TODO", code);
        Assert.Contains("// SINCE: 1.0.0", code);
        Assert.Contains("// COMPLEXITY:", code);
        Assert.Contains("Debug.Assert", code);
        Assert.Contains("[System.Obsolete(", code);
        Assert.Contains("public static int Add(", code);
        Assert.Contains("public static int OldAdd(", code);
    }

    #endregion
}
