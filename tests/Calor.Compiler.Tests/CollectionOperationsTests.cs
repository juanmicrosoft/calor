using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public class CollectionOperationsTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAllForParser();
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("clear")]
    [InlineData("insert")]
    [InlineData("contains-key")]
    [InlineData("contains-value")]
    [InlineData("contains")]
    [InlineData("count")]
    [InlineData("keyword-add")]
    public void NativeCollectionVisitors_RoundTripCallsWithoutRetiredTags(string operation)
    {
        var returnType = operation.StartsWith("contains", StringComparison.Ordinal) ? "bool"
            : operation == "count" ? "int" : "void";
        var source = $$"""
            module CollectionRoundTrip
              func Probe public (System.Collections.Generic.List<int>:items, System.Collections.Generic.Dictionary<string, int>:map, System.Collections.Generic.List<int>:@base) → {{returnType}}
                ! mut
            """;
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        var span = TextSpan.Empty;
        var value = new IntLiteralNode(span, 1);
        StatementNode statement = operation switch
        {
            "add" => new CollectionPushNode(span, "items",
                new CollectionCountNode(span, new ReferenceNode(span, "items"))),
            "remove" => new CollectionRemoveNode(span, "items", value),
            "clear" => new CollectionClearNode(span, "items"),
            "insert" => new CollectionInsertNode(span, "items", new IntLiteralNode(span, 0), value),
            "contains-key" => new ReturnStatementNode(span,
                new CollectionContainsNode(span, "map", new StringLiteralNode(span, "key"), ContainsMode.Key)),
            "contains-value" => new ReturnStatementNode(span,
                new CollectionContainsNode(span, "map", value, ContainsMode.DictValue)),
            "contains" => new ReturnStatementNode(span,
                new CollectionContainsNode(span, "items", value, ContainsMode.Value)),
            "count" => new ReturnStatementNode(span,
                new CollectionCountNode(span, new ReferenceNode(span, "items"))),
            "keyword-add" => new CollectionPushNode(span, "base", value),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var function = Assert.Single(module.Functions);
        var updatedFunction = new FunctionNode(function.Span,
            function.Id, function.Name, function.Visibility, function.Parameters, function.Output,
            function.Effects, [statement], function.Attributes);
        module = module.With(update =>
        {
            update.Functions = [updatedFunction];
            update.Items = [updatedFunction];
        });

        var native = new CalorEmitter().Emit(module);
        Assert.DoesNotContain("§", native);
        var reparsed = Parse(native, out diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        static string[] Tokens(string code) => CSharpSyntaxTree.ParseText(code).GetRoot()
            .DescendantTokens().Select(token => token.RawKind + ":" + token.ValueText).ToArray();
        Assert.Equal(Tokens(new CSharpEmitter().Emit(module)),
            Tokens(new CSharpEmitter().Emit(reparsed)));
    }
    #region Lexer Tests

    #endregion

    #region Parser Tests - List Creation

    #endregion

    #region Parser Tests - Dictionary Creation

    #endregion

    #region Parser Tests - Set Creation

    #endregion

    #region Parser Tests - Collection Operations

    #endregion

    #region Parser Tests - Contains Queries

    #endregion

    #region Parser Tests - Collection Count

    #endregion

    #region Parser Tests - Dictionary Foreach

    [Fact]
    public void Parse_DictionaryForeach_ReturnsDictForeachNode()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                each k, v in dict1
                  Console.WriteLine(k)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var foreachNode = func.Body[0] as DictionaryForeachNode;
        Assert.NotNull(foreachNode);
        Assert.NotEmpty(foreachNode.Id);
        Assert.Null(foreachNode.ToolingId);
        Assert.Equal("k", foreachNode.KeyName);
        Assert.Equal("v", foreachNode.ValueName);
        Assert.Single(foreachNode.Body);
    }

    #endregion

    #region Code Generation Tests

    [Fact]
    public void Emit_ListCreation_GeneratesCorrectCSharp()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:nums = new List<int>() { 1, 2, 3 }
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("List<int>", code);
        Assert.Contains("new List<int>()", code);
        Assert.Contains("1, 2, 3", code);
    }

    [Fact]
    public void Emit_DictionaryCreation_GeneratesCorrectCSharp()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let Dictionary<string, int>:ages = new Dictionary<string, int>() { { "alice", 30 }, { "bob", 25 } }
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("Dictionary<string, int>", code);
    }

    [Fact]
    public void Emit_SetCreation_GeneratesCorrectCSharp()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let HashSet<string>:items = new HashSet<string>() { "apple", "banana" }
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("HashSet<string>", code);
        Assert.Contains("new HashSet<string>()", code);
    }

    [Fact]
    public void Emit_CollectionPush_GeneratesAddCall()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                myList.Add(42)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myList.Add(42);", code);
    }

    [Fact]
    public void Emit_DictionaryPut_GeneratesIndexerAssignment()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                set myDict["key"] 42
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myDict[\"key\"] = 42;", code);
    }

    [Fact]
    public void Emit_CollectionRemove_GeneratesRemoveCall()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                myColl.Remove("item")
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myColl.Remove(\"item\");", code);
    }

    [Fact]
    public void Emit_CollectionClear_GeneratesClearCall()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                myColl.Clear()
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myColl.Clear();", code);
    }

    [Fact]
    public void Emit_CollectionInsert_GeneratesInsertCall()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                myList.Insert(0, "first")
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myList.Insert(0, \"first\");", code);
    }

    [Fact]
    public void Emit_CollectionContainsKey_GeneratesContainsKeyCall()
    {
        var source = """
            module Test Test block
              func HasKey () → bool
                !
                return myDict.ContainsKey("testKey")
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myDict.ContainsKey(\"testKey\")", code);
    }

    [Fact]
    public void Emit_CollectionContainsValue_GeneratesContainsValueCall()
    {
        var source = """
            module Test Test block
              func HasValue () → bool
                !
                return myDict.ContainsValue(42)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myDict.ContainsValue(42)", code);
    }

    [Fact]
    public void Emit_DictionaryForeach_GeneratesDeconstructingForeach()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                each key, value in myDict
                  Console.WriteLine(key)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("foreach (var (key, value) in myDict)", code);
    }

    [Fact]
    public void Emit_CollectionCount_GeneratesCountProperty()
    {
        var source = """
            module Test Test block
              func GetLength () → int
                !
                return myList . Count
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myList.Count", code);
    }
    #endregion
    #region Error Handling Tests

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Emit_EmptyCollections_GeneratesEmptyInitializers()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:emptyList = new List<int>()
                let Dictionary<string, string>:emptyDict = new Dictionary<string, string>()
                let HashSet<bool>:emptySet = new HashSet<bool>()
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("new List<int>()", code);
        Assert.Contains("new Dictionary<string, string>()", code);
        Assert.Contains("new HashSet<bool>()", code);
    }

    [Fact]
    public void Parse_MultipleCollectionOperations_InSequence()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:nums = new List<int>()
                nums.Add(1)
                nums.Add(2)
                nums.Insert(0, 0)
                nums.Remove(1)
                nums.Clear()
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        // 1 bind (list creation) + 5 operations
        Assert.Equal(6, func.Body.Count);
    }

    [Fact]
    public void Emit_CollectionWithExpressionElements_GeneratesCorrectCode()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:nums = new List<int>() { (1 + 2), (3 * 4) }
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("1 + 2", code);
        Assert.Contains("3 * 4", code);
    }

    #endregion

    private static void AssertChargedMutation(string source)
    {
        var result = Program.Compile(source, "collection-effects.calr");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("mut"));
    }

    #region Effect System Tests

    [Fact]
    public void Effect_CollectionPush_ReportsMutationWhenNotDeclared()
    {
        // A pure (`!`) public function that mutates a collection parameter must be charged mut.
        var source = """
            module Test Test block
              func Main public (System.Collections.Generic.List<int>:list1) → void
                !
                list1.Add(42)
            """;

        AssertChargedMutation(source);
    }

    [Fact]
    public void Effect_DictionaryPut_ReportsMutationWhenNotDeclared()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                set dict1["key"] 42
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var enforcementPass = new EffectEnforcementPass(effectDiagnostics, UnknownCallPolicy.Permissive);
        enforcementPass.Enforce(module);

        Assert.Contains(effectDiagnostics, d => d.Message.Contains("mut"));
    }

    [Fact]
    public void Effect_CollectionRemove_ReportsMutationWhenNotDeclared()
    {
        // A pure (`!`) public function that mutates a collection parameter must be charged mut.
        var source = """
            module Test Test block
              func Main public (System.Collections.Generic.List<string>:list1) → void
                !
                list1.Remove("item")
            """;

        AssertChargedMutation(source);
    }

    [Fact]
    public void Effect_CollectionClear_ReportsMutationWhenNotDeclared()
    {
        // A pure (`!`) public function that mutates a collection parameter must be charged mut.
        var source = """
            module Test Test block
              func Main public (System.Collections.Generic.List<string>:list1) → void
                !
                list1.Clear()
            """;

        AssertChargedMutation(source);
    }

    [Fact]
    public void Effect_CollectionInsert_ReportsMutationWhenNotDeclared()
    {
        // A pure (`!`) public function that mutates a collection parameter must be charged mut.
        var source = """
            module Test Test block
              func Main public (System.Collections.Generic.List<string>:list1) → void
                !
                list1.Insert(0, "first")
            """;

        AssertChargedMutation(source);
    }

    [Fact]
    public void Effect_CollectionSetIndex_ReportsMutationWhenNotDeclared()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                set list1[0] 42
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var enforcementPass = new EffectEnforcementPass(effectDiagnostics, UnknownCallPolicy.Permissive);
        enforcementPass.Enforce(module);

        Assert.Contains(effectDiagnostics, d => d.Message.Contains("mut"));
    }

    [Fact]
    public void Effect_CollectionContains_NoMutationReported()
    {
        var source = """
            module Test Test block
              func Main () → bool
                !
                return list1.Contains(42)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var enforcementPass = new EffectEnforcementPass(effectDiagnostics, UnknownCallPolicy.Permissive);
        enforcementPass.Enforce(module);

        Assert.DoesNotContain(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    [Fact]
    public void Effect_ListCreation_NoMutationReported()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:nums = new List<int>() { 1, 2 }
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var enforcementPass = new EffectEnforcementPass(effectDiagnostics, UnknownCallPolicy.Permissive);
        enforcementPass.Enforce(module);

        Assert.DoesNotContain(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_CollectionOperations_EmitsValidCalor()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                nums.Add(42)
                set dict["key"] 99
                nums.Insert(0, 1)
                nums.Remove(42)
                dict.Clear()
                set nums[0] 10
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        // Emit back to Calor
        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        // Re-parse the emitted Calor
        var reparsed = Parse(calorOutput, out var rediagnostics);
        Assert.False(rediagnostics.HasErrors, $"Round-trip failed: {string.Join(", ", rediagnostics.Select(d => d.Message))}");

        // Verify statement count matches
        Assert.Equal(module.Functions[0].Body.Count, reparsed.Functions[0].Body.Count);
    }

    [Fact]
    public void RoundTrip_DictionaryForeach_EmitsValidCalor()
    {
        var source = """
            module Test Test block
              func Main () → void
                ! cw
                each k, v in myDict
                  Console.WriteLine(k)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        // Emit back to Calor
        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        // Re-parse the emitted Calor
        var reparsed = Parse(calorOutput, out var rediagnostics);
        Assert.False(rediagnostics.HasErrors, $"Round-trip failed: {string.Join(", ", rediagnostics.Select(d => d.Message))}");

        // Verify structure
        var foreachNode = reparsed.Functions[0].Body[0] as DictionaryForeachNode;
        Assert.NotNull(foreachNode);
        Assert.NotEmpty(foreachNode.Id);
        Assert.Null(foreachNode.ToolingId);
        Assert.Equal("k", foreachNode.KeyName);
        Assert.Equal("v", foreachNode.ValueName);
    }

    #endregion

    #region Nested Collection Tests

    [Fact]
    public void Emit_ListOfLists_GeneratesNestedListType()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<List<int>>:matrix = new List<List<int>>()
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("List<List<int>>", code);
    }

    [Fact]
    public void Emit_DictionaryWithListValue_GeneratesCorrectType()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let Dictionary<string, List<int>>:grouped = new Dictionary<string, List<int>>()
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("Dictionary<string, List<int>>", code);
    }

    [Fact]
    public void Emit_SetOfSets_GeneratesNestedSetType()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let HashSet<HashSet<string>>:nestedSet = new HashSet<HashSet<string>>()
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("HashSet<HashSet<string>>", code);
    }

    #endregion

    #region Collection Indexing Tests

    [Fact]
    public void Parse_ArrayAccess_ReturnsArrayAccessNode()
    {
        var source = """
            module Test Test block
              func GetFirst () → int
                !
                return myList[0]
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);

        var accessNode = returnStmt.Expression as ArrayAccessNode;
        Assert.NotNull(accessNode);
    }

    [Fact]
    public void Emit_ArrayAccess_GeneratesIndexerAccess()
    {
        var source = """
            module Test Test block
              func GetFirst () → int
                !
                return myList[0]
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myList[0]", code);
    }

    [Fact]
    public void RoundTrip_ArrayAccess_EmitsValidCalor()
    {
        var source = """
            module Test Test block
              func GetFirst () → int
                !
                return myList[0]
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        // Emit back to Calor
        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        // Should contain §IDX syntax (brace format for simple references)
        Assert.Contains("myList[0]", calorOutput);

        // Re-parse the emitted Calor
        var reparsed = Parse(calorOutput, out var rediagnostics);
        Assert.False(rediagnostics.HasErrors, $"Round-trip failed: {string.Join(", ", rediagnostics.Select(d => d.Message))}");
    }

    #endregion

    #region Semantic Type Checking Tests

    [Fact]
    public void TypeCheck_ListCreation_ValidElements_NoErrors()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:numbers = new List<int>() { 1, 2, 3 }
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_DictionaryCreation_ValidEntries_NoErrors()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let Dictionary<string, int>:ages = new Dictionary<string, int>() { { "alice", 30 }, { "bob", 25 } }
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_SetCreation_ValidElements_NoErrors()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let HashSet<string>:fruits = new HashSet<string>() { "apple", "banana" }
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_CollectionPush_ValidValue_NoErrors()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:numbers = new List<int>() { 1, 2 }
                numbers.Add(3)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_DictionaryPut_ValidKeyValue_NoErrors()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let Dictionary<string, int>:ages = new Dictionary<string, int>() { { "alice", 30 } }
                set ages["bob"] 25
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_CollectionSetIndex_ValidIndex_NoErrors()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:numbers = new List<int>() { 1, 2 }
                set numbers[0] 99
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_CollectionInsert_ValidIndexAndValue_NoErrors()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:numbers = new List<int>() { 1, 2 }
                numbers.Insert(0, 99)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_CollectionClear_ValidCollection_NoErrors()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:numbers = new List<int>() { 1, 2 }
                numbers.Clear()
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_CollectionRemove_ValidValue_NoErrors()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:numbers = new List<int>() { 1, 2 }
                numbers.Remove(1)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    #endregion

    #region Integration Tests - Collections with Other Features

    [Fact]
    public void Parse_ForeachOverList_ParsesCorrectly()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:numbers = new List<int>() { 1, 2, 3 }
                each n in numbers
                  Console.WriteLine(n)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var func = module.Functions[0];
        Assert.Equal(2, func.Body.Count);
        Assert.IsType<ForeachStatementNode>(func.Body[1]);
    }

    [Fact]
    public void Emit_ForeachOverList_GeneratesCorrectCSharp()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:numbers = new List<int>() { 1, 2, 3 }
                each n in numbers
                  Console.WriteLine(n)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("new List<int>() { 1, 2, 3 }", csharp);
        Assert.Contains("foreach (var n in numbers)", csharp);
    }

    [Fact]
    public void Parse_DictionaryForeachKV_ParsesCorrectly()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let Dictionary<string, int>:scores = new Dictionary<string, int>() { { "alice", 100 }, { "bob", 85 } }
                each name, score in scores
                  Console.WriteLine(name)
                  Console.WriteLine(score)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var func = module.Functions[0];
        Assert.Equal(2, func.Body.Count);
        Assert.IsType<DictionaryForeachNode>(func.Body[1]);

        var foreachKV = func.Body[1] as DictionaryForeachNode;
        Assert.NotNull(foreachKV);
        Assert.Equal("name", foreachKV.KeyName);
        Assert.Equal("score", foreachKV.ValueName);
    }

    [Fact]
    public void Emit_DictionaryForeachKV_GeneratesCorrectCSharp()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let Dictionary<string, int>:scores = new Dictionary<string, int>() { { "alice", 100 }, { "bob", 85 } }
                each name, score in scores
                  Console.WriteLine(name)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("new Dictionary<string, int>()", csharp);
        Assert.Contains("foreach (var (name, score) in scores)", csharp);
    }

    [Fact]
    public void Emit_CollectionWithMultipleOperations_GeneratesCorrectCSharp()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                let List<int>:data = new List<int>() { 1, 2, 3 }
                data.Add(4)
                data.Insert(0, 0)
                set data[2] 99
                data.Remove(1)
                data.Clear()
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("new List<int>() { 1, 2, 3 }", csharp);
        Assert.Contains("data.Add(4);", csharp);
        Assert.Contains("data.Insert(0, 0);", csharp);
        Assert.Contains("data[2] = 99;", csharp);
        Assert.Contains("data.Remove(1);", csharp);
        Assert.Contains("data.Clear();", csharp);
    }

    [Fact]
    public void Emit_CollectionContainsExpression_GeneratesCorrectCSharp()
    {
        var source = """
            module Test Test block
              func Main () → bool
                !
                let List<int>:items = new List<int>() { 1, 2, 3 }
                return items.Contains(2)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("return items.Contains(2)", csharp);
    }

    [Fact]
    public void Emit_DictionaryContainsKey_GeneratesCorrectCSharp()
    {
        var source = """
            module Test Test block
              func Main () → bool
                !
                let Dictionary<string, int>:cache = new Dictionary<string, int>() { { "test", 42 } }
                return cache.ContainsKey("test")
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("return cache.ContainsKey(\"test\")", csharp);
    }

    [Fact]
    public void Emit_CollectionCount_GeneratesCorrectCSharp()
    {
        var source = """
            module Test Test block
              func Main () → int
                !
                let List<int>:items = new List<int>() { 1, 2, 3 }
                return items . Count
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("return items.Count", csharp);
    }

    [Fact]
    public void EffectSystem_CollectionMutations_TracksMutation()
    {
        var source = """
            module Test Test block
              func TestFunc () → void
                !
                let List<int>:a = new List<int>()
                a.Add(1)
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var effectDiagnostics = new DiagnosticBag();
        var enforcementPass = new EffectEnforcementPass(effectDiagnostics, UnknownCallPolicy.Permissive);
        enforcementPass.Enforce(module);

        // Should report mutation effect for PUSH operation
        Assert.Contains(effectDiagnostics, d => d.Message.Contains("mut"));
    }

    #endregion

    #region §IDX Format and Error Tests

    [Fact]
    public void Parse_ArrayAccess_SpaceFormat_ParsesCorrectly()
    {
        // §IDX arr 0 (correct format) should parse to ArrayAccessNode
        var source = """
            module Test Test block
              func Get () → int
                !
                return args[0]
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var func = module.Functions[0];
        var returnStmt = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        var access = Assert.IsType<ArrayAccessNode>(returnStmt.Expression);
        Assert.IsType<ReferenceNode>(access.Array);
        Assert.IsType<IntLiteralNode>(access.Index);
    }

    [Fact]
    public void Emit_ArrayAccess_EmitsBraceFormat()
    {
        // CalorEmitter should emit §IDX{arr} 0 (brace format for simple references)
        var span = new TextSpan(0, 0, 0, 0);
        var arrayRef = new ReferenceNode(span, "arr");
        var index = new IntLiteralNode(span, 0);
        var accessNode = new ArrayAccessNode(span, arrayRef, index);

        var emitter = new CalorEmitter();
        var output = accessNode.Accept(emitter);

        Assert.Equal("arr[0]", output);
    }

    [Fact]
    public void Parse_ArrayAccess_BraceFormat_IsValidSyntax()
    {
        // §IDX{arr} 0 (brace format) is valid — the attribute provides the array reference
        var source = """
            module Test Test block
              func Get () → int
                !
                return arr[0]
            """;

        var module = Parse(source, out var diagnostics);

        // Should parse without errors — §IDX{arr} is now valid attribute syntax
        Assert.False(diagnostics.HasErrors, "§IDX{arr} should be valid syntax");

        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        Assert.IsType<ArrayAccessNode>(returnStmt!.Expression);
    }

    [Fact]
    public void RoundTrip_ArrayAccess_EmitsAndReparsesCorrectly()
    {
        var source = """
            module Test Test block
              func GetFirst () → int
                !
                return myList[0]
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        // Emit → reparse roundtrip
        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);
        Assert.Contains("myList[0]", calorOutput);

        var reparsed = Parse(calorOutput, out var rediagnostics);
        Assert.False(rediagnostics.HasErrors, $"Round-trip failed: {string.Join(", ", rediagnostics.Select(d => d.Message))}");

        var func = reparsed.Functions[0];
        var returnStmt = Assert.IsType<ReturnStatementNode>(func.Body[0]);
        Assert.IsType<ArrayAccessNode>(returnStmt.Expression);
    }
    #endregion
    #region Foreach With Index Tests

    [Fact]
    public void Parse_ForeachWithoutIndex_IndexIsNull()
    {
        var source = """
            module Test Test block
              func Main () → void
                !
                each int:item in items
                  Console.WriteLine(item)
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var func = module.Functions[0];
        var foreachStmt = Assert.IsType<ForeachStatementNode>(func.Body[0]);
        Assert.Equal("item", foreachStmt.VariableName);
        Assert.Equal("int", foreachStmt.VariableType);
        Assert.Null(foreachStmt.IndexVariableName);
    }

    [Fact]
    public void FeatureSupport_ForeachIndex_IsFullySupported()
    {
        Assert.True(FeatureSupport.IsFullySupported("foreach-index"));
    }

    #endregion
}
