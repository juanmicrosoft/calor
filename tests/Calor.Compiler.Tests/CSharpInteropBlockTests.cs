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
    [Theory]
    [InlineData("")]
    [InlineData("public delegate void D();")]
    [InlineData("  x\r\n\t y  \r\n")]
    [InlineData("\u03c0\U0001f600")]
    [InlineData("#if NEVER\r\nunmatched \"\"\" { }§/CSHARP §/RAW\r\n#endif\r\n// tail")]
    [InlineData("const string s = \"\"\"\n    a\n      b\n    \"\"\";")]
    public void RawFraming_PreservesExactBytesAndFollowingLayout(string payload)
    {
        var length = System.Text.Encoding.UTF8.GetByteCount(payload);
        var source = $"§M{{RawTest}}\n  raw delegate {length}\n{payload}\n  §CL{{Next:pub}}\n    raw member 0\n\n";
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var raw = Assert.Single(module.InteropBlocks);
        Assert.Equal("delegate", raw.FeatureName);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(payload),
            System.Text.Encoding.UTF8.GetBytes(raw.CSharpCode));
        Assert.Equal("Next", Assert.Single(module.Classes).Name);
        Assert.Equal("", Assert.Single(module.Classes[0].InteropBlocks).CSharpCode);
        var next = Assert.Single(tokens.Where(t => t.Kind == TokenKind.Class));
        Assert.Equal(source.IndexOf("§CL", StringComparison.Ordinal), next.Span.Start);
        Assert.Equal(source[..next.Span.Start].Count(c => c == '\n') + 1, next.Span.Line);
        Assert.Equal(3, next.Span.Column);
        var emitted = new CalorEmitter().Emit(module);
        var reparsed = ParseRawModule(emitted);
        Assert.Equal(payload, Assert.Single(reparsed.InteropBlocks).CSharpCode);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void RawFraming_PayloadFinalNewlineCanBeTheLayoutBoundary(string newline)
    {
        var payload = "  // \u03c0" + newline;
        var source = $"§M{{RawTest}}{newline}  raw member {System.Text.Encoding.UTF8.GetByteCount(payload)}{newline}" +
            payload + "  §CL{Next:pub}" + newline;
        var module = ParseRawModule(source);
        Assert.Equal(payload, Assert.Single(module.InteropBlocks).CSharpCode);
        Assert.Equal("Next", Assert.Single(module.Classes).Name);
    }

    [Theory]
    [InlineData("raw", "reason")]
    [InlineData("raw delegate\n", "Missing")]
    [InlineData("raw delegate -1\n", "Invalid")]
    [InlineData("raw delegate 1x\nx", "Invalid")]
    [InlineData("raw delegate 2147483648\n", "overflows")]
    [InlineData("raw delegate 99999999999999999999999999\n", "overflows")]
    [InlineData("raw delegate 3", "newline")]
    [InlineData("raw delegate 4\nabc", "Truncated")]
    [InlineData("raw delegate 1\n\u03c0", "code point")]
    [InlineData("raw delegate 3\n\U0001f600", "code point")]
    [InlineData("raw member:~:NotAKind:~ 0\n", "reason")]
    [InlineData("raw delegate 1\nxy", "followed")]
    public void RawFraming_InvalidHeadersAndBoundariesDiagnose(string source, string message)
    {
        var diagnostics = new DiagnosticBag();
        new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.InvalidRawFrame
            && d.Message.Contains(message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RawFraming_UnpairedSurrogateIsRejectedRatherThanReplaced()
    {
        var diagnostics = new DiagnosticBag();
        new Lexer("raw member 3\n\uD800", diagnostics).TokenizeAll();
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.InvalidRawFrame
            && d.Message.Contains("surrogate", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("statement", "~")]
    [InlineData("delegate", "unsupported: delegate \u03c0\r\nbecause")]
    public void RawFraming_EmitterPreservesAllInteropMetadata(string? feature, string? reason)
    {
        foreach (var kind in Enum.GetValues<InteropMemberKind>())
        foreach (var unit in new[] { false, true })
        {
            var original = CSharpToCalorConverter.PreserveCompilationUnit(
                "// tail\r\n", "test", "test");
            var block = new CSharpInteropBlockNode(TextSpan.Empty, "// tail\r\n",
                feature, reason, kind, unit);
            var module = original.With(update =>
            {
                update.InteropBlocks = new[] { block };
                update.Items = new AstNode[] { block };
            });
            var emitted = new CalorEmitter().Emit(module);
            Assert.DoesNotContain("§CSHARP", emitted);
            var roundtrip = Assert.Single(ParseRawModule(emitted).InteropBlocks);
            Assert.Equal(block.CSharpCode, roundtrip.CSharpCode);
            Assert.Equal(feature, roundtrip.FeatureName);
            Assert.Equal(reason, roundtrip.Reason);
            Assert.Equal(kind, roundtrip.MemberKind);
            Assert.Equal(unit, roundtrip.IsCompilationUnitPassthrough);
        }
    }

    [Fact]
    public void RawFraming_StatementAndSourceToolsDoNotRewritePayload()
    {
        var payload = "  // §/M\r\n// §/NEW{x}\r\n// commas ,,, \u03c0 \r\n";
        var source = $"§M{{RawTest}}\n  §F{{Run:pub}} () -> void\n    raw statement {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n";
        var module = ParseRawModule(source);
        Assert.Equal(payload, Assert.IsType<RawCSharpNode>(Assert.Single(module.Functions[0].Body)).CSharpCode);
        var emitted = new CalorEmitter().Emit(module);
        Assert.Equal(payload, Assert.IsType<RawCSharpNode>(
            Assert.Single(ParseRawModule(emitted).Functions[0].Body)).CSharpCode);
        var healed = new Calor.Compiler.Formatting.SourceHealer().Heal(source);
        Assert.Equal(payload, Assert.IsType<RawCSharpNode>(
            Assert.Single(ParseRawModule(healed).Functions[0].Body)).CSharpCode);
        Assert.Equal(healed, new Calor.Compiler.Formatting.SourceHealer().Heal(healed));
        Assert.Equal(source, new PostConversionFixer().Fix(source).FixedSource);
        var formatted = new Calor.Compiler.Formatting.CalorFormatter().FormatSource(source);
        Assert.True(formatted.Success, string.Join("\n", formatted.Errors));
        Assert.Equal(payload, Assert.IsType<RawCSharpNode>(
            Assert.Single(ParseRawModule(formatted.Formatted).Functions[0].Body)).CSharpCode);
    }

    [Theory]
    [InlineData("// end")]
    [InlineData("// end\r\n\r\n")]
    [InlineData("// end\n\n")]
    public void RawFraming_WholeUnitAndAstFormatterKeepTerminalPayload(string payload)
    {
        var module = CSharpToCalorConverter.PreserveCompilationUnit(payload, "test", "test");
        var formatted = new Calor.Compiler.Formatting.CalorFormatter().Format(module);
        var parsed = ParseRawModule(formatted);
        Assert.Equal(payload, Assert.Single(parsed.InteropBlocks).CSharpCode);
        Assert.Equal(payload, new CSharpEmitter().Emit(parsed));
    }

    [Fact]
    public void RawFraming_InteropHintsInspectPayloadNotEnvelopeOrDelimiterText()
    {
        var payload = "// }§/CSHARP\npublic async Task Go() { await Work(); }";
        var source = $"raw member {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}";
        Assert.Contains(InteropHintAnalyzer.AnalyzeInteropBlocks(source),
            h => h.Contains("async", StringComparison.Ordinal));
        Assert.Throws<FormatException>(() => InteropHintAnalyzer.AnalyzeInteropBlocks("raw member 12\nx"));
    }

    [Fact]
    public void RawFraming_LambdaCaptureAndHoistingPreservePayload()
    {
        const string payload = "// \u03c0\r\n  // §/LAM\r\n";
        var source = $"§M{{RawTest}}\n  §F{{Run:pub}} () -> void\n" +
            "    §B{action} §LAM{}\n" +
            $"      raw statement {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n" +
            "    §/LAM\n";
        var module = ParseRawModule(source);
        var emitted = new CalorEmitter().Emit(module);
        var diagnostics = new DiagnosticBag();
        var frames = new Lexer(emitted, diagnostics).TokenizeAll()
            .Where(t => t.Value is RawCSharpFrame).ToList();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal(payload, Assert.IsType<RawCSharpFrame>(Assert.Single(frames).Value).Code);
        ParseRawModule(emitted);
    }

    [Theory]
    [InlineData("goto")]
    [InlineData("member:~:Other:unsupported%20statement")]
    public void RawFraming_StatementReasonIsPreservedInItsActualScope(string reason)
    {
        const string payload = "// exact\r\n";
        var source = $"§M{{RawTest}}\n  §CL{{C:pub}}\n    §MT{{Run:pub}}\n" +
            $"      raw {reason} {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n";
        var original = ParseRawModule(source);
        var raw = Assert.IsType<RawCSharpNode>(Assert.Single(original.Classes[0].Methods[0].Body));
        Assert.Equal(reason, raw.FramingReason);
        var parsed = ParseRawModule(new CalorEmitter().Emit(original));
        var roundtrip = Assert.IsType<RawCSharpNode>(Assert.Single(parsed.Classes[0].Methods[0].Body));
        Assert.Equal(reason, roundtrip.FramingReason);
        Assert.Equal(payload, roundtrip.CSharpCode);
    }

    [Theory]
    [InlineData("T1-INIT-OBJECT")]
    [InlineData("T1-INIT-ORDER")]
    public void RawFraming_DoesNotIntroduceInitializerRescue(string fixture)
    {
        var source = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests", "TestData", "InteropScope", "T1", "fixtures", fixture + ".cs.txt"));
        var result = new CSharpToCalorConverter(new ConversionOptions
        {
            ValidateRoundTripCSharp = false,
            UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot
        }).Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var compiled = Program.Compile(result.CalorSource!, fixture + ".calr",
            new CompilationOptions
            {
                EnforceEffects = false,
                UnknownCallPolicy = Effects.UnknownCallPolicy.Permissive,
                DeferGeneratedOutputValidation = true
            });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics) + "\n" + result.CalorSource);
        var validation = GeneratedCSharpCompiler.Validate(compiled.GeneratedCode);
        Assert.True(validation.CompilationSuccess,
            string.Join("\n", validation.FormattedCompilationErrors) + "\n" + result.CalorSource);
        var validated = new CSharpToCalorConverter(new ConversionOptions
        {
            UserManifestRoot = global::Calor.Tests.Shared.HermeticCalorHome.EmptyManifestRoot
        }).Convert(source);
        Assert.True(validated.Success, string.Join("\n", validated.Issues));
        Assert.Equal(0, validated.Paths.Rescue);
    }

    [Fact]
    public void RawFraming_HealerDiagnosticsRetainOriginalLineNumbers()
    {
        const string payload = "// one\r\n// two\r\n";
        var source = "§M{T}\n  §F{Main:pub} () -> void\n" +
            $"    raw statement {System.Text.Encoding.UTF8.GetByteCount(payload)}\n{payload}\n" +
            "    §IF (== 1 1)\n      §P \"a\"\n      §EI (== 1 2)\n      §P \"b\"";
        var healer = new Calor.Compiler.Formatting.SourceHealer();
        var healed = healer.Heal(source);
        var ambiguity = Assert.Single(healer.Ambiguities);
        Assert.Equal(10, ambiguity.Line);
        Assert.Contains("line 9", ambiguity.Message);
        var tokens = new Lexer(healed, new DiagnosticBag()).TokenizeAll();
        Assert.Equal(payload, Assert.IsType<RawCSharpFrame>(
            Assert.Single(tokens.Where(t => t.Value is RawCSharpFrame)).Value).Code);
    }

    private static ModuleNode ParseRawModule(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    [Theory]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    public void WholeCompilationUnitBoundary_PreservesExactSource(string newline, bool finalNewline)
    {
        var source = string.Join(newline, new[]
        {
            "#if NEVER",
            "  unmatched \"\"\" {",
            "#endif",
            "namespace First { public class A {} }",
            "namespace Second { public class B {",
            "  public const string Unicode = \"\u03c0\U0001f600\";",
            "  public const string Raw = \"\"\"",
            "      raw }§/CSHARP",
            "        indentation",
            "      \"\"\";",
            "  public const string Verbatim = @\"first",
            "    second \"\"quoted\"\" }§/CSHARP\";",
            "} }",
            "// final comment"
        }) + (finalNewline ? newline : "");
        Assert.True(GeneratedCSharpCompiler.Validate(source).CompilationSuccess);
        var module = CSharpToCalorConverter.PreserveCompilationUnit(
            source, "validation-failure", "Preserve exact source after validation failure.");
        var block = Assert.Single(module.InteropBlocks);
        Assert.True(block.IsCompilationUnitPassthrough);
        Assert.Equal("validation-failure", block.FeatureName);
        Assert.Equal(source, block.CSharpCode);
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(source),
            System.Text.Encoding.UTF8.GetBytes(new CSharpEmitter().Emit(module)));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void WholeCompilationUnitPayload_WithFinalNewline_DoesNotAcquireIndentation(string newline)
    {
        var source = string.Join(newline, new[]
        {
            "namespace First { public class A {} }",
            "namespace Second { public class B {",
            "  public const string Raw = \"\"\"",
            "      raw }§/CSHARP",
            "        indentation",
            "      \"\"\";",
            "  public const string Verbatim = @\"first",
            "    second \"\"quoted\"\" }§/CSHARP\";",
            "} }"
        }) + newline;
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(result.CalorSource!, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal(source, Assert.Single(module.InteropBlocks).CSharpCode);
        Assert.Equal(source, new CSharpEmitter().Emit(module));
    }

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
            §M{TestModule}
              §CSHARP{public static void Helper() { Console.WriteLine("hi"); }}§/CSHARP
            """;
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var parser = new Parser(lexer.TokenizeAllForParser(), diag);
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
            §M{TestModule}
              §CL{MyClass:pub}
                §CSHARP{public T GetAll<T>() where T : class => default!;}§/CSHARP
            """;
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var parser = new Parser(lexer.TokenizeAllForParser(), diag);
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
            §M{TestModule}
              §CL{MyClass:pub}
                §CSHARP{public T GetAll<T>() => default!;}§/CSHARP
            """;
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var parser = new Parser(lexer.TokenizeAllForParser(), diag);
        var module = parser.Parse();
        Assert.Empty(diag);

        var emitter = new CalorEmitter(new ConversionContext());
        var emitted = emitter.Emit(module);

        Assert.Contains("raw member ", emitted);
        Assert.DoesNotContain("}§/CSHARP", emitted);
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
            operatorOverloads: Array.Empty<OperatorOverloadNode>(),
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
        // A destructor/finalizer is an unsupported member type that hits the default case in ConvertClassMember
        var csharpSource = """
            public class MyClass
            {
                public int Add(int a, int b) => a + b;
                ~MyClass() { }
            }
            """;

        var options = new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
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
        // The unsupported destructor should be preserved as an interop block
        Assert.Contains("raw ", result.CalorSource!);
        Assert.DoesNotContain("}§/CSHARP", result.CalorSource!);
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
            Fidelity = ConversionFidelity.Lossy,
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
            Fidelity = ConversionFidelity.Lossy,
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
            Fidelity = ConversionFidelity.Lossy,
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
            Fidelity = ConversionFidelity.Lossy,
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
            Fidelity = ConversionFidelity.Lossy,
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
        var tool = new ConvertTool();
        var args = JsonDocument.Parse("""
            {
                "mode": "assess",
                "source": "public class Calc { public int Add(int a, int b) => a + b; }"
            }
            """).RootElement;

        var result = await tool.ExecuteAsync(args);

        Assert.False(result.IsError);
        var text = result.Content[0].Text!;
        Assert.Contains("score", text);
        Assert.Contains("summary", text);
    }

    #endregion

    #region HandleUnsupportedStatement Interop Mode Tests

    [Fact]
    public void Conversion_InteropMode_UnsupportedStatement_PreservesAsRawCSharp()
    {
        // 'unsafe' is an unsupported statement type that triggers HandleUnsupportedStatement
        var csharpSource = """
            public class MyClass
            {
                public unsafe void DoWork()
                {
                    int x = 1;
                    int* ptr = &x;
                    *ptr = 2;
                }
            }
            """;

        var options = new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
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
        // A class with a destructor (unsupported member type) should produce an interop block
        // for that specific member while converting other members normally
        var csharpSource = """
            public class MyList
            {
                private int[] _items = new int[10];
                public int Count => _items.Length;
                ~MyList() { }
            }
            """;

        var options = new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            ModuleName = "TestModule",
            GracefulFallback = true,
            Mode = ConversionMode.Interop
        };
        var converter = new CSharpToCalorConverter(options);
        var result = converter.Convert(csharpSource);

        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);
        // The destructor should be preserved as an interop block
        Assert.Contains("raw ", result.CalorSource!);
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
