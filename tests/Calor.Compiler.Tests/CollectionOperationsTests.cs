using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class CollectionOperationsTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static List<Token> Tokenize(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        return lexer.TokenizeAll();
    }

    #region Lexer Tests

    [Fact]
    public void Tokenize_ListKeywords_ReturnsCorrectTokens()
    {
        var tokens = Tokenize("§LIST §/LIST", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.List, tokens[0].Kind);
        Assert.Equal(TokenKind.EndList, tokens[1].Kind);
    }

    [Fact]
    public void Tokenize_DictKeywords_ReturnsCorrectTokens()
    {
        var tokens = Tokenize("§DICT §/DICT §KV", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Dict, tokens[0].Kind);
        Assert.Equal(TokenKind.EndDict, tokens[1].Kind);
        Assert.Equal(TokenKind.KeyValue, tokens[2].Kind);
    }

    [Fact]
    public void Tokenize_SetKeywords_ReturnsCorrectTokens()
    {
        var tokens = Tokenize("§HSET §/HSET", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.HashSet, tokens[0].Kind);
        Assert.Equal(TokenKind.EndHashSet, tokens[1].Kind);
    }

    [Fact]
    public void Tokenize_CollectionOperations_ReturnsCorrectTokens()
    {
        var tokens = Tokenize("§PUSH §PUT §REM §SETIDX §CLR §INS §HAS §KEY §VAL", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Push, tokens[0].Kind);
        Assert.Equal(TokenKind.Put, tokens[1].Kind);
        Assert.Equal(TokenKind.Remove, tokens[2].Kind);
        Assert.Equal(TokenKind.SetIndex, tokens[3].Kind);
        Assert.Equal(TokenKind.Clear, tokens[4].Kind);
        Assert.Equal(TokenKind.Insert, tokens[5].Kind);
        Assert.Equal(TokenKind.Has, tokens[6].Kind);
        Assert.Equal(TokenKind.Key, tokens[7].Kind);
        Assert.Equal(TokenKind.Val, tokens[8].Kind);
    }

    [Fact]
    public void Tokenize_EachKVKeywords_ReturnsCorrectTokens()
    {
        var tokens = Tokenize("§EACHKV §/EACHKV", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.EachKV, tokens[0].Kind);
        Assert.Equal(TokenKind.EndEachKV, tokens[1].Kind);
    }

    [Fact]
    public void Tokenize_CountKeyword_ReturnsCorrectToken()
    {
        var tokens = Tokenize("§CNT", out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(TokenKind.Count, tokens[0].Kind);
    }

    #endregion

    #region Parser Tests - List Creation

    [Fact]
    public void Parse_ListCreation_Empty_ReturnsListNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{list1:i32}
              §/LIST{list1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var bindStmt = func.Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);

        var listCreation = bindStmt.Initializer as ListCreationNode;
        Assert.NotNull(listCreation);
        Assert.Equal("list1", listCreation.Id);
        Assert.Equal("i32", listCreation.ElementType);
        Assert.Empty(listCreation.Elements);
    }

    [Fact]
    public void Parse_ListCreation_WithElements_ReturnsListNodeWithElements()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{list1:i32}
                1
                2
                3
              §/LIST{list1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var bindStmt = func.Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);

        var listCreation = bindStmt.Initializer as ListCreationNode;
        Assert.NotNull(listCreation);
        Assert.Equal(3, listCreation.Elements.Count);
    }

    #endregion

    #region Parser Tests - Dictionary Creation

    [Fact]
    public void Parse_DictionaryCreation_Empty_ReturnsDictNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{dict1:str:i32}
              §/DICT{dict1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var bindStmt = func.Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);

        var dictCreation = bindStmt.Initializer as DictionaryCreationNode;
        Assert.NotNull(dictCreation);
        Assert.Equal("dict1", dictCreation.Id);
        Assert.Equal("str", dictCreation.KeyType);
        Assert.Equal("i32", dictCreation.ValueType);
        Assert.Empty(dictCreation.Entries);
    }

    [Fact]
    public void Parse_DictionaryCreation_WithEntries_ReturnsDictNodeWithEntries()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{dict1:str:i32}
                §KV "one" 1
                §KV "two" 2
              §/DICT{dict1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var bindStmt = func.Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);

        var dictCreation = bindStmt.Initializer as DictionaryCreationNode;
        Assert.NotNull(dictCreation);
        Assert.Equal(2, dictCreation.Entries.Count);
    }

    #endregion

    #region Parser Tests - Set Creation

    [Fact]
    public void Parse_SetCreation_Empty_ReturnsSetNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §HSET{set1:str}
              §/HSET{set1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var bindStmt = func.Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);

        var setCreation = bindStmt.Initializer as SetCreationNode;
        Assert.NotNull(setCreation);
        Assert.Equal("set1", setCreation.Id);
        Assert.Equal("str", setCreation.ElementType);
        Assert.Empty(setCreation.Elements);
    }

    [Fact]
    public void Parse_SetCreation_WithElements_ReturnsSetNodeWithElements()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §HSET{set1:str}
                "apple"
                "banana"
              §/HSET{set1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var bindStmt = func.Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);

        var setCreation = bindStmt.Initializer as SetCreationNode;
        Assert.NotNull(setCreation);
        Assert.Equal(2, setCreation.Elements.Count);
    }

    #endregion

    #region Parser Tests - Collection Operations

    [Fact]
    public void Parse_CollectionPush_ReturnsPushNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §PUSH{list1} 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var pushNode = func.Body[0] as CollectionPushNode;
        Assert.NotNull(pushNode);
        Assert.Equal("list1", pushNode.CollectionName);
    }

    [Fact]
    public void Parse_DictionaryPut_ReturnsPutNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §PUT{dict1} "key" 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var putNode = func.Body[0] as DictionaryPutNode;
        Assert.NotNull(putNode);
        Assert.Equal("dict1", putNode.DictionaryName);
    }

    [Fact]
    public void Parse_CollectionRemove_ReturnsRemoveNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §REM{coll1} "item"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var removeNode = func.Body[0] as CollectionRemoveNode;
        Assert.NotNull(removeNode);
        Assert.Equal("coll1", removeNode.CollectionName);
    }

    [Fact]
    public void Parse_CollectionClear_ReturnsClearNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §CLR{coll1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var clearNode = func.Body[0] as CollectionClearNode;
        Assert.NotNull(clearNode);
        Assert.Equal("coll1", clearNode.CollectionName);
    }

    [Fact]
    public void Parse_CollectionInsert_ReturnsInsertNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §INS{list1} 0 "first"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var insertNode = func.Body[0] as CollectionInsertNode;
        Assert.NotNull(insertNode);
        Assert.Equal("list1", insertNode.CollectionName);
    }

    [Fact]
    public void Parse_CollectionSetIndex_ReturnsSetIndexNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §SETIDX{list1} 0 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var setIndexNode = func.Body[0] as CollectionSetIndexNode;
        Assert.NotNull(setIndexNode);
        Assert.Equal("list1", setIndexNode.CollectionName);
    }

    #endregion

    #region Parser Tests - Contains Queries

    [Fact]
    public void Parse_CollectionContains_Value_ReturnsContainsNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{bool}
              §R §HAS{list1} 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);

        var containsNode = returnStmt.Expression as CollectionContainsNode;
        Assert.NotNull(containsNode);
        Assert.Equal("list1", containsNode.CollectionName);
        Assert.Equal(ContainsMode.Value, containsNode.Mode);
    }

    [Fact]
    public void Parse_CollectionContains_Key_ReturnsContainsNodeWithKeyMode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{bool}
              §R §HAS{dict1} §KEY "mykey"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);

        var containsNode = returnStmt.Expression as CollectionContainsNode;
        Assert.NotNull(containsNode);
        Assert.Equal("dict1", containsNode.CollectionName);
        Assert.Equal(ContainsMode.Key, containsNode.Mode);
    }

    [Fact]
    public void Parse_CollectionContains_DictValue_ReturnsContainsNodeWithDictValueMode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{bool}
              §R §HAS{dict1} §VAL 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);

        var containsNode = returnStmt.Expression as CollectionContainsNode;
        Assert.NotNull(containsNode);
        Assert.Equal("dict1", containsNode.CollectionName);
        Assert.Equal(ContainsMode.DictValue, containsNode.Mode);
    }

    #endregion

    #region Parser Tests - Collection Count

    [Fact]
    public void Parse_CollectionCount_ReturnsCountNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{i32}
              §R §CNT myList
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);

        var countNode = returnStmt.Expression as CollectionCountNode;
        Assert.NotNull(countNode);
    }

    #endregion

    #region Parser Tests - Dictionary Foreach

    [Fact]
    public void Parse_DictionaryForeach_ReturnsDictForeachNode()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §EACHKV{e1:k:v} dict1
                §P k
              §/EACHKV{e1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];

        var foreachNode = func.Body[0] as DictionaryForeachNode;
        Assert.NotNull(foreachNode);
        Assert.Equal("e1", foreachNode.Id);
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{nums:i32}
                1
                2
                3
              §/LIST{nums}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{ages:str:i32}
                §KV "alice" 30
                §KV "bob" 25
              §/DICT{ages}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §HSET{items:str}
                "apple"
                "banana"
              §/HSET{items}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §PUSH{myList} 42
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §PUT{myDict} "key" 42
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §REM{myColl} "item"
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §CLR{myColl}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §INS{myList} 0 "first"
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:HasKey:pub}
              §O{bool}
              §R §HAS{myDict} §KEY "testKey"
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:HasValue:pub}
              §O{bool}
              §R §HAS{myDict} §VAL 42
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §EACHKV{e1:key:value} myDict
                §P key
              §/EACHKV{e1}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:GetLength:pub}
              §O{i32}
              §R §CNT myList
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("myList.Count", code);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void Parse_MismatchedListId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{list1:i32}
              §/LIST{list2}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.MismatchedId);
    }

    [Fact]
    public void Parse_MismatchedDictId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{dict1:str:i32}
              §/DICT{dict2}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.MismatchedId);
    }

    [Fact]
    public void Parse_MismatchedEachKVId_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §EACHKV{e1:k:v} dict1
                §P k
              §/EACHKV{e2}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.MismatchedId);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Parse_EmptyList_ReturnsListNodeWithNoElements()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{empty:str}
              §/LIST{empty}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var listCreation = bindStmt?.Initializer as ListCreationNode;
        Assert.NotNull(listCreation);
        Assert.Empty(listCreation.Elements);
    }

    [Fact]
    public void Parse_EmptyDictionary_ReturnsDictNodeWithNoEntries()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{empty:str:i32}
              §/DICT{empty}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var dictCreation = bindStmt?.Initializer as DictionaryCreationNode;
        Assert.NotNull(dictCreation);
        Assert.Empty(dictCreation.Entries);
    }

    [Fact]
    public void Parse_EmptyHashSet_ReturnsSetNodeWithNoElements()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §HSET{empty:i32}
              §/HSET{empty}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var setCreation = bindStmt?.Initializer as SetCreationNode;
        Assert.NotNull(setCreation);
        Assert.Empty(setCreation.Elements);
    }

    [Fact]
    public void Emit_EmptyCollections_GeneratesEmptyInitializers()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{emptyList:i32}
              §/LIST{emptyList}
              §DICT{emptyDict:str:str}
              §/DICT{emptyDict}
              §HSET{emptySet:bool}
              §/HSET{emptySet}
            §/F{f001}
            §/M{m001}
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
    public void Parse_CollectionWithSingleElement_ReturnsCorrectCount()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{single:i32}
                42
              §/LIST{single}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var listCreation = bindStmt?.Initializer as ListCreationNode;
        Assert.NotNull(listCreation);
        Assert.Single(listCreation.Elements);
    }

    [Fact]
    public void Parse_DictionaryWithSingleEntry_ReturnsCorrectCount()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{single:str:i32}
                §KV "key" 1
              §/DICT{single}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var dictCreation = bindStmt?.Initializer as DictionaryCreationNode;
        Assert.NotNull(dictCreation);
        Assert.Single(dictCreation.Entries);
    }

    [Fact]
    public void Parse_MultipleCollectionOperations_InSequence()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{nums:i32}
              §/LIST{nums}
              §PUSH{nums} 1
              §PUSH{nums} 2
              §INS{nums} 0 0
              §REM{nums} 1
              §CLR{nums}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{nums:i32}
                (+ 1 2)
                (* 3 4)
              §/LIST{nums}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("(1 + 2)", code);
        Assert.Contains("(3 * 4)", code);
    }

    #endregion

    #region Effect System Tests

    [Fact]
    public void Effect_CollectionPush_ReportsMutationWhenNotDeclared()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §PUSH{list1} 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var checker = new EffectChecker(effectDiagnostics);
        checker.Check(module);

        Assert.Contains(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    [Fact]
    public void Effect_DictionaryPut_ReportsMutationWhenNotDeclared()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §PUT{dict1} "key" 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var checker = new EffectChecker(effectDiagnostics);
        checker.Check(module);

        Assert.Contains(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    [Fact]
    public void Effect_CollectionRemove_ReportsMutationWhenNotDeclared()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §REM{coll1} "item"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var checker = new EffectChecker(effectDiagnostics);
        checker.Check(module);

        Assert.Contains(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    [Fact]
    public void Effect_CollectionClear_ReportsMutationWhenNotDeclared()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §CLR{coll1}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var checker = new EffectChecker(effectDiagnostics);
        checker.Check(module);

        Assert.Contains(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    [Fact]
    public void Effect_CollectionInsert_ReportsMutationWhenNotDeclared()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §INS{list1} 0 "first"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var checker = new EffectChecker(effectDiagnostics);
        checker.Check(module);

        Assert.Contains(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    [Fact]
    public void Effect_CollectionSetIndex_ReportsMutationWhenNotDeclared()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §SETIDX{list1} 0 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var checker = new EffectChecker(effectDiagnostics);
        checker.Check(module);

        Assert.Contains(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    [Fact]
    public void Effect_CollectionContains_NoMutationReported()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{bool}
              §R §HAS{list1} 42
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var checker = new EffectChecker(effectDiagnostics);
        checker.Check(module);

        Assert.DoesNotContain(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    [Fact]
    public void Effect_ListCreation_NoMutationReported()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{nums:i32}
                1
                2
              §/LIST{nums}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var effectDiagnostics = new DiagnosticBag();
        var checker = new EffectChecker(effectDiagnostics);
        checker.Check(module);

        Assert.DoesNotContain(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_ListCreation_EmitsValidCalor()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{nums:i32}
                1
                2
                3
              §/LIST{nums}
            §/F{f001}
            §/M{m001}
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
        var func = reparsed.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var listCreation = bindStmt?.Initializer as ListCreationNode;
        Assert.NotNull(listCreation);
        Assert.Equal(3, listCreation.Elements.Count);
    }

    [Fact]
    public void RoundTrip_DictionaryCreation_EmitsValidCalor()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{ages:str:i32}
                §KV "alice" 30
                §KV "bob" 25
              §/DICT{ages}
            §/F{f001}
            §/M{m001}
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
        var func = reparsed.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var dictCreation = bindStmt?.Initializer as DictionaryCreationNode;
        Assert.NotNull(dictCreation);
        Assert.Equal(2, dictCreation.Entries.Count);
    }

    [Fact]
    public void RoundTrip_HashSetCreation_EmitsValidCalor()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §HSET{tags:str}
                "important"
                "urgent"
              §/HSET{tags}
            §/F{f001}
            §/M{m001}
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
        var func = reparsed.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var setCreation = bindStmt?.Initializer as SetCreationNode;
        Assert.NotNull(setCreation);
        Assert.Equal(2, setCreation.Elements.Count);
    }

    [Fact]
    public void RoundTrip_CollectionOperations_EmitsValidCalor()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §PUSH{nums} 42
              §PUT{dict} "key" 99
              §INS{nums} 0 1
              §REM{nums} 42
              §CLR{dict}
              §SETIDX{nums} 0 10
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §E{cw}
              §EACHKV{e1:k:v} myDict
                §P k
              §/EACHKV{e1}
            §/F{f001}
            §/M{m001}
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
        Assert.Equal("e1", foreachNode.Id);
        Assert.Equal("k", foreachNode.KeyName);
        Assert.Equal("v", foreachNode.ValueName);
    }

    #endregion

    #region Nested Collection Tests

    [Fact]
    public void Parse_ListOfLists_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{outer:List<i32>}
              §/LIST{outer}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var listCreation = bindStmt?.Initializer as ListCreationNode;
        Assert.NotNull(listCreation);
        Assert.Equal("List<i32>", listCreation.ElementType);
    }

    [Fact]
    public void Emit_ListOfLists_GeneratesNestedListType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{matrix:List<i32>}
              §/LIST{matrix}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("List<List<int>>", code);
    }

    [Fact]
    public void Parse_DictionaryWithListValue_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{grouped:str:List<i32>}
              §/DICT{grouped}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var dictCreation = bindStmt?.Initializer as DictionaryCreationNode;
        Assert.NotNull(dictCreation);
        Assert.Equal("str", dictCreation.KeyType);
        Assert.Equal("List<i32>", dictCreation.ValueType);
    }

    [Fact]
    public void Emit_DictionaryWithListValue_GeneratesCorrectType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{grouped:str:List<i32>}
              §/DICT{grouped}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("Dictionary<string, List<int>>", code);
    }

    [Fact]
    public void Parse_SetOfSets_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §HSET{nestedSet:HashSet<str>}
              §/HSET{nestedSet}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        var setCreation = bindStmt?.Initializer as SetCreationNode;
        Assert.NotNull(setCreation);
        Assert.Equal("HashSet<str>", setCreation.ElementType);
    }

    [Fact]
    public void Emit_SetOfSets_GeneratesNestedSetType()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §HSET{nestedSet:HashSet<str>}
              §/HSET{nestedSet}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:GetFirst:pub}
              §O{i32}
              §R §IDX myList 0
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:GetFirst:pub}
              §O{i32}
              §R §IDX myList 0
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:GetFirst:pub}
              §O{i32}
              §R §IDX myList 0
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        // Emit back to Calor
        var emitter = new CalorEmitter();
        var calorOutput = emitter.Emit(module);

        // Should contain §IDX syntax
        Assert.Contains("§IDX{", calorOutput);

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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
                3
              §/LIST{numbers}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_ListCreation_InvalidElement_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                "invalid"
                3
              §/LIST{numbers}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.True(diagnostics.HasErrors, "Expected type error for string in int list");
        Assert.Contains(diagnostics, d => d.Message.Contains("type mismatch"));
    }

    [Fact]
    public void TypeCheck_DictionaryCreation_ValidEntries_NoErrors()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{ages:str:i32}
                §KV "alice" 30
                §KV "bob" 25
              §/DICT{ages}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_DictionaryCreation_InvalidKeyType_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{ages:str:i32}
                §KV 123 30
                §KV "bob" 25
              §/DICT{ages}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.True(diagnostics.HasErrors, "Expected type error for int key in string-keyed dict");
        Assert.Contains(diagnostics, d => d.Message.Contains("Dictionary key type mismatch"));
    }

    [Fact]
    public void TypeCheck_DictionaryCreation_InvalidValueType_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{ages:str:i32}
                §KV "alice" "thirty"
              §/DICT{ages}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.True(diagnostics.HasErrors, "Expected type error for string value in int-valued dict");
        Assert.Contains(diagnostics, d => d.Message.Contains("Dictionary value type mismatch"));
    }

    [Fact]
    public void TypeCheck_SetCreation_ValidElements_NoErrors()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §HSET{fruits:str}
                "apple"
                "banana"
              §/HSET{fruits}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_SetCreation_InvalidElement_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §HSET{fruits:str}
                "apple"
                42
              §/HSET{fruits}
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.True(diagnostics.HasErrors, "Expected type error for int in string set");
        Assert.Contains(diagnostics, d => d.Message.Contains("type mismatch"));
    }

    [Fact]
    public void TypeCheck_CollectionPush_ValidValue_NoErrors()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
              §/LIST{numbers}
              §PUSH{numbers} 3
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_CollectionPush_InvalidValue_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
              §/LIST{numbers}
              §PUSH{numbers} "invalid"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.True(diagnostics.HasErrors, "Expected type error for string push to int list");
        Assert.Contains(diagnostics, d => d.Message.Contains("Cannot add"));
    }

    [Fact]
    public void TypeCheck_DictionaryPut_ValidKeyValue_NoErrors()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{ages:str:i32}
                §KV "alice" 30
              §/DICT{ages}
              §PUT{ages} "bob" 25
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_DictionaryPut_InvalidKey_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{ages:str:i32}
                §KV "alice" 30
              §/DICT{ages}
              §PUT{ages} 123 25
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.True(diagnostics.HasErrors, "Expected type error for int key in string-keyed dict");
        Assert.Contains(diagnostics, d => d.Message.Contains("Dictionary key type mismatch"));
    }

    [Fact]
    public void TypeCheck_CollectionSetIndex_ValidIndex_NoErrors()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
              §/LIST{numbers}
              §SETIDX{numbers} 0 99
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_CollectionSetIndex_InvalidValue_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
              §/LIST{numbers}
              §SETIDX{numbers} 0 "invalid"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.True(diagnostics.HasErrors, "Expected type error for string value in int list");
        Assert.Contains(diagnostics, d => d.Message.Contains("Cannot assign"));
    }

    [Fact]
    public void TypeCheck_UndefinedCollection_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §PUSH{undefined} 1
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.True(diagnostics.HasErrors, "Expected undefined collection error");
        Assert.Contains(diagnostics, d => d.Message.Contains("Undefined collection"));
    }

    [Fact]
    public void TypeCheck_CollectionInsert_ValidIndexAndValue_NoErrors()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
              §/LIST{numbers}
              §INS{numbers} 0 99
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
              §/LIST{numbers}
              §CLR{numbers}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
              §/LIST{numbers}
              §REM{numbers} 1
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.False(diagnostics.HasErrors, $"Unexpected errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
    }

    [Fact]
    public void TypeCheck_CollectionRemove_InvalidValue_ReportsError()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
              §/LIST{numbers}
              §REM{numbers} "invalid"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var typeChecker = new TypeChecking.TypeChecker(diagnostics);
        typeChecker.Check(module);

        Assert.True(diagnostics.HasErrors, "Expected type error for string remove from int list");
        Assert.Contains(diagnostics, d => d.Message.Contains("Cannot remove"));
    }

    #endregion

    #region Integration Tests - Collections with Other Features

    [Fact]
    public void Parse_ForeachOverList_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
                3
              §/LIST{numbers}
              §EACH{e001:n} numbers
                §P n
              §/EACH{e001}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{numbers:i32}
                1
                2
                3
              §/LIST{numbers}
              §EACH{e001:n} numbers
                §P n
              §/EACH{e001}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{scores:str:i32}
                §KV "alice" 100
                §KV "bob" 85
              §/DICT{scores}
              §EACHKV{e001:name:score} scores
                §P name
                §P score
              §/EACHKV{e001}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §DICT{scores:str:i32}
                §KV "alice" 100
                §KV "bob" 85
              §/DICT{scores}
              §EACHKV{e001:name:score} scores
                §P name
              §/EACHKV{e001}
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
              §LIST{data:i32}
                1
                2
                3
              §/LIST{data}
              §PUSH{data} 4
              §INS{data} 0 0
              §SETIDX{data} 2 99
              §REM{data} 1
              §CLR{data}
            §/F{f001}
            §/M{m001}
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
    public void Parse_CollectionContainsExpression_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{bool}
              §LIST{items:i32}
                1
                2
                3
              §/LIST{items}
              §R §HAS{items} 2
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var func = module.Functions[0];
        Assert.Equal(2, func.Body.Count);
        var returnStmt = func.Body[1] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        Assert.IsType<CollectionContainsNode>(returnStmt.Expression);
    }

    [Fact]
    public void Emit_CollectionContainsExpression_GeneratesCorrectCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{bool}
              §LIST{items:i32}
                1
                2
                3
              §/LIST{items}
              §R §HAS{items} 2
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("return items.Contains(2)", csharp);
    }

    [Fact]
    public void Parse_DictionaryContainsKey_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{bool}
              §DICT{cache:str:i32}
                §KV "test" 42
              §/DICT{cache}
              §R §HAS{cache} §KEY "test"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var func = module.Functions[0];
        var returnStmt = func.Body[1] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var contains = returnStmt.Expression as CollectionContainsNode;
        Assert.NotNull(contains);
        Assert.Equal(ContainsMode.Key, contains.Mode);
    }

    [Fact]
    public void Emit_DictionaryContainsKey_GeneratesCorrectCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{bool}
              §DICT{cache:str:i32}
                §KV "test" 42
              §/DICT{cache}
              §R §HAS{cache} §KEY "test"
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitter = new CSharpEmitter();
        var csharp = emitter.Emit(module);

        Assert.Contains("return cache.ContainsKey(\"test\")", csharp);
    }

    [Fact]
    public void Parse_CollectionCount_ParsesCorrectly()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{i32}
              §LIST{items:i32}
                1
                2
                3
              §/LIST{items}
              §R §CNT items
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, $"Errors: {string.Join(", ", diagnostics.Select(d => d.Message))}");
        var func = module.Functions[0];
        var returnStmt = func.Body[1] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        Assert.IsType<CollectionCountNode>(returnStmt.Expression);
    }

    [Fact]
    public void Emit_CollectionCount_GeneratesCorrectCSharp()
    {
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{i32}
              §LIST{items:i32}
                1
                2
                3
              §/LIST{items}
              §R §CNT items
            §/F{f001}
            §/M{m001}
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
            §M{m001:Test}
            §F{f001:TestFunc:pub}
              §O{void}
              §LIST{a:i32}
              §/LIST{a}
              §PUSH{a} 1
            §/F{f001}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var effectDiagnostics = new DiagnosticBag();
        var checker = new EffectChecker(effectDiagnostics);
        checker.Check(module);

        // Should report mutation effect for PUSH operation
        Assert.Contains(effectDiagnostics, d => d.Message.Contains("Mutation"));
    }

    #endregion
}
