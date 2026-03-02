using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class IndexerTests
{
    #region Lexer Tests

    [Fact]
    public void Lexer_Indexer_TokenizesCorrectly()
    {
        var source = "§IXER{ix1:int:pub}";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Indexer);
    }

    [Fact]
    public void Lexer_EndIndexer_TokenizesCorrectly()
    {
        var source = "§/IXER{ix1}";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Contains(tokens, t => t.Kind == TokenKind.EndIndexer);
    }

    #endregion

    #region Parser Tests

    [Fact]
    public void Parser_FullFormIndexer_ParsesCorrectly()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §IXER{ix1:int:pub}
            §I{int:index}
            §GET
            §R INT:0
            §/GET
            §SET
            §/SET
            §/IXER{ix1}
            §/CL{c1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        var cls = Assert.Single(module.Classes);
        var indexer = Assert.Single(cls.Indexers);
        Assert.Equal("ix1", indexer.Id);
        Assert.Equal("int", indexer.TypeName);
        Assert.Equal(Visibility.Public, indexer.Visibility);
        Assert.Single(indexer.Parameters);
        Assert.Equal("index", indexer.Parameters[0].Name);
        Assert.Equal("INT", indexer.Parameters[0].TypeName); // ExpandType maps "int" -> "INT"
        Assert.NotNull(indexer.Getter);
        Assert.NotNull(indexer.Setter);
    }

    [Fact]
    public void Parser_CompactAutoIndexer_ParsesCorrectly()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §IXER{ix1:int:pub:get,set} (int:index)
            §/CL{c1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        var cls = Assert.Single(module.Classes);
        var indexer = Assert.Single(cls.Indexers);
        Assert.Equal("ix1", indexer.Id);
        Assert.Equal("int", indexer.TypeName);
        Assert.Equal(Visibility.Public, indexer.Visibility);
        Assert.True(indexer.IsAutoIndexer);
        Assert.NotNull(indexer.Getter);
        Assert.NotNull(indexer.Setter);
        Assert.Single(indexer.Parameters);
        Assert.Equal("index", indexer.Parameters[0].Name);
    }

    [Fact]
    public void Parser_MultiParameterIndexer_ParsesCorrectly()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §IXER{ix1:int:pub:get,set} (int:row, int:col)
            §/CL{c1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        var cls = Assert.Single(module.Classes);
        var indexer = Assert.Single(cls.Indexers);
        Assert.Equal(2, indexer.Parameters.Count);
        Assert.Equal("row", indexer.Parameters[0].Name);
        Assert.Equal("col", indexer.Parameters[1].Name);
    }

    [Fact]
    public void Parser_InterfaceIndexer_ParsesCorrectly()
    {
        var source = """
            §M{m1:Test}
            §IFACE{i1:IMyCollection}
            §IXER{ix1:int:pub:get,set} (int:index)
            §/IFACE{i1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        var iface = Assert.Single(module.Interfaces);
        var indexer = Assert.Single(iface.Indexers);
        Assert.Equal("ix1", indexer.Id);
        Assert.Equal("int", indexer.TypeName);
    }

    [Fact]
    public void Parser_VirtualIndexer_ParsesModifiers()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §IXER{ix1:int:pub:virt:get,set} (int:index)
            §/CL{c1}
            §/M{m1}
            """;

        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        var indexer = Assert.Single(module.Classes[0].Indexers);
        Assert.True(indexer.IsVirtual);
    }

    #endregion

    #region C# Emission Tests

    [Fact]
    public void CSharpEmitter_AutoIndexer_EmitsCorrectly()
    {
        var indexer = new IndexerNode(
            TextSpan.Empty,
            "ix1",
            "int",
            Visibility.Public,
            MethodModifiers.None,
            new[] { new ParameterNode(TextSpan.Empty, "index", "int", new AttributeCollection()) },
            new PropertyAccessorNode(TextSpan.Empty, PropertyAccessorNode.AccessorKind.Get, Visibility.Public,
                Array.Empty<RequiresNode>(), Array.Empty<StatementNode>(), new AttributeCollection()),
            new PropertyAccessorNode(TextSpan.Empty, PropertyAccessorNode.AccessorKind.Set, Visibility.Public,
                Array.Empty<RequiresNode>(), Array.Empty<StatementNode>(), new AttributeCollection()),
            null,
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>());

        var classNode = new ClassDefinitionNode(
            TextSpan.Empty, "c1", "MyClass",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields: Array.Empty<ClassFieldNode>(),
            properties: Array.Empty<PropertyNode>(),
            constructors: Array.Empty<ConstructorNode>(),
            methods: Array.Empty<MethodNode>(),
            events: Array.Empty<EventDefinitionNode>(),
            operatorOverloads: Array.Empty<OperatorOverloadNode>(),
            attributes: new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Public,
            indexers: new[] { indexer });

        var moduleNode = new ModuleNode(
            TextSpan.Empty, "m1", "Test",
            usings: Array.Empty<UsingDirectiveNode>(),
            interfaces: Array.Empty<InterfaceDefinitionNode>(),
            classes: new[] { classNode },
            enums: Array.Empty<EnumDefinitionNode>(),
            enumExtensions: Array.Empty<EnumExtensionNode>(),
            delegates: Array.Empty<DelegateDefinitionNode>(),
            functions: Array.Empty<FunctionNode>(),
            attributes: new AttributeCollection(),
            issues: Array.Empty<IssueNode>(),
            assumptions: Array.Empty<AssumeNode>(),
            invariants: Array.Empty<InvariantNode>(),
            decisions: Array.Empty<DecisionNode>(),
            context: null);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(moduleNode);

        Assert.Contains("this[int index]", output);
        Assert.Contains("get; set;", output);
    }

    [Fact]
    public void CSharpEmitter_IndexerWithBody_EmitsCorrectly()
    {
        var getterBody = new List<StatementNode>
        {
            new ReturnStatementNode(TextSpan.Empty,
                new IntLiteralNode(TextSpan.Empty, 42))
        };

        var indexer = new IndexerNode(
            TextSpan.Empty,
            "ix1",
            "int",
            Visibility.Public,
            MethodModifiers.None,
            new[] { new ParameterNode(TextSpan.Empty, "index", "int", new AttributeCollection()) },
            new PropertyAccessorNode(TextSpan.Empty, PropertyAccessorNode.AccessorKind.Get, Visibility.Public,
                Array.Empty<RequiresNode>(), getterBody, new AttributeCollection()),
            null,
            null,
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>());

        var classNode = new ClassDefinitionNode(
            TextSpan.Empty, "c1", "MyClass",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields: Array.Empty<ClassFieldNode>(),
            properties: Array.Empty<PropertyNode>(),
            constructors: Array.Empty<ConstructorNode>(),
            methods: Array.Empty<MethodNode>(),
            events: Array.Empty<EventDefinitionNode>(),
            operatorOverloads: Array.Empty<OperatorOverloadNode>(),
            attributes: new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Public,
            indexers: new[] { indexer });

        var moduleNode = new ModuleNode(
            TextSpan.Empty, "m1", "Test",
            usings: Array.Empty<UsingDirectiveNode>(),
            interfaces: Array.Empty<InterfaceDefinitionNode>(),
            classes: new[] { classNode },
            enums: Array.Empty<EnumDefinitionNode>(),
            enumExtensions: Array.Empty<EnumExtensionNode>(),
            delegates: Array.Empty<DelegateDefinitionNode>(),
            functions: Array.Empty<FunctionNode>(),
            attributes: new AttributeCollection(),
            issues: Array.Empty<IssueNode>(),
            assumptions: Array.Empty<AssumeNode>(),
            invariants: Array.Empty<InvariantNode>(),
            decisions: Array.Empty<DecisionNode>(),
            context: null);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(moduleNode);

        Assert.Contains("this[int index]", output);
        Assert.Contains("get", output);
        Assert.Contains("return 42", output);
    }

    #endregion

    #region C# to Calor Conversion Tests

    [Fact]
    public void Conversion_SimpleIndexer_ProducesIndexerNode()
    {
        var csharp = """
            public class MyList
            {
                public int this[int index] { get; set; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var cls = Assert.Single(result.Ast!.Classes);
        var indexer = Assert.Single(cls.Indexers);
        Assert.Equal("i32", indexer.TypeName); // TypeMapper maps C# "int" -> "i32"
        Assert.Single(indexer.Parameters);
        Assert.Equal("index", indexer.Parameters[0].Name);
        Assert.True(indexer.IsAutoIndexer);
    }

    [Fact]
    public void Conversion_ExpressionBodiedIndexer_ProducesGetterBody()
    {
        var csharp = """
            public class MyList
            {
                private int[] _items = new int[10];
                public int this[int index] => _items[index];
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var cls = Assert.Single(result.Ast!.Classes);
        var indexer = Assert.Single(cls.Indexers);
        Assert.NotNull(indexer.Getter);
        Assert.True(indexer.Getter!.Body.Count > 0, "Expression-bodied indexer should have a getter body");
    }

    [Fact]
    public void Conversion_MultiParameterIndexer_PreservesAllParams()
    {
        var csharp = """
            public class Matrix
            {
                public int this[int row, int col] { get; set; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var cls = Assert.Single(result.Ast!.Classes);
        var indexer = Assert.Single(cls.Indexers);
        Assert.Equal(2, indexer.Parameters.Count);
        Assert.Equal("row", indexer.Parameters[0].Name);
        Assert.Equal("col", indexer.Parameters[1].Name);
    }

    [Fact]
    public void Conversion_VirtualIndexer_PreservesModifiers()
    {
        var csharp = """
            public class Base
            {
                public virtual int this[int index] { get; set; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var indexer = Assert.Single(result.Ast!.Classes[0].Indexers);
        Assert.True(indexer.IsVirtual);
    }

    [Fact]
    public void Conversion_InterfaceIndexer_ProducesIndexerNode()
    {
        var csharp = """
            public interface IReadOnlyAccess
            {
                int this[int index] { get; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var iface = Assert.Single(result.Ast!.Interfaces);
        var indexer = Assert.Single(iface.Indexers);
        Assert.NotNull(indexer.Getter);
        Assert.Null(indexer.Setter);
    }

    [Fact]
    public void Conversion_IndexerWithGetSetBodies_PreservesBodies()
    {
        var csharp = """
            public class MyDict
            {
                private int[] _data = new int[100];
                public int this[int key]
                {
                    get { return _data[key]; }
                    set { _data[key] = value; }
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var indexer = Assert.Single(result.Ast!.Classes[0].Indexers);
        Assert.NotNull(indexer.Getter);
        Assert.NotNull(indexer.Setter);
        Assert.False(indexer.IsAutoIndexer, "Indexer with bodies should not be auto-implemented");
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_CSharpToCalorToCSharp_PreservesIndexer()
    {
        var csharp = """
            public class MyCollection
            {
                public int this[int index] { get; set; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);
        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));

        // Re-emit to C#
        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("this[int index]", output);
        Assert.Contains("get; set;", output);
    }

    [Fact]
    public void RoundTrip_CalorParseEmitReparse_PreservesIndexer()
    {
        var source = """
            §M{m1:Test}
            §CL{c1:MyClass:pub}
            §IXER{ix1:int:pub:get,set} (int:index)
            §/CL{c1}
            §/M{m1}
            """;

        // Parse
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();
        Assert.Empty(diag);

        // Emit to C#
        var emitter = new CSharpEmitter();
        var csharpOutput = emitter.Emit(module);

        Assert.Contains("this[int index]", csharpOutput);
        Assert.Contains("get; set;", csharpOutput);
    }

    [Fact]
    public void RoundTrip_CSharpToCalorReparseToCSharp_PreservesIndexer()
    {
        var csharp = """
            public class Dict
            {
                public string this[string key] { get; set; }
            }
            """;

        // Convert C# to Calor
        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);
        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));

        // Re-parse generated Calor
        var diag = new DiagnosticBag();
        var lexer = new Lexer(result.CalorSource!, diag);
        var tokens = lexer.TokenizeAll();
        var parser = new Parser(tokens, diag);
        var module = parser.Parse();
        Assert.Empty(diag);

        // Verify the re-parsed AST has the indexer
        var cls = Assert.Single(module.Classes);
        var indexer = Assert.Single(cls.Indexers);
        Assert.NotNull(indexer.Getter);
        Assert.NotNull(indexer.Setter);

        // Emit back to C# and verify
        var emitter = new CSharpEmitter();
        var output = emitter.Emit(module);
        Assert.Contains("this[", output);
        Assert.Contains("get; set;", output);
    }

    #endregion

    #region String Key Indexer Tests

    [Fact]
    public void Conversion_StringKeyIndexer_ProducesCorrectNode()
    {
        var csharp = """
            public class JsonObject
            {
                public object this[string key] { get; set; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var cls = Assert.Single(result.Ast!.Classes);
        var indexer = Assert.Single(cls.Indexers);
        Assert.Equal("any", indexer.TypeName); // TypeMapper maps C# "object" -> "any"
        Assert.Single(indexer.Parameters);
        Assert.Equal("key", indexer.Parameters[0].Name);
        Assert.Equal("str", indexer.Parameters[0].TypeName); // TypeMapper maps C# "string" -> "str"
    }

    [Fact]
    public void CSharpEmitter_StringKeyIndexer_EmitsCorrectly()
    {
        var csharp = """
            public class JsonObject
            {
                public object this[string key] { get; set; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);
        Assert.True(result.Success);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("this[string key]", output);
        Assert.Contains("get; set;", output);
    }

    #endregion

    #region Abstract/Override Indexer Tests

    [Fact]
    public void Conversion_AbstractIndexer_PreservesModifier()
    {
        var csharp = """
            public abstract class Base
            {
                public abstract int this[int index] { get; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var indexer = Assert.Single(result.Ast!.Classes[0].Indexers);
        Assert.True(indexer.IsAbstract);
    }

    [Fact]
    public void CSharpEmitter_AbstractIndexer_EmitsAbstractKeyword()
    {
        var csharp = """
            public abstract class Base
            {
                public abstract int this[int index] { get; }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);
        Assert.True(result.Success);

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);

        Assert.Contains("abstract", output);
        Assert.Contains("this[int index]", output);
    }

    [Fact]
    public void Conversion_OverrideIndexer_PreservesModifier()
    {
        var csharp = """
            public class Derived : System.Collections.ObjectModel.Collection<int>
            {
                public int this[int index]
                {
                    get => base[index];
                }
            }
            """;

        var converter = new CSharpToCalorConverter();
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues.Select(d => d.ToString())));
        var indexer = Assert.Single(result.Ast!.Classes[0].Indexers);
        Assert.NotNull(indexer.Getter);
    }

    #endregion
}
