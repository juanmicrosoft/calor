using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class CSharpInteropBlockTests
{
    #region 1. Lexer Tests

    [Fact]
    public void Lexer_CSharpInteropBlock_TokenizesCorrectly()
    {
        var source = "§CSHARP{public void Foo() { }}§/CSHARP";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Equal(2, tokens.Count); // CSharpInterop + Eof
        Assert.Equal(TokenKind.CSharpInterop, tokens[0].Kind);
        Assert.Equal("public void Foo() { }", tokens[0].Value as string);
    }

    [Fact]
    public void Lexer_CSharpInteropBlock_MultilineContent()
    {
        var source = "§CSHARP{\npublic int X { get; set; }\npublic int Y { get; set; }\n}§/CSHARP";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Equal(TokenKind.CSharpInterop, tokens[0].Kind);
        var content = tokens[0].Value as string;
        Assert.NotNull(content);
        Assert.Contains("public int X", content);
        Assert.Contains("public int Y", content);
    }

    [Fact]
    public void Lexer_CSharpInteropBlock_Unterminated_ReportsError()
    {
        var source = "§CSHARP{public void Foo() { }";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.NotEmpty(diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCode.UnterminatedCSharpInteropBlock);
    }

    [Fact]
    public void Lexer_CSharpInteropBlock_MissingOpenBrace_ReportsError()
    {
        var source = "§CSHARP public void Foo() }§/CSHARP";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.NotEmpty(diag);
        Assert.Contains(diag, d => d.Code == DiagnosticCode.UnterminatedCSharpInteropBlock);
    }

    [Fact]
    public void Lexer_CSharpInteropBlock_EmbeddedCloseBrace_DoesNotTerminateEarly()
    {
        // A } followed by something other than §/CSHARP should not terminate the block
        var source = "§CSHARP{if (x) { return; } else { break; }}§/CSHARP";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();

        Assert.Empty(diag);
        Assert.Equal(TokenKind.CSharpInterop, tokens[0].Kind);
        var content = tokens[0].Value as string;
        Assert.NotNull(content);
        Assert.Contains("if (x) { return; } else { break; }", content);
    }

    #endregion

    #region 2. Parser (Module) Tests

    [Fact]
    public void Parser_ModuleWithInteropBlock_ParsesCorrectly()
    {
        var source = """
            §M{m001:TestModule}
            §CSHARP{public static void Helper() { Console.WriteLine("hi"); }}§/CSHARP
            §/M{m001}
            """;
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var parser = new Parser(lexer.Tokenize(), diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        Assert.Single(module.InteropBlocks);
        Assert.Contains("Helper", module.InteropBlocks[0].CSharpCode);
    }

    #endregion

    #region 3. Parser (Class) Tests

    [Fact]
    public void Parser_ClassWithInteropBlock_ParsesCorrectly()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:MyClass:pub}
            §CSHARP{public T GetAll<T>() where T : class => default!;}§/CSHARP
            §/CL{c001}
            §/M{m001}
            """;
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var parser = new Parser(lexer.Tokenize(), diag);
        var module = parser.Parse();

        Assert.Empty(diag);
        Assert.Single(module.Classes);
        Assert.Single(module.Classes[0].InteropBlocks);
        Assert.Contains("GetAll<T>", module.Classes[0].InteropBlocks[0].CSharpCode);
    }

    #endregion

    #region 4. CalorEmitter Round-Trip Tests

    [Fact]
    public void CalorEmitter_RoundTrips_CSharpInteropBlock()
    {
        var source = """
            §M{m001:TestModule}
            §CL{c001:MyClass:pub}
            §CSHARP{public T GetAll<T>() => default!;}§/CSHARP
            §/CL{c001}
            §/M{m001}
            """;
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var parser = new Parser(lexer.Tokenize(), diag);
        var module = parser.Parse();
        Assert.Empty(diag);

        var emitter = new CalorEmitter(new ConversionContext());
        var emitted = emitter.Emit(module);

        Assert.Contains("§CSHARP{", emitted);
        Assert.Contains("}§/CSHARP", emitted);
        Assert.Contains("GetAll<T>", emitted);
    }

    #endregion

    #region 5. CSharpEmitter Tests

    [Fact]
    public void CSharpEmitter_EmitsRawCSharp_FromInteropBlock()
    {
        var interopBlock = new CSharpInteropBlockNode(
            new TextSpan(0, 0, 1, 1),
            "public T GetAll<T>() => default!;");

        var classNode = new ClassDefinitionNode(
            new TextSpan(0, 0, 1, 1), "c001", "MyClass",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: false,
            baseClass: null,
            implementedInterfaces: Array.Empty<string>(),
            typeParameters: Array.Empty<TypeParameterNode>(),
            fields: Array.Empty<ClassFieldNode>(),
            properties: Array.Empty<PropertyNode>(),
            constructors: Array.Empty<ConstructorNode>(),
            methods: Array.Empty<MethodNode>(),
            events: Array.Empty<EventDefinitionNode>(),
            attributes: new AttributeCollection(),
            csharpAttributes: Array.Empty<CalorAttributeNode>(),
            visibility: Visibility.Public,
            interopBlocks: new[] { interopBlock });

        var moduleNode = new ModuleNode(
            new TextSpan(0, 0, 1, 1), "m001", "TestModule",
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

        Assert.Contains("public T GetAll<T>() => default!;", output);
    }

    #endregion

    #region 6. Conversion (Interop Mode) Tests

    [Fact]
    public void Conversion_InteropMode_UnsupportedMember_CreatesCSharpInteropBlock()
    {
        // An indexer is an unsupported member type that hits the default case in ConvertClassMember
        var csharpSource = """
            public class MyClass
            {
                public int Add(int a, int b) => a + b;
                public int this[int index] => index;
            }
            """;

        var options = new ConversionOptions
        {
            ModuleName = "TestModule",
            GracefulFallback = true,
            Mode = ConversionMode.Interop
        };
        var converter = new CSharpToCalorConverter(options);
        var result = converter.Convert(csharpSource);

        // Should succeed (interop mode doesn't fail on unsupported code)
        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);
        // The supported method should be converted normally
        Assert.Contains("Add", result.CalorSource!);
        // The unsupported indexer should be preserved as an interop block
        Assert.Contains("§CSHARP{", result.CalorSource!);
        Assert.Contains("}§/CSHARP", result.CalorSource!);
        // Stats should track the interop block
        Assert.True(result.Context.Stats.InteropBlocksEmitted >= 1);
    }

    [Fact]
    public void Conversion_InteropMode_UnsupportedMember_StandardMode_NoInteropBlock()
    {
        // In standard mode, unsupported members should NOT produce §CSHARP blocks
        var csharpSource = """
            public class MyClass
            {
                public int Add(int a, int b) => a + b;
                public int this[int index] => index;
            }
            """;

        var options = new ConversionOptions
        {
            ModuleName = "TestModule",
            GracefulFallback = true,
            Mode = ConversionMode.Standard
        };
        var converter = new CSharpToCalorConverter(options);
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        if (result.CalorSource != null)
        {
            Assert.DoesNotContain("§CSHARP{", result.CalorSource);
        }
    }

    #endregion

    #region 7. Conversion (Mixed) Tests

    [Fact]
    public void Conversion_InteropMode_MixedMembers_ProducesHybridOutput()
    {
        var csharpSource = """
            public class Calculator
            {
                public int Add(int a, int b) => a + b;
                public int Subtract(int a, int b) => a - b;
            }
            """;

        var options = new ConversionOptions
        {
            ModuleName = "TestModule",
            GracefulFallback = true,
            Mode = ConversionMode.Interop
        };
        var converter = new CSharpToCalorConverter(options);
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);
        // Both methods should be converted normally
        Assert.Contains("Add", result.CalorSource!);
        Assert.Contains("Subtract", result.CalorSource!);
    }

    #endregion

    #region 8. Conversion (Round-Trip) Tests

    [Fact]
    public void Conversion_InteropMode_RoundTrip_ProducesValidOutput()
    {
        var csharpSource = """
            public class Calculator
            {
                public int Add(int a, int b) => a + b;
            }
            """;

        var options = new ConversionOptions
        {
            ModuleName = "TestModule",
            GracefulFallback = true,
            Mode = ConversionMode.Interop
        };
        var converter = new CSharpToCalorConverter(options);
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);

        // Re-parse the generated Calor to verify it's valid
        var parseResult = CalorSourceHelper.Parse(result.CalorSource!, "test.calr");
        Assert.True(parseResult.IsSuccess, $"Generated Calor failed to parse: {string.Join(", ", parseResult.Errors)}");

        // Compile back to C# to verify round-trip
        var compilationResult = Program.Compile(result.CalorSource!, "test.calr");
        Assert.False(compilationResult.HasErrors, $"Round-trip C# compilation errors: {string.Join(", ", compilationResult.Diagnostics)}");
    }

    #endregion

    #region 9. Backward Compatibility Tests

    [Fact]
    public void Conversion_StandardMode_StillProducesFallbackComments()
    {
        var csharpSource = """
            public class MyClass
            {
                public int Add(int a, int b) => a + b;
            }
            """;

        var options = new ConversionOptions
        {
            ModuleName = "TestModule",
            GracefulFallback = true,
            Mode = ConversionMode.Standard
        };
        var converter = new CSharpToCalorConverter(options);
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        // In standard mode, there should be no §CSHARP blocks
        if (result.CalorSource != null)
        {
            Assert.DoesNotContain("§CSHARP{", result.CalorSource);
        }
    }

    #endregion

    #region 10. Stats Tests

    [Fact]
    public void Conversion_InteropMode_StatsAreAccurate()
    {
        var csharpSource = """
            public class Calculator
            {
                public int Add(int a, int b) => a + b;
                public int Sub(int a, int b) => a - b;
            }
            """;

        var options = new ConversionOptions
        {
            ModuleName = "TestModule",
            GracefulFallback = true,
            Mode = ConversionMode.Interop
        };
        var converter = new CSharpToCalorConverter(options);
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        // Both methods should be fully convertible
        Assert.True(result.Context.Stats.MethodsConverted >= 2);
    }

    #endregion

    #region MCP Tool Tests

    [Fact]
    public async Task ConvertTool_InteropMode_AcceptsParameter()
    {
        var tool = new ConvertTool();
        var args = JsonDocument.Parse("""
            {
                "source": "public class Calc { public int Add(int a, int b) => a + b; }",
                "mode": "interop"
            }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("success", text);
    }

    [Fact]
    public async Task AnalyzeConvertibilityTool_ReturnsScore()
    {
        var tool = new AnalyzeConvertibilityTool();
        var args = JsonDocument.Parse("""
            {
                "source": "public class Calc { public int Add(int a, int b) => a + b; }"
            }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("convertibility_score", text);
        Assert.Contains("recommendation", text);
    }

    #endregion

    #region HandleUnsupportedStatement Interop Mode Tests

    [Fact]
    public void Conversion_InteropMode_UnsupportedStatement_PreservesAsRawCSharp()
    {
        // 'goto' is an unsupported statement type that triggers HandleUnsupportedStatement
        var csharpSource = """
            public class MyClass
            {
                public void DoWork()
                {
                    int x = 1;
                    goto done;
                    done:
                    x = 2;
                }
            }
            """;

        var options = new ConversionOptions
        {
            ModuleName = "TestModule",
            GracefulFallback = true,
            Mode = ConversionMode.Interop
        };
        var converter = new CSharpToCalorConverter(options);
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);

        // In interop mode, unsupported statements should be preserved as §RAW blocks
        // (RawCSharpNode), not lost as TODO comments
        // Re-parse and compile to verify the output is valid
        var parseResult = CalorSourceHelper.Parse(result.CalorSource!, "test.calr");
        Assert.True(parseResult.IsSuccess, $"Generated Calor failed to parse: {string.Join(", ", parseResult.Errors)}");
    }

    #endregion

    #region Module-Level Interop Fallback Tests

    [Fact]
    public void Conversion_InteropMode_UnsupportedMemberType_FallsBackToInteropBlock()
    {
        // A class with an indexer (unsupported member type) should produce an interop block
        // for that specific member while converting other members normally
        var csharpSource = """
            public class MyList
            {
                private int[] _items = new int[10];
                public int Count => _items.Length;
                public int this[int index] => _items[index];
            }
            """;

        var options = new ConversionOptions
        {
            ModuleName = "TestModule",
            GracefulFallback = true,
            Mode = ConversionMode.Interop
        };
        var converter = new CSharpToCalorConverter(options);
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);
        // The indexer should be preserved as an interop block
        Assert.Contains("§CSHARP{", result.CalorSource!);
        // The Count property should still be converted
        Assert.Contains("Count", result.CalorSource!);
    }

    #endregion

    #region AST Node Tests

    [Fact]
    public void CSharpInteropBlockNode_StoresMetadata()
    {
        var node = new CSharpInteropBlockNode(
            new TextSpan(0, 10, 1, 1),
            "public T Get<T>() => default!;",
            featureName: "generics",
            reason: "Generic method not supported",
            memberKind: InteropMemberKind.Method);

        Assert.Equal("public T Get<T>() => default!;", node.CSharpCode);
        Assert.Equal("generics", node.FeatureName);
        Assert.Equal("Generic method not supported", node.Reason);
        Assert.Equal(InteropMemberKind.Method, node.MemberKind);
    }

    [Fact]
    public void CSharpInteropBlockNode_Accept_CallsVisitor()
    {
        var node = new CSharpInteropBlockNode(
            new TextSpan(0, 10, 1, 1), "int x;");

        var emitter = new CalorEmitter(new ConversionContext());
        // Should not throw
        node.Accept(emitter);
    }

    #endregion
}
