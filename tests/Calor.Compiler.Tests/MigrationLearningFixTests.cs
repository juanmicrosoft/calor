using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for compiler bugs identified from real-world migration learnings
/// (Newtonsoft.Json and Humanizer conversion campaigns).
/// </summary>
public class MigrationLearningFixTests
{
    private readonly CSharpToCalorConverter _converter = new();
    private static TextSpan Span => new(0, 1, 1, 1);

    #region Helpers

    private static string Emit(ModuleNode module)
    {
        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => $"{i.Severity}: {i.Message}"));
    }

    /// <summary>
    /// Emits a single BindStatementNode through CSharpEmitter to test SanitizeIdentifier.
    /// </summary>
    private static string EmitBindStatement(string variableName)
    {
        var bindNode = new BindStatementNode(
            Span, variableName, "int", false,
            new IntLiteralNode(Span, 42),
            new AttributeCollection());

        var emitter = new CSharpEmitter();
        return bindNode.Accept(emitter);
    }

    #endregion

    #region PR 1: Complete Reserved Keyword Sanitization

    [Theory]
    [InlineData("as")]
    [InlineData("is")]
    [InlineData("in")]
    [InlineData("event")]
    [InlineData("var")]
    [InlineData("default")]
    [InlineData("lock")]
    [InlineData("delegate")]
    [InlineData("checked")]
    [InlineData("yield")]
    [InlineData("out")]
    [InlineData("ref")]
    [InlineData("volatile")]
    [InlineData("abstract")]
    [InlineData("override")]
    [InlineData("sealed")]
    [InlineData("virtual")]
    [InlineData("dynamic")]
    [InlineData("async")]
    [InlineData("await")]
    [InlineData("typeof")]
    [InlineData("sizeof")]
    [InlineData("unchecked")]
    [InlineData("unsafe")]
    [InlineData("fixed")]
    [InlineData("foreach")]
    [InlineData("goto")]
    [InlineData("throw")]
    [InlineData("try")]
    [InlineData("catch")]
    [InlineData("finally")]
    [InlineData("explicit")]
    [InlineData("implicit")]
    [InlineData("extern")]
    [InlineData("operator")]
    [InlineData("params")]
    [InlineData("readonly")]
    [InlineData("stackalloc")]
    [InlineData("const")]
    public void SanitizeIdentifier_NewReservedKeywords_PrefixedWithAt(string keyword)
    {
        var result = EmitBindStatement(keyword);

        // Should produce "int @keyword = 42;"
        Assert.Contains($"@{keyword}", result);
        Assert.DoesNotContain($"int {keyword} =", result);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("interface")]
    [InlineData("enum")]
    [InlineData("namespace")]
    [InlineData("using")]
    [InlineData("public")]
    [InlineData("private")]
    [InlineData("static")]
    [InlineData("void")]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("return")]
    [InlineData("if")]
    [InlineData("else")]
    [InlineData("for")]
    [InlineData("while")]
    [InlineData("switch")]
    [InlineData("case")]
    [InlineData("break")]
    [InlineData("continue")]
    [InlineData("new")]
    public void SanitizeIdentifier_OriginalKeywords_StillPrefixed(string keyword)
    {
        var result = EmitBindStatement(keyword);
        Assert.Contains($"@{keyword}", result);
    }

    [Fact]
    public void SanitizeIdentifier_NonKeyword_NotPrefixed()
    {
        var result = EmitBindStatement("myVariable");
        Assert.Contains("myVariable", result);
        Assert.DoesNotContain("@myVariable", result);
    }

    #endregion

    #region PR 2: Call Expression Leading Dot

    [Fact]
    public void CallExpression_LeadingDot_PrependedWithThis()
    {
        var callNode = new CallExpressionNode(
            Span, ".DoSomething",
            Array.Empty<ExpressionNode>());

        var emitter = new CSharpEmitter();
        var result = callNode.Accept(emitter);

        Assert.Equal("this.DoSomething()", result);
    }

    [Fact]
    public void CallExpression_LeadingDot_WithArguments()
    {
        var callNode = new CallExpressionNode(
            Span, ".Process",
            new List<ExpressionNode> { new IntLiteralNode(Span, 42) });

        var emitter = new CSharpEmitter();
        var result = callNode.Accept(emitter);

        Assert.Equal("this.Process(42)", result);
    }

    [Fact]
    public void CallExpression_NoDot_NotPrepended()
    {
        var callNode = new CallExpressionNode(
            Span, "Console.WriteLine",
            new List<ExpressionNode> { new StringLiteralNode(Span, "hello") });

        var emitter = new CSharpEmitter();
        var result = callNode.Accept(emitter);

        Assert.Equal("Console.WriteLine(\"hello\")", result);
    }

    [Fact]
    public void CallExpression_DotInMiddle_NotPrepended()
    {
        var callNode = new CallExpressionNode(
            Span, "obj.Method",
            Array.Empty<ExpressionNode>());

        var emitter = new CSharpEmitter();
        var result = callNode.Accept(emitter);

        Assert.Equal("obj.Method()", result);
        Assert.DoesNotContain("this", result);
    }

    #endregion

    #region PR 3: Module ID Consistency

    [Fact]
    public void Convert_ModuleId_AlwaysM001()
    {
        // Even with many members, module ID should be m001
        var csharpSource = """
            namespace TestNamespace
            {
                public class ClassA
                {
                    public int FieldA;
                    public int FieldB;
                    public void Method1() { }
                    public void Method2() { }
                    public void Method3() { }
                    public void Method4() { }
                    public void Method5() { }
                    public int Prop1 { get; set; }
                    public int Prop2 { get; set; }
                    public int Prop3 { get; set; }
                }

                public class ClassB
                {
                    public void Method6() { }
                    public void Method7() { }
                    public void Method8() { }
                }

                public enum MyEnum
                {
                    Value1,
                    Value2,
                    Value3
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Equal("m001", result.Ast.Id);
    }

    [Fact]
    public void Convert_SimpleClass_ModuleIdIsM001()
    {
        var csharpSource = """
            namespace Simple
            {
                public class Foo
                {
                    public int Bar() { return 42; }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.Equal("m001", result.Ast!.Id);
    }

    #endregion

    #region PR 4: Interop Block Namespace Duplication

    [Fact]
    public void Interop_ConvertedClass_NoDoubleNamespace()
    {
        var csharpSource = """
            namespace MyLib
            {
                public class SimpleClass
                {
                    public void DoWork() { }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));
        Assert.NotNull(result.Ast);
        Assert.Equal("MyLib", result.Ast.Name);

        var emitted = Emit(result.Ast);
        var namespaceCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, namespaceCount);
    }

    [Fact]
    public void Interop_MultipleClasses_NoDoubleNamespace()
    {
        var csharpSource = """
            namespace MyProject
            {
                public class ClassOne
                {
                    public int Value { get; set; }
                }

                public class ClassTwo
                {
                    public string Name { get; set; }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitted = Emit(result.Ast!);
        var namespaceCount = CountOccurrences(emitted, "namespace MyProject");
        Assert.Equal(1, namespaceCount);
    }

    [Fact]
    public void RoundTrip_KeywordIdentifiers_SanitizedInCSharpOutput()
    {
        // Test the full round-trip: C# with @keyword identifiers → Calor → C#
        var csharpSource = """
            namespace TestKeywords
            {
                public class KeywordTest
                {
                    public int Run()
                    {
                        int @event = 10;
                        int @lock = 20;
                        int @is = 30;
                        return @event + @lock + @is;
                    }
                }
            }
            """;

        var result = _converter.Convert(csharpSource);
        Assert.True(result.Success, GetErrorMessage(result));

        var emitted = Emit(result.Ast!);
        // Keywords should be sanitized with @ prefix in emitted C#
        Assert.Contains("@event", emitted);
        Assert.Contains("@lock", emitted);
        Assert.Contains("@is", emitted);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    #endregion

    #region Fix 5: Type Names Emitted via MapTypeName Instead of SanitizeIdentifier

    [Fact]
    public void NewExpression_MapsCalorDateTimeType()
    {
        // Direct AST node: "datetime" is a Calor type that must map to "DateTime"
        var node = new NewExpressionNode(
            Span, "datetime",
            Array.Empty<string>(),
            Array.Empty<ExpressionNode>());

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("new DateTime()", result);
    }

    [Fact]
    public void NewExpression_MapsCalorTimespanType()
    {
        var node = new NewExpressionNode(
            Span, "timespan",
            Array.Empty<string>(),
            Array.Empty<ExpressionNode>());

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("new TimeSpan()", result);
    }

    [Fact]
    public void NewExpression_MapsCalorGuidType()
    {
        var node = new NewExpressionNode(
            Span, "guid",
            Array.Empty<string>(),
            Array.Empty<ExpressionNode>());

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("new Guid()", result);
    }

    [Fact]
    public void NewExpression_MapsCalorDictType()
    {
        // Dict<str, i32> → Dictionary<string, int>
        var node = new NewExpressionNode(
            Span, "Dict",
            new[] { "str", "i32" },
            Array.Empty<ExpressionNode>());

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("new Dictionary<string, int>()", result);
    }

    [Fact]
    public void CatchClause_MapsCalorExceptionType_Roundtrip()
    {
        // Roundtrip: C# → Calor → C# (verifies exception types survive)
        var csharp = """
            namespace Test
            {
                public class ErrorHandler
                {
                    public void Handle()
                    {
                        try { }
                        catch (ArgumentException ex) { }
                    }
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));
        var emitted = Emit(result.Ast!);
        Assert.Contains("catch (ArgumentException ex)", emitted);
    }

    [Fact]
    public void CatchClause_MapsCalorExceptionType_DirectAST()
    {
        // Build AST directly with a Calor type name to verify MapTypeName is called
        var catchClause = new CatchClauseNode(
            Span, "str", "ex", null,
            Array.Empty<StatementNode>(),
            new AttributeCollection());
        var tryStmt = new TryStatementNode(
            Span, "t001",
            Array.Empty<StatementNode>(),
            new[] { catchClause },
            null,
            new AttributeCollection());
        var method = new MethodNode(
            Span, "f001", "Handle", Visibility.Public, MethodModifiers.None,
            Array.Empty<TypeParameterNode>(), Array.Empty<ParameterNode>(),
            null, null,
            Array.Empty<RequiresNode>(), Array.Empty<EnsuresNode>(),
            new StatementNode[] { tryStmt },
            new AttributeCollection());
        var classNode = new ClassDefinitionNode(
            Span, "c001", "TestClass",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null, implementedInterfaces: new List<string>(),
            typeParameters: new List<TypeParameterNode>(),
            fields: new List<ClassFieldNode>(),
            properties: new List<PropertyNode>(),
            constructors: new List<ConstructorNode>(),
            methods: new List<MethodNode> { method },
            events: new List<EventDefinitionNode>(),
            operatorOverloads: new List<OperatorOverloadNode>(),
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>());
        var module = new ModuleNode(
            Span, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            new[] { classNode },
            new List<FunctionNode>(),
            new AttributeCollection());

        var emitted = Emit(module);
        // "str" is a Calor type → should be mapped to "string" in C#
        Assert.Contains("catch (string ex)", emitted);
        Assert.DoesNotContain("catch (str ex)", emitted);
    }

    [Fact]
    public void EventDefinition_MapsDelegateType_Roundtrip()
    {
        // EventHandler is not in TypeMapper, so it passes through unchanged
        var csharp = """
            namespace Test
            {
                public class Publisher
                {
                    public event EventHandler OnChanged;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));
        var emitted = Emit(result.Ast!);
        Assert.Contains("event EventHandler OnChanged", emitted);
    }

    [Fact]
    public void EventDefinition_MapsDelegateType_DirectAST()
    {
        // Build AST directly with a Calor type in the delegate type field
        var classNode = new ClassDefinitionNode(
            Span, "c001", "TestClass",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null, implementedInterfaces: new List<string>(),
            typeParameters: new List<TypeParameterNode>(),
            fields: new List<ClassFieldNode>(),
            properties: new List<PropertyNode>(),
            constructors: new List<ConstructorNode>(),
            methods: new List<MethodNode>(),
            events: new List<EventDefinitionNode>
            {
                // Use "Seq<str>" (Calor type) — should map to "IEnumerable<string>"
                new(Span, "ev1", "OnChanged", Visibility.Public, "Seq<str>", new AttributeCollection())
            },
            operatorOverloads: new List<OperatorOverloadNode>(),
            new AttributeCollection(),
            Array.Empty<CalorAttributeNode>());
        var module = new ModuleNode(
            Span, "m001", "Test",
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            new[] { classNode },
            new List<FunctionNode>(),
            new AttributeCollection());

        var emitted = Emit(module);
        Assert.Contains("event IEnumerable<string> OnChanged", emitted);
        Assert.DoesNotContain("Seq<str>", emitted);
    }

    #endregion

    #region Fix 6: Namespace Stripping in CSHARP Interop Blocks

    private static ModuleNode CreateModuleWithInteropBlock(string namespaceName, string interopCode)
    {
        var interopBlock = new CSharpInteropBlockNode(Span, interopCode);
        return new ModuleNode(
            Span, "m001", namespaceName,
            Array.Empty<UsingDirectiveNode>(),
            Array.Empty<InterfaceDefinitionNode>(),
            Array.Empty<ClassDefinitionNode>(),
            Array.Empty<EnumDefinitionNode>(),
            Array.Empty<EnumExtensionNode>(),
            Array.Empty<DelegateDefinitionNode>(),
            Array.Empty<FunctionNode>(),
            new AttributeCollection(),
            Array.Empty<IssueNode>(),
            Array.Empty<AssumeNode>(),
            Array.Empty<InvariantNode>(),
            Array.Empty<DecisionNode>(),
            context: null,
            interopBlocks: new[] { interopBlock });
    }

    [Fact]
    public void CSharpInterop_StripsFileScopedNamespace()
    {
        var module = CreateModuleWithInteropBlock("MyLib",
            "namespace MyLib;\n\npublic static class Helper\n{\n    public static int Add(int a, int b) => a + b;\n}");

        var emitted = Emit(module);
        var nsCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, nsCount);
    }

    [Fact]
    public void CSharpInterop_StripsBlockScopedNamespace()
    {
        var module = CreateModuleWithInteropBlock("MyLib",
            "namespace MyLib\n{\n    public static class Helper\n    {\n        public static int Add(int a, int b) => a + b;\n    }\n}");

        var emitted = Emit(module);
        var nsCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, nsCount);
    }

    [Fact]
    public void CSharpInterop_StripsBlockScopedNamespace_BraceOnNextLine()
    {
        // Brace on its own line, separate from the namespace declaration
        var module = CreateModuleWithInteropBlock("MyLib",
            "namespace MyLib\n\n{\n    public class Foo { }\n}");

        var emitted = Emit(module);
        var nsCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, nsCount);
        Assert.Contains("public class Foo", emitted);
    }

    [Fact]
    public void CSharpInterop_PreservesNonMatchingNamespace()
    {
        var module = CreateModuleWithInteropBlock("MyLib",
            "namespace OtherLib;\n\npublic static class Helper { }");

        var emitted = Emit(module);
        Assert.Contains("namespace MyLib", emitted);
        Assert.Contains("namespace OtherLib", emitted);
    }

    [Fact]
    public void CSharpInterop_BracesInStringLiterals_DoNotConfuseStripping()
    {
        // Braces inside a string literal should not affect namespace brace matching
        var code = "namespace MyLib\n{\n    public class Foo\n    {\n        public string Value = \"{ not a brace }\";\n    }\n}";
        var module = CreateModuleWithInteropBlock("MyLib", code);

        var emitted = Emit(module);
        var nsCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, nsCount);
        Assert.Contains("\"{ not a brace }\"", emitted);
    }

    [Fact]
    public void CSharpInterop_BracesInComments_DoNotConfuseStripping()
    {
        // Braces inside comments should not affect namespace brace matching
        var code = "namespace MyLib\n{\n    // { this is a comment with braces }\n    public class Foo { }\n}";
        var module = CreateModuleWithInteropBlock("MyLib", code);

        var emitted = Emit(module);
        var nsCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, nsCount);
        Assert.Contains("public class Foo", emitted);
    }

    [Fact]
    public void CSharpInterop_BracesInVerbatimString_DoNotConfuseStripping()
    {
        // Braces inside verbatim strings should not affect brace matching
        var code = "namespace MyLib\n{\n    public class Foo\n    {\n        public string Value = @\"{\n}\n{\";\n    }\n}";
        var module = CreateModuleWithInteropBlock("MyLib", code);

        var emitted = Emit(module);
        var nsCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, nsCount);
    }

    [Fact]
    public void CSharpInterop_TwoSpaceIndentation_DedentsCorrectly()
    {
        // Content indented with 2 spaces instead of 4
        var code = "namespace MyLib\n{\n  public class Foo\n  {\n    public int X;\n  }\n}";
        var module = CreateModuleWithInteropBlock("MyLib", code);

        var emitted = Emit(module);
        var nsCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, nsCount);
        Assert.Contains("public class Foo", emitted);
    }

    [Fact]
    public void CSharpInterop_TabIndentation_DedentsCorrectly()
    {
        // Content indented with tabs
        var code = "namespace MyLib\n{\n\tpublic class Foo\n\t{\n\t\tpublic int X;\n\t}\n}";
        var module = CreateModuleWithInteropBlock("MyLib", code);

        var emitted = Emit(module);
        var nsCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, nsCount);
        Assert.Contains("public class Foo", emitted);
    }

    [Fact]
    public void CSharpInterop_EmptyInteropBlock_NoError()
    {
        var module = CreateModuleWithInteropBlock("MyLib", "");

        var emitted = Emit(module);
        Assert.Contains("namespace MyLib", emitted);
    }

    [Fact]
    public void CSharpInterop_NamespaceWithTrailingComment_Stripped()
    {
        var code = "namespace MyLib { // main namespace\n    public class Foo { }\n}";
        var module = CreateModuleWithInteropBlock("MyLib", code);

        var emitted = Emit(module);
        var nsCount = CountOccurrences(emitted, "namespace MyLib");
        Assert.Equal(1, nsCount);
        Assert.Contains("public class Foo", emitted);
    }

    #endregion

    #region Fix 7: Primary Constructor Duplicate Fields

    [Fact]
    public void ClassPrimaryConstructor_SkipsDuplicateField()
    {
        var csharp = """
            namespace Test
            {
                public class MyClass(string name, int age)
                {
                    public string name = name;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = result.Ast!.Classes.First();
        // "name" has an explicit field, so primary constructor should NOT create a duplicate
        var nameFields = cls.Fields.Where(f => f.Name == "name").ToList();
        Assert.Single(nameFields);
        // "age" has no explicit member, so primary constructor SHOULD create it
        var ageFields = cls.Fields.Where(f => f.Name == "age").ToList();
        Assert.Single(ageFields);
    }

    [Fact]
    public void ClassPrimaryConstructor_SkipsDuplicateProperty()
    {
        var csharp = """
            namespace Test
            {
                public class MyClass(string name, int age)
                {
                    public string Name => name;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = result.Ast!.Classes.First();
        // "Name" (capital) is an explicit property — case sensitive, should NOT match "name"
        // Both "name" and "age" should be generated as fields from primary constructor
        var nameFields = cls.Fields.Where(f => f.Name == "name").ToList();
        Assert.Single(nameFields);
        var ageFields = cls.Fields.Where(f => f.Name == "age").ToList();
        Assert.Single(ageFields);
        // "Name" should exist as a property
        Assert.Contains(cls.Properties, p => p.Name == "Name");
    }

    [Fact]
    public void StructPrimaryConstructor_GeneratesFields()
    {
        var csharp = """
            namespace Test
            {
                public struct Point(int x, int y)
                {
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = result.Ast!.Classes.First();
        Assert.Equal(2, cls.Fields.Count);
        Assert.Contains(cls.Fields, f => f.Name == "x");
        Assert.Contains(cls.Fields, f => f.Name == "y");
    }

    [Fact]
    public void StructPrimaryConstructor_SkipsDuplicateField()
    {
        var csharp = """
            namespace Test
            {
                public struct Point(int x, int y)
                {
                    public int x = x;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = result.Ast!.Classes.First();
        // "x" has explicit field — only one should exist
        var xFields = cls.Fields.Where(f => f.Name == "x").ToList();
        Assert.Single(xFields);
        // "y" has no explicit member — should be generated
        var yFields = cls.Fields.Where(f => f.Name == "y").ToList();
        Assert.Single(yFields);
    }

    [Fact]
    public void StructPrimaryConstructor_SkipsDuplicateProperty()
    {
        var csharp = """
            namespace Test
            {
                public struct Point(int x, int y)
                {
                    public int x { get; } = x;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = result.Ast!.Classes.First();
        // "x" has explicit property — primary constructor should skip generating a field for it
        var xFields = cls.Fields.Where(f => f.Name == "x").ToList();
        Assert.Empty(xFields);
        Assert.Contains(cls.Properties, p => p.Name == "x");
        // "y" has no explicit member — should be generated as field
        var yFields = cls.Fields.Where(f => f.Name == "y").ToList();
        Assert.Single(yFields);
    }

    [Fact]
    public void RecordPrimaryConstructor_SkipsDuplicateProperty()
    {
        var csharp = """
            namespace Test
            {
                public record Person(string Name, int Age)
                {
                    public string Name { get; init; } = Name;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = result.Ast!.Classes.First();
        // "Name" has an explicit property, so primary constructor should NOT create a duplicate
        var nameProps = cls.Properties.Where(p => p.Name == "Name").ToList();
        Assert.Single(nameProps);
        // "Age" has no explicit member, so primary constructor SHOULD create it
        var ageProps = cls.Properties.Where(p => p.Name == "Age").ToList();
        Assert.Single(ageProps);
    }

    [Fact]
    public void RecordPrimaryConstructor_AllParamsDuplicated_NoExtraProperties()
    {
        var csharp = """
            namespace Test
            {
                public record Config(string Key, string Value)
                {
                    public string Key { get; init; } = Key;
                    public string Value { get; init; } = Value;
                }
            }
            """;

        var result = _converter.Convert(csharp);
        Assert.True(result.Success, GetErrorMessage(result));

        var cls = result.Ast!.Classes.First();
        // Both params have explicit properties — no extra properties should be generated
        Assert.Equal(2, cls.Properties.Count);
        Assert.Single(cls.Properties.Where(p => p.Name == "Key"));
        Assert.Single(cls.Properties.Where(p => p.Name == "Value"));
    }

    #endregion

    #region Fix 8: UTF-8 String Literal Emission

    [Fact]
    public void CSharpEmitter_Utf8StringLiteral_EmitsU8Suffix()
    {
        var node = new StringLiteralNode(Span, "hello") { IsUtf8 = true };
        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("\"hello\"u8", result);
    }

    [Fact]
    public void CSharpEmitter_RegularStringLiteral_NoU8Suffix()
    {
        var node = new StringLiteralNode(Span, "hello");
        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);

        Assert.Equal("\"hello\"", result);
    }

    #endregion

    #region Fix 9: List Pattern Slice Position

    [Fact]
    public void CSharpEmitter_ListPattern_SliceAtEnd()
    {
        // [var a, var b, ..var rest]
        var node = new ListPatternNode(
            Span,
            new PatternNode[] { new VarPatternNode(Span, "a"), new VarPatternNode(Span, "b") },
            new VarPatternNode(Span, "rest"),
            sliceIndex: 2); // after both patterns = end

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);
        Assert.Equal("[var a, var b, ..var rest]", result);
    }

    [Fact]
    public void CSharpEmitter_ListPattern_SliceAtStart()
    {
        // [..var rest, var a, var b]
        var node = new ListPatternNode(
            Span,
            new PatternNode[] { new VarPatternNode(Span, "a"), new VarPatternNode(Span, "b") },
            new VarPatternNode(Span, "rest"),
            sliceIndex: 0); // before all patterns = start

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);
        Assert.Equal("[..var rest, var a, var b]", result);
    }

    [Fact]
    public void CSharpEmitter_ListPattern_SliceInMiddle()
    {
        // [var first, .., var last] — discard slice emits bare ..
        var node = new ListPatternNode(
            Span,
            new PatternNode[] { new VarPatternNode(Span, "first"), new VarPatternNode(Span, "last") },
            new VarPatternNode(Span, "_"),
            sliceIndex: 1); // between first and last

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);
        Assert.Equal("[var first, .., var last]", result);
    }

    [Fact]
    public void CSharpEmitter_ListPattern_NoSlice()
    {
        // [var a, var b, var c]
        var node = new ListPatternNode(
            Span,
            new PatternNode[] { new VarPatternNode(Span, "a"), new VarPatternNode(Span, "b"), new VarPatternNode(Span, "c") },
            null);

        var emitter = new CSharpEmitter();
        var result = node.Accept(emitter);
        Assert.Equal("[var a, var b, var c]", result);
    }

    [Fact]
    public void RoundTrip_ListPattern_SlicePositionPreserved()
    {
        // C# with middle slice: [var first, .., var last]
        var csharp = @"public class Test { public bool M(int[] arr) => arr is [var first, .., var last]; }";
        var result = _converter.Convert(csharp, "test");
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));

        // Re-emit to C# and verify slice is in the middle
        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);
        Assert.Contains("[var first, .., var last]", output);
    }

    [Fact]
    public void RoundTrip_ListPattern_SliceAtStartPreserved()
    {
        var csharp = @"public class Test { public bool M(int[] arr) => arr is [.. var rest, var a, var b]; }";
        var result = _converter.Convert(csharp, "test");
        Assert.True(result.Success, string.Join("\n", result.Issues.Select(i => i.Message)));

        var emitter = new CSharpEmitter();
        var output = emitter.Emit(result.Ast!);
        Assert.Contains("[..var rest, var a, var b]", output);
    }

    #endregion
}
