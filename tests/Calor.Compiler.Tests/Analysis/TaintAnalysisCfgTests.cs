using Calor.Compiler.Analysis.Security;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using System.Text.Json;
using Xunit;

namespace Calor.Compiler.Tests.Analysis;

public sealed class TaintAnalysisCfgTests
{
    private static TextSpan Span(int start) => new(start, start + 1, 1, start + 1);

    [Fact]
    public void RegressionCorpusInventory_EnforcesCommittedPrecisionRecallTargets()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "Security",
            "TaintAnalysis",
            "manifest.json");
        using var inventory = JsonDocument.Parse(File.ReadAllText(path));
        var root = inventory.RootElement;

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        var expected = cases
            .Select(testCase => testCase.GetProperty("expectedFinding").GetBoolean())
            .ToArray();
        var actual = cases
            .Select(testCase => ExecuteCorpusCase(testCase.GetProperty("id").GetString()!))
            .ToArray();
        var truePositives = expected.Zip(actual).Count(pair => pair.First && pair.Second);
        var falsePositives = expected.Zip(actual).Count(pair => !pair.First && pair.Second);
        var falseNegatives = expected.Zip(actual).Count(pair => pair.First && !pair.Second);
        var precision = truePositives / (double)Math.Max(1, truePositives + falsePositives);
        var recall = truePositives / (double)Math.Max(1, truePositives + falseNegatives);

        Assert.True(precision >= root.GetProperty("minimumPrecision").GetDouble());
        Assert.True(recall >= root.GetProperty("minimumRecall").GetDouble());
    }

    [Fact]
    public void DirectSourceToSink_ReportsEvenWhenLegacyHopThresholdIsTwo()
    {
        var input = Variable("user_input", "STRING", "param:input", true);
        var function = Function(
            "Direct",
            "fn:direct",
            [input],
            [Call("db.execute", Reference(input, 2), 1)]);

        var finding = Assert.Single(new TaintAnalysis(
            function,
            new TaintAnalysisOptions { MinTaintHops = 2 }).Vulnerabilities);

        Assert.Equal(TaintSink.SqlQuery, finding.Sink);
        Assert.Equal(input.Name, finding.SourceVariable);
        Assert.True(finding.ProvenancePath.Count >= 2);
    }

    [Fact]
    public void ConsoleReadLine_CallReturnSource_ReachesSink()
    {
        var value = Variable("value", "STRING", "local:value");
        var function = Function(
            "ConsoleSource",
            "fn:console-source",
            [],
            [
                new BoundBindStatement(
                    Span(10),
                    value,
                    new BoundCallExpression(Span(11), "Console.ReadLine", [], "STRING")),
                Call("db.execute", Reference(value, 12), 13),
            ]);

        var finding = Assert.Single(new TaintAnalysis(function).Vulnerabilities);
        Assert.Equal(TaintSource.UserInput, finding.Source);
        Assert.Equal("Console.ReadLine", finding.SourceVariable);
    }

    [Fact]
    public void SafeOverwrite_StronglyKillsEarlierTaint()
    {
        var input = Variable("user_input", "STRING", "param:overwrite", true);
        var value = Variable("value", "STRING", "local:value");
        var function = Function(
            "Overwrite",
            "fn:overwrite",
            [input],
            [
                new BoundBindStatement(Span(20), value, Reference(input, 21)),
                new BoundAssignmentStatement(
                    Span(22),
                    Reference(value, 23),
                    new BoundStringLiteral(Span(24), "safe")),
                Call("db.execute", Reference(value, 25), 26),
            ]);

        Assert.Empty(new TaintAnalysis(function).Vulnerabilities);
    }

    [Fact]
    public void ConstantsOnly_DoNotProduceAFinding()
    {
        var value = Variable("value", "STRING", "local:constant-value");
        var function = Function(
            "Constants",
            "fn:constants",
            [],
            [
                new BoundBindStatement(
                    Span(27),
                    value,
                    new BoundStringLiteral(Span(28), "safe")),
                Call("db.execute", Reference(value, 29), 30),
            ]);

        Assert.Empty(new TaintAnalysis(function).Vulnerabilities);
    }

    [Fact]
    public void BranchJoin_PreservesTaintFromOneReachableBranch()
    {
        var input = Variable("user_input", "STRING", "param:branch-input", true);
        var value = Variable("value", "STRING", "local:branch-value");
        var condition = Variable("condition", "BOOL", "param:condition", true);
        var function = Function(
            "Branch",
            "fn:branch",
            [input, condition],
            [
                new BoundBindStatement(
                    Span(30),
                    value,
                    new BoundStringLiteral(Span(31), "safe")),
                new BoundIfStatement(
                    Span(32),
                    Reference(condition, 33),
                    [
                        new BoundAssignmentStatement(
                            Span(34),
                            Reference(value, 35),
                            Reference(input, 36)),
                    ],
                    [],
                    null),
                Call("db.execute", Reference(value, 37), 38),
            ]);

        Assert.Single(new TaintAnalysis(function).Vulnerabilities);
    }

    [Fact]
    public void LoopFixedPoint_PropagatesTaintThroughBackEdge()
    {
        var input = Variable("user_input", "STRING", "param:loop-input", true);
        var value = Variable("value", "STRING", "local:loop-value");
        var condition = Variable("condition", "BOOL", "param:loop-condition", true);
        var function = Function(
            "Loop",
            "fn:loop",
            [input, condition],
            [
                new BoundBindStatement(
                    Span(40),
                    value,
                    new BoundStringLiteral(Span(41), "safe")),
                new BoundWhileStatement(
                    Span(42),
                    Reference(condition, 43),
                    [
                        new BoundAssignmentStatement(
                            Span(44),
                            Reference(value, 45),
                            Reference(input, 46)),
                    ]),
                Call("db.execute", Reference(value, 47), 48),
            ]);

        var analysis = new TaintAnalysis(function);
        Assert.True(analysis.DataflowResult.IsConverged);
        Assert.Single(analysis.Vulnerabilities);
    }

    [Fact]
    public void AliasFieldAndCollectionAccessPaths_PreserveTaint()
    {
        var input = Variable("user_input", "STRING", "param:path-input", true);
        var first = Variable("first", "OBJECT", "local:first");
        var alias = Variable("alias", "OBJECT", "local:alias");
        var items = Variable("items", "STRING[]", "local:items");
        var fieldOnAlias = Field(alias, "Text", 52);
        var fieldOnFirst = Field(first, "Text", 53);
        var item = new BoundArrayAccess(Span(54), Reference(items, 55), new BoundIntLiteral(Span(56), 0));
        var itemRead = new BoundArrayAccess(Span(57), Reference(items, 58), new BoundIntLiteral(Span(59), 0));
        var function = Function(
            "AccessPaths",
            "fn:access-paths",
            [input],
            [
                new BoundBindStatement(Span(50), first, null),
                new BoundBindStatement(Span(51), alias, Reference(first, 52)),
                new BoundAssignmentStatement(Span(53), fieldOnAlias, Reference(input, 54)),
                Call("db.execute", fieldOnFirst, 55),
                new BoundBindStatement(Span(56), items, null),
                new BoundAssignmentStatement(Span(57), item, Reference(input, 58)),
                Call("db.execute", itemRead, 59),
            ]);

        Assert.Equal(2, new TaintAnalysis(function).Vulnerabilities.Count);
    }

    [Fact]
    public void ExactSanitizerIdentity_DoesNotTreatDesanitizeAsSanitizer()
    {
        var input = Variable("user_input", "STRING", "param:sanitize-input", true);
        var dangerous = Variable("dangerous", "STRING", "local:dangerous");
        var safe = Variable("safe", "STRING", "local:safe");
        var function = Function(
            "Sanitizers",
            "fn:sanitizers",
            [input],
            [
                new BoundBindStatement(
                    Span(60),
                    dangerous,
                    new BoundCallExpression(
                        Span(61),
                        "desanitize",
                        [Reference(input, 62)],
                        "STRING")),
                Call("db.execute", Reference(dangerous, 63), 64),
                new BoundBindStatement(
                    Span(65),
                    safe,
                    new BoundCallExpression(
                        Span(66),
                        "sql_escape",
                        [Reference(input, 67)],
                        "STRING")),
                Call("db.execute", Reference(safe, 68), 69),
            ]);

        var finding = Assert.Single(new TaintAnalysis(function).Vulnerabilities);
        Assert.Equal("dangerous", finding.SinkVariable);
    }

    [Fact]
    public void RecursiveSanitizerSummary_ConvergesOverFiniteLattice()
    {
        var value = Variable("value", "STRING", "param:recursive-value", true);
        var condition = Variable("condition", "BOOL", "param:recursive-condition", true);
        var selfSymbol = new FunctionSymbol(
            new SymbolId("fn:recursive-sanitizer"),
            "Self",
            "STRING",
            [value, condition]);
        var recursiveCall = new BoundCallExpression(
            Span(70),
            "Self",
            [Reference(value, 71), Reference(condition, 72)],
            "STRING",
            resolvedSymbol: selfSymbol);
        var self = new BoundFunction(
            Span(73),
            selfSymbol,
            [
                new BoundIfStatement(
                    Span(74),
                    Reference(condition, 75),
                    [new BoundReturnStatement(Span(76), Reference(value, 77))],
                    [],
                    [
                        new BoundReturnStatement(
                            Span(78),
                            new BoundCallExpression(
                                Span(79),
                                "sql_escape",
                                [recursiveCall],
                                "STRING")),
                    ]),
            ],
            new Scope());

        new TaintAnalysisRunner(new DiagnosticBag()).Analyze(Module(self));

        var input = Variable("user_input", "STRING", "param:recursive-caller", true);
        var forwarded = Variable("forwarded", "STRING", "local:recursive-forwarded");
        var caller = Function(
            "RecursiveCaller",
            "fn:recursive-caller",
            [input],
            [
                new BoundBindStatement(
                    Span(80),
                    forwarded,
                    new BoundCallExpression(
                        Span(81),
                        "Self",
                        [Reference(input, 82), new BoundBoolLiteral(Span(83), true)],
                        "STRING",
                        resolvedSymbol: selfSymbol)),
                Call("db.execute", Reference(forwarded, 84), 85),
            ]);

        var diagnostics = new DiagnosticBag();
        new TaintAnalysisRunner(diagnostics).Analyze(Module(self, caller));

        Assert.Single(diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.SqlInjection));
    }

    [Fact]
    public void InterproceduralReturnAndParameterSinkSummaries_PropagateExactResolvedCalls()
    {
        var identityInput = Variable("value", "STRING", "param:identity-value", true);
        var identitySymbol = FunctionSymbol("Identity", "fn:identity", [identityInput]);
        var identity = new BoundFunction(
            Span(70),
            identitySymbol,
            [new BoundReturnStatement(Span(71), Reference(identityInput, 72))],
            new Scope());

        var executeInput = Variable("value", "STRING", "param:execute-value", true);
        var executeSymbol = FunctionSymbol("Execute", "fn:execute", [executeInput]);
        var execute = new BoundFunction(
            Span(73),
            executeSymbol,
            [Call("db.execute", Reference(executeInput, 74), 75)],
            new Scope());

        var callerInput = Variable("user_input", "STRING", "param:caller-input", true);
        var forwarded = Variable("forwarded", "STRING", "local:forwarded");
        var caller = Function(
            "Caller",
            "fn:caller",
            [callerInput],
            [
                new BoundBindStatement(
                    Span(76),
                    forwarded,
                    new BoundCallExpression(
                        Span(77),
                        "Identity",
                        [Reference(callerInput, 78)],
                        "STRING",
                        resolvedSymbol: identitySymbol)),
                Call("db.execute", Reference(forwarded, 79), 80),
                new BoundCallStatement(
                    Span(81),
                    "Execute",
                    [Reference(callerInput, 82)],
                    resolvedSymbol: executeSymbol),
            ]);

        var diagnostics = new DiagnosticBag();
        new TaintAnalysisRunner(diagnostics).Analyze(Module(identity, execute, caller));

        Assert.Equal(
            2,
            diagnostics.Count(diagnostic => diagnostic.Code == DiagnosticCode.SqlInjection));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("call Identity", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("call Execute", StringComparison.Ordinal));
    }

    [Fact]
    public void VerificationAnalysisPass_UsesModuleTaintSummaries()
    {
        var identityInput = Variable("value", "STRING", "param:pass-identity", true);
        var identitySymbol = FunctionSymbol("Identity", "fn:pass-identity", [identityInput]);
        var identity = new BoundFunction(
            Span(83),
            identitySymbol,
            [new BoundReturnStatement(Span(84), Reference(identityInput, 85))],
            new Scope());

        var executeInput = Variable("value", "STRING", "param:pass-execute", true);
        var executeSymbol = FunctionSymbol("Execute", "fn:pass-execute", [executeInput]);
        var execute = new BoundFunction(
            Span(86),
            executeSymbol,
            [Call("db.execute", Reference(executeInput, 87), 88)],
            new Scope());

        var callerInput = Variable("user_input", "STRING", "param:pass-caller", true);
        var value = Variable("value", "STRING", "local:pass-value");
        var caller = Function(
            "Caller",
            "fn:pass-caller",
            [callerInput],
            [
                new BoundBindStatement(
                    Span(89),
                    value,
                    new BoundCallExpression(
                        Span(90),
                        "Identity",
                        [Reference(callerInput, 91)],
                        "STRING",
                        resolvedSymbol: identitySymbol)),
                new BoundCallStatement(
                    Span(92),
                    "Execute",
                    [Reference(value, 93)],
                    resolvedSymbol: executeSymbol),
            ]);

        var diagnostics = new DiagnosticBag();
        var result = new VerificationAnalysisPass(
            diagnostics,
            new VerificationAnalysisOptions
            {
                EnableDataflow = false,
                EnableBugPatterns = false,
                EnableTaintAnalysis = true,
                EnableKInduction = false,
            }).AnalyzeBound(Module(identity, execute, caller));

        Assert.Equal(1, result.TaintVulnerabilities);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.SqlInjection);
    }

    [Fact]
    public void MayAliasJoin_WeakFieldWritesPreserveAllPossibleTargets()
    {
        var input = Variable("user_input", "STRING", "param:alias-field-input", true);
        var condition = Variable("condition", "BOOL", "param:alias-field-condition", true);
        var first = Variable("first", "OBJECT", "local:alias-field-first");
        var second = Variable("second", "OBJECT", "local:alias-field-second");
        var alias = Variable("alias", "OBJECT", "local:alias-field-alias");
        var aliasField = Field(alias, "Value", 101);
        var function = Function(
            "MayAliasFields",
            "fn:may-alias-fields",
            [input, condition],
            [
                new BoundBindStatement(Span(94), first, null),
                new BoundBindStatement(Span(95), second, null),
                new BoundBindStatement(Span(96), alias, null),
                new BoundIfStatement(
                    Span(97),
                    Reference(condition, 98),
                    [new BoundAssignmentStatement(Span(99), Reference(alias, 100), Reference(first, 101))],
                    [],
                    [new BoundAssignmentStatement(Span(102), Reference(alias, 103), Reference(second, 104))]),
                new BoundAssignmentStatement(Span(105), aliasField, Reference(input, 106)),
                new BoundAssignmentStatement(
                    Span(107),
                    aliasField,
                    new BoundStringLiteral(Span(108), "safe")),
                Call("db.execute", Field(first, "Value", 109), 110),
                Call("db.execute", Field(second, "Value", 111), 112),
            ]);

        Assert.Equal(2, new TaintAnalysis(function).Vulnerabilities.Count);
    }

    [Fact]
    public void MayAliasJoin_WeakArrayElementWritesPreserveAllPossibleTargets()
    {
        var input = Variable("user_input", "STRING", "param:alias-array-input", true);
        var condition = Variable("condition", "BOOL", "param:alias-array-condition", true);
        var first = Variable("first", "STRING[]", "local:alias-array-first");
        var second = Variable("second", "STRING[]", "local:alias-array-second");
        var alias = Variable("alias", "STRING[]", "local:alias-array-alias");
        var aliasItem = new BoundArrayAccess(
            Span(120),
            Reference(alias, 121),
            new BoundIntLiteral(Span(122), 0));
        var function = Function(
            "MayAliasArrays",
            "fn:may-alias-arrays",
            [input, condition],
            [
                new BoundBindStatement(Span(113), first, null),
                new BoundBindStatement(Span(114), second, null),
                new BoundBindStatement(Span(115), alias, null),
                new BoundIfStatement(
                    Span(116),
                    Reference(condition, 117),
                    [new BoundAssignmentStatement(Span(118), Reference(alias, 119), Reference(first, 120))],
                    [],
                    [new BoundAssignmentStatement(Span(121), Reference(alias, 122), Reference(second, 123))]),
                new BoundAssignmentStatement(Span(124), aliasItem, Reference(input, 125)),
                new BoundAssignmentStatement(
                    Span(126),
                    aliasItem,
                    new BoundStringLiteral(Span(127), "safe")),
                Call(
                    "db.execute",
                    new BoundArrayAccess(Span(128), Reference(first, 129), new BoundIntLiteral(Span(130), 0)),
                    131),
                Call(
                    "db.execute",
                    new BoundArrayAccess(Span(132), Reference(second, 133), new BoundIntLiteral(Span(134), 0)),
                    135),
            ]);

        Assert.Equal(2, new TaintAnalysis(function).Vulnerabilities.Count);
    }

    [Fact]
    public void ResolvedLocalNamedSqlEscape_IsNotTreatedAsBuiltInSanitizer()
    {
        var localInput = Variable("value", "STRING", "param:local-escape", true);
        var localSymbol = FunctionSymbol("sql_escape", "fn:local-escape", [localInput]);
        var localEscape = new BoundFunction(
            Span(136),
            localSymbol,
            [new BoundReturnStatement(Span(137), Reference(localInput, 138))],
            new Scope());
        var input = Variable("user_input", "STRING", "param:local-escape-caller", true);
        var value = Variable("value", "STRING", "local:local-escape-value");
        var caller = Function(
            "Caller",
            "fn:local-escape-caller",
            [input],
            [
                new BoundBindStatement(
                    Span(139),
                    value,
                    new BoundCallExpression(
                        Span(140),
                        "sql_escape",
                        [Reference(input, 141)],
                        "STRING",
                        resolvedSymbol: localSymbol)),
                Call("db.execute", Reference(value, 142), 143),
            ]);

        var diagnostics = new DiagnosticBag();
        new TaintAnalysisRunner(diagnostics).Analyze(Module(localEscape, caller));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.SqlInjection);
    }

    [Fact]
    public void ResolvedCustomTypeCannotMatchTargetOnlySanitizer()
    {
        var input = Variable("user_input", "STRING", "param:contoso-input", true);
        var value = Variable("value", "STRING", "local:contoso-value");
        var function = Function(
            "ContosoSanitizer",
            "fn:contoso-sanitizer",
            [input],
            [
                new BoundBindStatement(
                    Span(143),
                    value,
                    new BoundCallExpression(
                        Span(144),
                        "Contoso.UnsafeSql.parameterize",
                        [Reference(input, 145)],
                        "STRING",
                        resolvedTypeName: "Contoso.UnsafeSql",
                        resolvedMethodName: "parameterize",
                        resolvedParameterTypes: ["STRING"])),
                Call("db.execute", Reference(value, 146), 147),
            ]);

        var finding = Assert.Single(new TaintAnalysis(
            function,
            new TaintAnalysisOptions
            {
                AdditionalSanitizers =
                [
                    new TaintSanitizerRule(
                        new TaintCallIdentity(Target: "Contoso.UnsafeSql.parameterize"),
                        TaintSink.SqlQuery),
                ],
            }).Vulnerabilities);

        Assert.Equal(TaintSink.SqlQuery, finding.Sink);
    }

    [Fact]
    public void InterproceduralAlternatives_UnionReturnAndParameterSinkSummaries()
    {
        var readSymbol = FunctionSymbol("Read", "fn:alternative-read", []);
        var read = new BoundFunction(
            Span(148),
            readSymbol,
            [
                new BoundReturnStatement(
                    Span(149),
                    new BoundCallExpression(Span(150), "Console.ReadLine", [], "STRING")),
            ],
            new Scope());
        var safeSymbol = FunctionSymbol("Safe", "fn:alternative-safe", []);
        var safe = new BoundFunction(
            Span(151),
            safeSymbol,
            [new BoundReturnStatement(Span(152), new BoundStringLiteral(Span(153), "safe"))],
            new Scope());
        var executeInput = Variable("value", "STRING", "param:alternative-execute", true);
        var executeSymbol = FunctionSymbol("Execute", "fn:alternative-execute", [executeInput]);
        var execute = new BoundFunction(
            Span(154),
            executeSymbol,
            [Call("db.execute", Reference(executeInput, 155), 156)],
            new Scope());
        var noOpInput = Variable("value", "STRING", "param:alternative-noop", true);
        var noOpSymbol = FunctionSymbol("NoOp", "fn:alternative-noop", [noOpInput]);
        var noOp = new BoundFunction(Span(157), noOpSymbol, [], new Scope());
        var value = Variable("value", "STRING", "local:alternative-value");
        var caller = Function(
            "Caller",
            "fn:alternative-caller",
            [],
            [
                new BoundBindStatement(
                    Span(158),
                    value,
                    new BoundCallExpression(
                        Span(159),
                        "ReadOrSafe",
                        [],
                        "STRING",
                        resolvedSymbols: [readSymbol, safeSymbol])),
                Call("db.execute", Reference(value, 160), 161),
                new BoundCallStatement(
                    Span(162),
                    "ExecuteOrNoOp",
                    [Reference(value, 163)],
                    resolvedSymbols: [executeSymbol, noOpSymbol]),
            ]);

        var diagnostics = new DiagnosticBag();
        new TaintAnalysisRunner(diagnostics).Analyze(Module(read, safe, execute, noOp, caller));

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Code == DiagnosticCode.SqlInjection));
    }

    [Fact]
    public void AliasRebinding_PreservesOldFieldObjectForAlias()
    {
        var input = Variable("user_input", "STRING", "param:rebind-field-input", true);
        var first = Variable("first", "OBJECT", "local:rebind-field-first");
        var second = Variable("second", "OBJECT", "local:rebind-field-second");
        var alias = Variable("alias", "OBJECT", "local:rebind-field-alias");
        var function = Function(
            "RebindField",
            "fn:rebind-field",
            [input],
            [
                new BoundBindStatement(Span(164), first, null),
                new BoundBindStatement(Span(165), second, null),
                new BoundBindStatement(Span(166), alias, null),
                new BoundAssignmentStatement(Span(167), Field(first, "Value", 168), Reference(input, 169)),
                new BoundAssignmentStatement(Span(170), Reference(alias, 171), Reference(first, 172)),
                new BoundAssignmentStatement(Span(173), Reference(first, 174), Reference(second, 175)),
                Call("db.execute", Field(alias, "Value", 176), 177),
                Call("db.execute", Field(first, "Value", 178), 179),
            ]);

        Assert.Single(new TaintAnalysis(function).Vulnerabilities);
    }

    [Fact]
    public void AliasRebinding_PreservesOldArrayObjectForAlias()
    {
        var input = Variable("user_input", "STRING", "param:rebind-array-input", true);
        var first = Variable("first", "STRING[]", "local:rebind-array-first");
        var second = Variable("second", "STRING[]", "local:rebind-array-second");
        var alias = Variable("alias", "STRING[]", "local:rebind-array-alias");
        var function = Function(
            "RebindArray",
            "fn:rebind-array",
            [input],
            [
                new BoundBindStatement(Span(180), first, null),
                new BoundBindStatement(Span(181), second, null),
                new BoundBindStatement(Span(182), alias, null),
                new BoundAssignmentStatement(
                    Span(183),
                    new BoundArrayAccess(Span(184), Reference(first, 185), new BoundIntLiteral(Span(186), 0)),
                    Reference(input, 187)),
                new BoundAssignmentStatement(Span(188), Reference(alias, 189), Reference(first, 190)),
                new BoundAssignmentStatement(Span(191), Reference(first, 192), Reference(second, 193)),
                Call(
                    "db.execute",
                    new BoundArrayAccess(Span(194), Reference(alias, 195), new BoundIntLiteral(Span(196), 0)),
                    197),
                Call(
                    "db.execute",
                    new BoundArrayAccess(Span(198), Reference(first, 199), new BoundIntLiteral(Span(200), 0)),
                    201),
            ]);

        Assert.Single(new TaintAnalysis(function).Vulnerabilities);
    }

    [Fact]
    public void FieldReferenceAlias_PreservesStoredObjectAfterSourceRebinding()
    {
        var input = Variable("user_input", "STRING", "param:field-reference-input", true);
        var holder = Variable("holder", "OBJECT", "local:field-reference-holder");
        var child = Variable("child", "OBJECT", "local:field-reference-child");
        var replacement = Variable("replacement", "OBJECT", "local:field-reference-replacement");
        var alias = Variable("alias", "OBJECT", "local:field-reference-alias");
        var childFieldSymbol = Variable("Child", "OBJECT", "field:reference-child");
        var valueFieldSymbol = Variable("Value", "STRING", "field:reference-value");
        var childField = Field(holder, "Child", 202, "OBJECT", childFieldSymbol);
        var function = Function(
            "FieldReferenceAlias",
            "fn:field-reference-alias",
            [input],
            [
                new BoundBindStatement(Span(203), holder, null),
                new BoundBindStatement(Span(204), child, null),
                new BoundBindStatement(Span(205), replacement, null),
                new BoundAssignmentStatement(Span(206), childField, Reference(child, 207)),
                new BoundBindStatement(
                    Span(208),
                    alias,
                    Field(holder, "Child", 209, "OBJECT", childFieldSymbol)),
                new BoundAssignmentStatement(Span(210), Reference(child, 211), Reference(replacement, 212)),
                new BoundAssignmentStatement(
                    Span(213),
                    Field(alias, "Value", 214, "STRING", valueFieldSymbol),
                    Reference(input, 215)),
                Call(
                    "db.execute",
                    Field(
                        Field(holder, "Child", 216, "OBJECT", childFieldSymbol),
                        "Value",
                        217,
                        "STRING",
                        valueFieldSymbol),
                    218),
            ]);

        Assert.Single(new TaintAnalysis(function).Vulnerabilities);
    }

    [Fact]
    public void ArrayReferenceAlias_PreservesStoredObjectAfterSourceRebinding()
    {
        var input = Variable("user_input", "STRING", "param:array-reference-input", true);
        var items = Variable("items", "OBJECT[]", "local:array-reference-items");
        var child = Variable("child", "OBJECT", "local:array-reference-child");
        var replacement = Variable("replacement", "OBJECT", "local:array-reference-replacement");
        var alias = Variable("alias", "OBJECT", "local:array-reference-alias");
        var valueFieldSymbol = Variable("Value", "STRING", "field:array-reference-value");
        BoundExpression Element(int span) =>
            new BoundArrayAccess(
                Span(span),
                Reference(items, span + 1),
                new BoundIntLiteral(Span(span + 2), 0));
        var function = Function(
            "ArrayReferenceAlias",
            "fn:array-reference-alias",
            [input],
            [
                new BoundBindStatement(Span(219), items, null),
                new BoundBindStatement(Span(220), child, null),
                new BoundBindStatement(Span(221), replacement, null),
                new BoundAssignmentStatement(Span(222), Element(223), Reference(child, 226)),
                new BoundBindStatement(Span(227), alias, Element(228)),
                new BoundAssignmentStatement(Span(231), Reference(child, 232), Reference(replacement, 233)),
                new BoundAssignmentStatement(
                    Span(234),
                    Field(alias, "Value", 235, "STRING", valueFieldSymbol),
                    Reference(input, 236)),
                Call(
                    "db.execute",
                    Field(Element(237), "Value", 240, "STRING", valueFieldSymbol),
                    241),
            ]);

        Assert.Single(new TaintAnalysis(function).Vulnerabilities);
    }

    [Theory]
    [InlineData("WriteAllText", new[] { "System.String", "System.String" })]
    [InlineData("Delete", new[] { "System.String" })]
    [InlineData("Open", new[] { "System.String", "System.IO.FileMode" })]
    [InlineData("Move", new[] { "System.String", "System.String" })]
    public void SystemIoFilePathSinks_UseExactResolvedSignatures(
        string method,
        string[] parameterTypes)
    {
        var path = Variable("user_path", "STRING", $"param:file-{method}", true);
        var args = parameterTypes.Length == 1
            ? new BoundExpression[] { Reference(path, 202) }
            : [Reference(path, 202), new BoundStringLiteral(Span(203), "safe")];
        var call = new BoundCallStatement(
            Span(204),
            $"File.{method}",
            args,
            resolvedTypeName: "System.IO.File",
            resolvedMethodName: method,
            resolvedParameterTypes: parameterTypes);
        var function = Function($"File{method}", $"fn:file-{method}", [path], [call]);

        var finding = Assert.Single(new TaintAnalysis(function).Vulnerabilities);
        Assert.Equal(TaintSink.FilePath, finding.Sink);
    }

    [Fact]
    public void Move_DestinationArgumentIsAFilePathSink()
    {
        var destination = Variable("user_path", "STRING", "param:file-move-destination", true);
        var move = new BoundCallStatement(
            Span(242),
            "File.Move",
            [new BoundStringLiteral(Span(243), "safe.txt"), Reference(destination, 244)],
            resolvedTypeName: "System.IO.File",
            resolvedMethodName: "Move",
            resolvedParameterTypes: ["System.String", "System.String"]);
        var function = Function("FileMove", "fn:file-move", [destination], [move]);

        var finding = Assert.Single(new TaintAnalysis(function).Vulnerabilities);
        Assert.Equal(TaintSink.FilePath, finding.Sink);
    }

    [Fact]
    public void ReadAllText_PathArgumentIsAFilePathSink()
    {
        var path = Variable("user_path", "STRING", "param:file-read-path", true);
        var read = new BoundCallStatement(
            Span(242),
            "File.ReadAllText",
            [Reference(path, 243)],
            resolvedTypeName: "System.IO.File",
            resolvedMethodName: "ReadAllText",
            resolvedParameterTypes: ["System.String"]);
        var function = Function("FileReadAllText", "fn:file-read-all-text", [path], [read]);

        var finding = Assert.Single(new TaintAnalysis(function).Vulnerabilities);
        Assert.Equal(TaintSink.FilePath, finding.Sink);
    }

    [Fact]
    public void WriteAllText_ContentArgumentIsNotAFilePathSink()
    {
        var content = Variable("user_input", "STRING", "param:file-write-content", true);
        var write = new BoundCallStatement(
            Span(244),
            "File.WriteAllText",
            [new BoundStringLiteral(Span(245), "safe.txt"), Reference(content, 246)],
            resolvedTypeName: "System.IO.File",
            resolvedMethodName: "WriteAllText",
            resolvedParameterTypes: ["System.String", "System.String"]);
        var function = Function("FileWriteContent", "fn:file-write-content", [content], [write]);

        Assert.Empty(new TaintAnalysis(function).Vulnerabilities);
    }

    [Fact]
    public void ProcessStartStatement_UsesExactResolvedSignature()
    {
        var input = Variable("user_input", "STRING", "param:process-input", true);
        var processStart = new BoundCallStatement(
            Span(144),
            "Process.Start",
            [Reference(input, 145)],
            resolvedTypeName: "System.Diagnostics.Process",
            resolvedMethodName: "Start",
            resolvedParameterTypes: ["STRING"]);
        var function = Function("ProcessStart", "fn:process-start", [input], [processStart]);

        var finding = Assert.Single(new TaintAnalysis(function).Vulnerabilities);
        Assert.Equal(TaintSink.CommandExecution, finding.Sink);
    }

    [Fact]
    public void ProcessStartStatement_NonMatchingResolvedSignatureIsNotASink()
    {
        var input = Variable("user_input", "STRING", "param:process-int-input", true);
        var processStart = new BoundCallStatement(
            Span(146),
            "Process.Start",
            [Reference(input, 147)],
            resolvedTypeName: "System.Diagnostics.Process",
            resolvedMethodName: "Start",
            resolvedParameterTypes: ["INT"]);
        var function = Function("ProcessStartInt", "fn:process-start-int", [input], [processStart]);

        Assert.Empty(new TaintAnalysis(function).Vulnerabilities);
    }

    [Fact]
    public void Binder_DerivesExactIdentityForProcessStartStatement()
    {
        var attributes = new AttributeCollection();
        var function = new FunctionNode(
            Span(148),
            "f001",
            "Run",
            Visibility.Public,
            [new ParameterNode(Span(149), "user_input", "STRING", attributes)],
            new OutputNode(Span(150), "VOID"),
            null,
            [
                new CallStatementNode(
                    Span(151),
                    "Process.Start",
                    false,
                    [new ReferenceNode(Span(152), "user_input")],
                    attributes),
            ],
            attributes);
        var module = new ModuleNode(
            Span(153),
            "m001",
            "Test",
            [],
            [function],
            attributes);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics).Bind(module);

        var call = Assert.IsType<BoundCallStatement>(bound.Functions.Single().Body.Single());
        Assert.Equal("System.Diagnostics.Process", call.ResolvedTypeName);
        Assert.Equal("Start", call.ResolvedMethodName);
        Assert.Equal(["STRING"], call.ResolvedParameterTypes);
        Assert.Contains(
            new TaintAnalysis(bound.Functions.Single()).Vulnerabilities,
            finding => finding.Sink == TaintSink.CommandExecution);
    }

    [Fact]
    public void InterproceduralCallReturnSourceSummary_PropagatesConsoleReadLine()
    {
        var readSymbol = FunctionSymbol("Read", "fn:read", []);
        var read = new BoundFunction(
            Span(83),
            readSymbol,
            [
                new BoundReturnStatement(
                    Span(84),
                    new BoundCallExpression(Span(85), "Console.ReadLine", [], "STRING")),
            ],
            new Scope());
        var value = Variable("value", "STRING", "local:read-value");
        var caller = Function(
            "ReadCaller",
            "fn:read-caller",
            [],
            [
                new BoundBindStatement(
                    Span(86),
                    value,
                    new BoundCallExpression(
                        Span(87),
                        "Read",
                        [],
                        "STRING",
                        resolvedSymbol: readSymbol)),
                Call("db.execute", Reference(value, 88), 89),
            ]);

        var diagnostics = new DiagnosticBag();
        new TaintAnalysisRunner(diagnostics).Analyze(Module(read, caller));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.SqlInjection);
    }

    [Fact]
    public void StrictUnknownExternal_ReturnsConservativeExternalApiSource()
    {
        var value = Variable("value", "STRING", "local:external-value");
        var function = Function(
            "UnknownExternal",
            "fn:unknown-external",
            [],
            [
                new BoundBindStatement(
                    Span(90),
                    value,
                    new BoundCallExpression(Span(91), "External.Fetch", [], "STRING")),
                Call("db.execute", Reference(value, 92), 93),
            ]);

        Assert.Empty(new TaintAnalysis(function).Vulnerabilities);
        var finding = Assert.Single(new TaintAnalysis(
            function,
            new TaintAnalysisOptions { StrictExternalCalls = true }).Vulnerabilities);
        Assert.Equal(TaintSource.ExternalApi, finding.Source);
    }

    private static BoundModule Module(params BoundFunction[] functions) =>
        new(Span(100), "Test", functions);

    private bool ExecuteCorpusCase(string id)
    {
        switch (id)
        {
            case "direct-source-sink":
            case "provenance":
                DirectSourceToSink_ReportsEvenWhenLegacyHopThresholdIsTwo();
                return true;
            case "console-readline-return-source":
                ConsoleReadLine_CallReturnSource_ReachesSink();
                return true;
            case "branch-join":
                BranchJoin_PreservesTaintFromOneReachableBranch();
                return true;
            case "loop-backedge":
                LoopFixedPoint_PropagatesTaintThroughBackEdge();
                return true;
            case "alias-field-collection":
                AliasFieldAndCollectionAccessPaths_PreserveTaint();
                return true;
            case "desanitize-not-sanitizer":
                ExactSanitizerIdentity_DoesNotTreatDesanitizeAsSanitizer();
                return true;
            case "recursive-sanitizer-summary":
                RecursiveSanitizerSummary_ConvergesOverFiniteLattice();
                return true;
            case "interprocedural-summary":
                InterproceduralReturnAndParameterSinkSummaries_PropagateExactResolvedCalls();
                return true;
            case "interprocedural-call-return-source":
                InterproceduralCallReturnSourceSummary_PropagatesConsoleReadLine();
                return true;
            case "strict-unknown-external":
                StrictUnknownExternal_ReturnsConservativeExternalApiSource();
                return true;
            case "verification-pass-module-summary":
                VerificationAnalysisPass_UsesModuleTaintSummaries();
                return true;
            case "may-alias-field":
                MayAliasJoin_WeakFieldWritesPreserveAllPossibleTargets();
                return true;
            case "may-alias-array":
                MayAliasJoin_WeakArrayElementWritesPreserveAllPossibleTargets();
                return true;
            case "resolved-local-sql-escape":
                ResolvedLocalNamedSqlEscape_IsNotTreatedAsBuiltInSanitizer();
                return true;
            case "resolved-custom-target-sanitizer":
                ResolvedCustomTypeCannotMatchTargetOnlySanitizer();
                return true;
            case "interprocedural-alternatives":
                InterproceduralAlternatives_UnionReturnAndParameterSinkSummaries();
                return true;
            case "alias-rebinding-field":
                AliasRebinding_PreservesOldFieldObjectForAlias();
                return true;
            case "alias-rebinding-array":
                AliasRebinding_PreservesOldArrayObjectForAlias();
                return true;
            case "field-reference-alias":
                FieldReferenceAlias_PreservesStoredObjectAfterSourceRebinding();
                return true;
            case "array-reference-alias":
                ArrayReferenceAlias_PreservesStoredObjectAfterSourceRebinding();
                return true;
            case "system-io-file-read":
                ReadAllText_PathArgumentIsAFilePathSink();
                return true;
            case "system-io-file-move-destination":
                Move_DestinationArgumentIsAFilePathSink();
                return true;
            case "system-io-file-write":
                SystemIoFilePathSinks_UseExactResolvedSignatures(
                    "WriteAllText",
                    ["System.String", "System.String"]);
                return true;
            case "system-io-file-delete":
                SystemIoFilePathSinks_UseExactResolvedSignatures("Delete", ["System.String"]);
                return true;
            case "system-io-file-open":
                SystemIoFilePathSinks_UseExactResolvedSignatures(
                    "Open",
                    ["System.String", "System.IO.FileMode"]);
                return true;
            case "process-start-signature":
                ProcessStartStatement_UsesExactResolvedSignature();
                return true;
            case "safe-strong-overwrite":
                SafeOverwrite_StronglyKillsEarlierTaint();
                return false;
            case "constants":
                ConstantsOnly_DoNotProduceAFinding();
                return false;
            case "process-start-non-sink-signature":
                ProcessStartStatement_NonMatchingResolvedSignatureIsNotASink();
                return false;
            case "system-io-file-write-content":
                WriteAllText_ContentArgumentIsNotAFilePathSink();
                return false;
            default:
                throw new InvalidOperationException($"Unknown taint regression corpus case '{id}'.");
        }
    }

    private static BoundFunction Function(
        string name,
        string id,
        IReadOnlyList<VariableSymbol> parameters,
        IReadOnlyList<BoundStatement> body) =>
        new(
            Span(101),
            FunctionSymbol(name, id, parameters),
            body,
            new Scope());

    private static FunctionSymbol FunctionSymbol(
        string name,
        string id,
        IReadOnlyList<VariableSymbol> parameters) =>
        new(new SymbolId(id), name, "VOID", parameters);

    private static VariableSymbol Variable(
        string name,
        string type,
        string id,
        bool parameter = false) =>
        new(new SymbolId(id), name, type, true, parameter, declarationSpan: Span(id.Length));

    private static BoundVariableExpression Reference(VariableSymbol variable, int span) =>
        new(Span(span), variable);

    private static BoundFieldAccessExpression Field(
        VariableSymbol target,
        string name,
        int span,
        string typeName = "STRING",
        VariableSymbol? resolvedField = null) =>
        Field(Reference(target, span + 1), name, span, typeName, resolvedField);

    private static BoundFieldAccessExpression Field(
        BoundExpression target,
        string name,
        int span,
        string typeName = "STRING",
        VariableSymbol? resolvedField = null) =>
        new(Span(span), target, name, typeName, resolvedField);

    private static BoundCallStatement Call(string target, BoundExpression argument, int span) =>
        new(Span(span), target, [argument]);
}
