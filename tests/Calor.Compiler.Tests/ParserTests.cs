using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class ParserTests
{
    [Theory]
    [InlineData("public", "pub")]
    [InlineData("internal", "int")]
    [InlineData("protected", "prot")]
    [InlineData("pro", "prot")]
    [InlineData("private", "priv")]
    [InlineData("pri", "priv")]
    [InlineData("protected-internal", "prot-int")]
    [InlineData("private-protected", "priv-prot")]
    public void RemovedClassVisibility_IsNotReinterpretedAsABaseType(string alias, string canonical)
    {
        AssertRetiredMarker($"§M{{Modifiers}}\n  §CL{{Widget:{alias}}}", "§CL");

        // The old canonical strings are retained case identities, not native visibility spellings.
        var (native, visibility) = canonical switch
        {
            "pub" => ("public", Visibility.Public),
            "int" => ("internal", Visibility.Internal),
            "prot" => ("protected", Visibility.Protected),
            "priv" => ("private", Visibility.Private),
            "prot-int" => ("protected internal", Visibility.ProtectedInternal),
            "priv-prot" => ("private protected", Visibility.PrivateProtected),
            _ => throw new ArgumentOutOfRangeException(nameof(canonical))
        };
        var module = ParseNative($"module Modifiers\n  class Owner\n    class Widget {native}");
        var type = Assert.Single(Assert.Single(module.Classes).NestedClasses);
        Assert.Equal("Widget", type.Name);
        Assert.Equal(visibility, type.Visibility);
        Assert.Null(type.BaseClass);
        var roundTrip = ParseNative(new CalorEmitter().Emit(module));
        Assert.Equal(visibility, Assert.Single(Assert.Single(roundTrip.Classes).NestedClasses).Visibility);

        if (alias is "pro" or "pri")
        {
            Parse($"module Modifiers\n  class Widget {alias}", out var diagnostics);
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InvalidModifier
                && diagnostic.Message.Contains($"Unknown class modifier '{alias}'"));
        }
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", false)]
    [InlineData("False", true)]
    [InlineData("TRUE", true)]
    public void ConfigureAwait_RequiresExactBooleanSpelling(string value, bool hasError)
    {
        AssertRetiredMarker(
            $"§M{{Modifiers}}\n  §AF{{Run:pub}} () -> i32\n    §R §AWAIT{{{value}}} §C{{GetAsync}} §/C",
            "§AWAIT");
        var source = $"""
            module Modifiers
              func Run async (System.Threading.Tasks.Task<int>:task) → System.Threading.Tasks.Task<int>
                !
                return await task.ConfigureAwait({value})
            """;
        var module = ParseNative(source);
        var function = Assert.Single(module.Functions);
        Assert.True(function.IsAsync);
        var awaited = Assert.IsType<AwaitExpressionNode>(
            Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body)).Expression);
        Assert.Null(awaited.ConfigureAwait);
        var call = Assert.IsType<ExpressionCallNode>(awaited.Awaited);
        var member = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal("ConfigureAwait", member.FieldName);
        Assert.Equal("task", Assert.IsType<ReferenceNode>(member.Target).Name);
        var argument = Assert.Single(call.Arguments);
        if (hasError)
            Assert.Equal(value, Assert.IsType<ReferenceNode>(argument).Name);
        else
            Assert.Equal(value == "true", Assert.IsType<BoolLiteralNode>(argument).Value);

        // Native configuration is an ordinary call: spelling is checked by binding, not an await attribute.
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(module);
        Assert.Equal(hasError, diagnostics.HasErrors);
        if (hasError)
        {
            var error = Assert.Single(diagnostics.Errors,
                diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
            Assert.Contains(value, error.Message);
            Assert.Equal(value, source.Substring(error.Span.Start, error.Span.Length));

            var rejected = Program.Compile(source);
            Assert.Contains(rejected.Diagnostics.Errors,
                diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference
                    && source.Substring(diagnostic.Span.Start, diagnostic.Span.Length) == value);
            Assert.True(string.IsNullOrEmpty(rejected.GeneratedCode));

            var declaredSource = $"""
                module Modifiers
                  func Run async (System.Threading.Tasks.Task<int>:task, bool:{value}) → System.Threading.Tasks.Task<int>
                    !
                    return await task.ConfigureAwait({value})
                """;
            var declared = Program.Compile(declaredSource);
            Assert.False(declared.HasErrors, string.Join("\n", declared.Diagnostics));
            Assert.True(GeneratedCSharpCompiler.Validate(declared.GeneratedCode).CompilationSuccess);
        }
        else
        {
            var result = Program.Compile(source);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
        }
    }

    [Theory]
    [InlineData("class", TypeConstraintKind.Class)]
    [InlineData("CLASS", TypeConstraintKind.TypeName)]
    public void ConstraintKeywords_AreNotCaseFolded(string constraint, TypeConstraintKind expected)
    {
        var source = $"module Modifiers\n  func Run<T> () → void\n    !\n    where T : {constraint}";
        foreach (var module in new[] { ParseNative(source), ParseNative(new CalorEmitter().Emit(ParseNative(source))) })
        {
            var parsed = Assert.Single(Assert.Single(Assert.Single(module.Functions).TypeParameters).Constraints);
            Assert.Equal(expected, parsed.Kind);
            if (expected == TypeConstraintKind.TypeName)
                Assert.Equal(constraint, parsed.TypeName);
        }
    }

    [Theory]
    [InlineData("(i32:x:REF)", "")]
    [InlineData("", "    §I{i32:x:REF}")]
    public void ParameterModifierCaseVariants_ReportAnError(string signature, string taggedParameter)
    {
        AssertRetiredMarker($"§M{{Modifiers}}\n  §F{{Run:pub}} {signature}\n{taggedParameter}\n    §R",
            taggedParameter.Length == 0 ? "§F" : "§I");
        var nativeSignature = signature.Length == 0 ? "(REF int:x)" : "(int:x:REF)";
        var source = $"module Modifiers\n  func Run {nativeSignature} → void\n    return";
        Parse(source, out var diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken
            && diagnostic.Message.Contains(signature.Length == 0
                ? "Expected a type:name parameter."
                : "Parameter modifiers precede the type"));

        var module = ParseNative("module Modifiers\n  func Run (ref int:x) → void\n    !\n    return");
        var parameter = Assert.Single(Assert.Single(module.Functions).Parameters);
        Assert.Equal(ParameterModifier.Ref, parameter.Modifier);
        Assert.Equal("int", parameter.TypeName);
        Assert.Equal("x", parameter.Name);
    }

    [Theory]
    [InlineData("MT")]
    [InlineData("AMT")]
    public void CompactMethodWithoutModifiers_DoesNotTreatDefaultVisibilityAsAModifier(string marker)
    {
        AssertRetiredMarker($"§M{{Modifiers}}\n  §CL{{Widget:pub}}\n    §{marker}{{Run:pub}} () -> void",
            $"§{marker}");
        var source = marker == "AMT"
            ? "module Modifiers\n  class Widget\n    func Run async () → System.Threading.Tasks.Task\n      !\n      return"
            : "module Modifiers\n  class Widget\n    func Run () → void\n      !\n      return";
        foreach (var module in new[] { ParseNative(source), ParseNative(new CalorEmitter().Emit(ParseNative(source))) })
        {
            var method = Assert.Single(Assert.Single(module.Classes).Methods);
            Assert.Equal("Run", method.Name);
            Assert.Equal(Visibility.Public, method.Visibility);
            Assert.Equal(MethodModifiers.None, method.Modifiers);
            Assert.Equal(marker == "AMT", method.IsAsync);
        }
    }

    [Theory]
    [InlineData("STATIC", "static")]
    [InlineData("STAT", "static")]
    [InlineData("VIRTUAL", "virtual")]
    [InlineData("OV", "override")]
    [InlineData("Readonly", "readonly")]
    [InlineData("Required", "required")]
    [InlineData("Unsafe", "unsafe")]
    [InlineData("VOL", "volatile")]
    public void MemberModifierCaseVariants_ReportCanonicalSpelling(string modifier, string canonical)
    {
        var source = $$"""
            module Modifiers
              class Widget
                func Run {{modifier}} () → void
            """;
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InvalidModifier
            && diagnostic.Message.Contains($"'{canonical}'"));

        var nativeMember = canonical switch
        {
            "readonly" => "field int:Value readonly = 1",
            "required" => "property string:Value required\n      get\n      set",
            "volatile" => "field int:Value volatile",
            _ => $"func Run {canonical} () → void\n      !"
        };
        var type = Assert.Single(ParseNative($"module Modifiers\n  class Widget\n    {nativeMember}").Classes);
        var expected = canonical switch
        {
            "static" => MethodModifiers.Static,
            "virtual" => MethodModifiers.Virtual,
            "override" => MethodModifiers.Override,
            "readonly" => MethodModifiers.Readonly,
            "required" => MethodModifiers.Required,
            "unsafe" => MethodModifiers.Unsafe,
            "volatile" => MethodModifiers.Volatile,
            _ => throw new ArgumentOutOfRangeException(nameof(canonical))
        };
        var actual = canonical is "readonly" or "volatile" ? Assert.Single(type.Fields).Modifiers
            : canonical == "required" ? Assert.Single(type.Properties).Modifiers
            : Assert.Single(type.Methods).Modifiers;
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("STATIC", "static")]
    [InlineData("ABS", "abstract")]
    [InlineData("Struct", "struct")]
    [InlineData("Readonly", "readonly")]
    public void ClassModifierCaseVariants_ReportCanonicalSpelling(string modifier, string canonical)
    {
        var source = $"module Modifiers\n  class Widget {modifier}";
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InvalidModifier
            && diagnostic.Message.Contains($"'{canonical}'"));

        var header = canonical switch
        {
            "static" => "module Widget",
            "abstract" => "class Widget abstract",
            "struct" => "class Widget struct",
            "readonly" => "class Widget struct readonly",
            _ => throw new ArgumentOutOfRangeException(nameof(canonical))
        };
        var type = Assert.Single(ParseNative($"module Modifiers\n  {header}").Classes);
        Assert.Equal(canonical == "static", type.IsModule);
        Assert.Equal(canonical == "static", type.IsStatic);
        Assert.Equal(canonical == "abstract", type.IsAbstract);
        Assert.Equal(canonical is "struct" or "readonly", type.IsStruct);
        Assert.Equal(canonical == "readonly", type.IsReadOnly);
    }

    [Theory]
    [InlineData("add", "+")]
    [InlineData("SUBTRACT", "-")]
    [InlineData("mul", "*")]
    [InlineData("div", "/")]
    [InlineData("mod", "%")]
    [InlineData("pow", "**")]
    [InlineData("eq", "==")]
    [InlineData("ne", "!=")]
    [InlineData("lt", "<")]
    [InlineData("le", "<=")]
    [InlineData("gt", ">")]
    [InlineData("ge", ">=")]
    [InlineData("and", "&&")]
    [InlineData("or", "||")]
    [InlineData("not", "!")]
    [InlineData("band", "&")]
    [InlineData("bor", "|")]
    [InlineData("bxor", "^")]
    [InlineData("shl", "<<")]
    [InlineData("shr", ">>")]
    [InlineData("negate", "-")]
    [InlineData("bnot", "~")]
    public void Parse_RemovedWordOperator_NamesSymbol(string word, string symbol)
    {
        var source = $"module Test\n  func Test () → int\n    return ({word} 1 2)";
        Parse(source, out var diagnostics);
        var error = Assert.Single(diagnostics.Errors.Where(diagnostic =>
            diagnostic.Code == DiagnosticCode.UnexpectedToken
            && diagnostic.Message.StartsWith("Expected CloseParen", StringComparison.Ordinal)));
        Assert.Equal("1", source.Substring(error.Span.Start, error.Span.Length));
        Assert.Null(BinaryOperatorExtensions.FromString(word));
        Assert.Null(UnaryOperatorExtensions.FromString(word));

        var expression = symbol switch
        {
            "**" => "Math.Pow(1, 2)",
            "!" => "!true",
            "~" => "~1",
            "-" when word == "negate" => "-1",
            "&&" or "||" => $"true {symbol} false",
            _ => $"1 {symbol} 2"
        };
        var native = Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(
            ParseNative($"module Test\n  func Test () → object\n    !\n    return {expression}").Functions).Body)).Expression;
        if (symbol == "**")
        {
            var call = Assert.IsType<ExpressionCallNode>(native);
            var target = Assert.IsType<FieldAccessNode>(call.TargetExpression);
            Assert.Equal("Math", Assert.IsType<ReferenceNode>(target.Target).Name);
            Assert.Equal("Pow", target.FieldName);
            Assert.Equal(new long[] { 1, 2 }, call.Arguments.Cast<IntLiteralNode>().Select(argument => argument.Value));
        }
        else if (word == "negate")
            Assert.Equal(-1, Assert.IsType<IntLiteralNode>(native).Value);
        else if (symbol is "!" or "~")
            Assert.Equal(UnaryOperatorExtensions.FromString(symbol), Assert.IsType<UnaryOperationNode>(native).Operator);
        else
            Assert.Equal(BinaryOperatorExtensions.FromString(symbol), Assert.IsType<BinaryOperationNode>(native).Operator);
    }

    [Theory]
    [InlineData("LEN", "\"x\"")]
    [InlineData("Cast", "i32 1")]
    [InlineData("IS-LETTER", "'A'")]
    [InlineData("SB-NEW", "\"x\"")]
    [InlineData("INC", "1")]
    public void Parse_OperatorNames_AreExactCase(string name, string arguments)
    {
        AssertRetiredMarker($"§M{{Test}}\n  §F{{Test:pub}} () -> i32\n    §R ({name} {arguments})", "§R");
        var source = $"module Test\n  func Test () → int\n    return ({name} {arguments})";
        Parse(source, out var diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken
            && diagnostic.Message.Contains("CloseParen"));

        var expression = name switch
        {
            "LEN" => "\"x\".Length",
            "Cast" => "(int)1",
            "IS-LETTER" => "char.IsLetter('A')",
            "SB-NEW" => "new System.Text.StringBuilder(\"x\")",
            "INC" => "1 + 1",
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var module = ParseNative($"module NativeOperators\n  func Run () → object\n    ! alloc\n    return {expression}");
        var native = Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body)).Expression;
        switch (name)
        {
            case "LEN":
                Assert.Equal("Length", Assert.IsType<FieldAccessNode>(native).FieldName);
                break;
            case "Cast":
                Assert.Equal(TypeOp.Cast, Assert.IsType<TypeOperationNode>(native).Operation);
                break;
            case "IS-LETTER":
                Assert.Equal("IsLetter", Assert.IsType<FieldAccessNode>(
                    Assert.IsType<ExpressionCallNode>(native).TargetExpression).FieldName);
                break;
            case "SB-NEW":
                Assert.Equal("System.Text.StringBuilder", Assert.IsType<NewExpressionNode>(native).TypeName);
                break;
            case "INC":
                Assert.Equal(BinaryOperator.Add, Assert.IsType<BinaryOperationNode>(native).Operator);
                break;
        }
        Assert.True(GeneratedCSharpCompiler.Validate(new CSharpEmitter().Emit(module)).CompilationSuccess);

        if (name is "LEN" or "Cast" or "INC")
        {
            // These are now legal ordinary function names, not case variants of parser intrinsics.
            var declaration = $"module Names\n  func {name} (int:value) → int\n    !\n    return value";
            var matching = declaration + $"\n  func Run () → int\n    !\n    return {name}(1)";
            var result = Program.Compile(matching);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            var call = Assert.IsType<CallExpressionNode>(Assert.IsType<ReturnStatementNode>(
                Assert.Single(result.Ast!.Functions.Single(function => function.Name == "Run").Body)).Expression);
            Assert.Equal(name, call.Target);
            Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);

            var wrongCase = name.ToLowerInvariant();
            var mismatching = ParseNative(declaration + $"\n  func Run () → int\n    !\n    return {wrongCase}(1)");
            var validation = GeneratedCSharpCompiler.Validate(new CSharpEmitter().Emit(mismatching));
            Assert.Contains(validation.CompilationErrors, diagnostic => diagnostic.Id == "CS0103"
                && diagnostic.GetMessage().Contains($"'{wrongCase}'"));
        }
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static ModuleNode ParseNative(string source)
    {
        var module = Parse(source, out var diagnostics);
        Assert.Empty(diagnostics.Errors);
        Assert.DoesNotContain('§', source);
        Assert.Empty(module.InteropBlocks);
        return module;
    }

    private static void AssertRetiredMarker(string source, string marker)
    {
        Parse(source, out var diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownSectionMarker
            && diagnostic.Message.Contains($"'{marker}'")
            && source.Substring(diagnostic.Span.Start, diagnostic.Span.Length) == marker);
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownSectionMarker
            && diagnostic.Message.Contains($"'{marker}'"));
        Assert.True(string.IsNullOrEmpty(result.GeneratedCode));
    }

    [Fact]
    public void Parse_MinimalModule_ReturnsModuleNode()
    {
        var source = """
            module Test
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(module.LocalId, module.Id);
        Assert.Null(module.ToolingId);
        Assert.Equal("Test", module.Name);
        Assert.Empty(module.Functions);
    }

    [Fact]
    public void Parse_ModuleWithFunction_ReturnsFunctionNode()
    {
        var source = """
            module Test
              func MyFunc public () → void
                !
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Equal(func.LocalId, func.Id);
        Assert.Null(func.ToolingId);
        Assert.Equal("MyFunc", func.Name);
        Assert.Equal(Visibility.Public, func.Visibility);
    }

    [Fact]
    public void Parse_FunctionWithParameters_ReturnsParameterNodes()
    {
        var source = """
            module Test
              func Add public (int:a, int:b) → int
                !
                return a + b
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Equal(2, func.Parameters.Count);
        Assert.Equal("a", func.Parameters[0].Name);
        Assert.Equal("int", func.Parameters[0].TypeName);
        Assert.Equal("b", func.Parameters[1].Name);
        Assert.Equal("int", func.Parameters[1].TypeName);
        Assert.Equal("int", func.Output!.TypeName);
    }

    [Fact]
    public void Parse_UnknownPrefixBangEffect_PreservesDiagnosticText()
    {
        foreach (var effects in new[] { "!e", "alloc, !e" })
        {
            var source = $$"""
                module Test
                  func Main public () → void
                    ! {{effects}}
                """;
            var result = Program.Compile(source);
            var diagnostic = Assert.Single(result.Diagnostics.Errors.Where(
                diagnostic => diagnostic.Code == DiagnosticCode.UnknownEffectCode));
            Assert.Contains("'!e'", diagnostic.Message);
            Assert.DoesNotContain("'! e'", diagnostic.Message);
            Assert.DoesNotContain(result.Diagnostics.Errors,
                diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken);
        }
    }

    [Theory]
    [InlineData("!e", "!e", false)]
    [InlineData("alloc, !e", "!e", true)]
    [InlineData("!cw", "!cw", false)]
    [InlineData("alloc, !cw", "!cw", true)]
    [InlineData("alloc, !", "!", true)]
    [InlineData("alloc, ! cw", "!", true)]
    public void NativeEffects_MalformedBangCodeDoesNotStartAnotherRow(string row, string code, bool hasAllocation)
    {
        foreach (var source in new[]
        {
            $"module Test\n  func Main () → int\n    ! {row}\n    return 42\n  func Next () → int\n    !\n    return 7",
            $"module Test\n  class Worker\n    func Main () → int\n      ! {row}\n      return 42\n    func Next () → int\n      !\n      return 7"
        })
        {
            var module = Parse(source, out var diagnostics);
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownEffectCode
                && diagnostic.Message.Contains($"'{code}'"));
            var callables = module.Functions.Select(function => (function.Name, function.Effects, function.Body))
                .Concat(module.Classes.SelectMany(type => type.Methods)
                    .Select(method => (method.Name, method.Effects, method.Body))).ToArray();
            Assert.Equal(new[] { "Main", "Next" }, callables.Select(callable => callable.Name));
            Assert.Equal(42, Assert.IsType<IntLiteralNode>(Assert.IsType<ReturnStatementNode>(
                callables[0].Body.Last()).Expression).Value);
            Assert.Equal(7, Assert.IsType<IntLiteralNode>(Assert.IsType<ReturnStatementNode>(
                Assert.Single(callables[1].Body)).Expression).Value);
            Assert.Empty(callables[1].Effects!.Effects);
            var effects = callables[0].Effects;
            Assert.NotNull(effects);
            Assert.Equal(hasAllocation, effects.Effects.ContainsKey("memory"));
            if (hasAllocation)
                Assert.Equal("allocation", effects.Effects["memory"]);
            Assert.False(effects.Effects.ContainsKey("io"));
            var result = Program.Compile(source);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownEffectCode
                && diagnostic.Message.Contains($"'{code}'"));
            Assert.True(string.IsNullOrEmpty(result.GeneratedCode));
        }
    }

    [Fact]
    public void NativeEffects_CanonicalRowsStillEnforceTheirDeclaredPermissions()
    {
        var pure = Program.Compile("""
            module Test
              func Main () → void
                !
                Console.WriteLine("effect")
            """);
        Assert.Contains(pure.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("cw"));
        Assert.True(string.IsNullOrEmpty(pure.GeneratedCode));

        var declared = Program.Compile("""
            module Test
              func Main () → void
                ! cw
                Console.WriteLine("effect")
            """);
        Assert.False(declared.HasErrors, string.Join("\n", declared.Diagnostics));
        Assert.Equal("console_write", Assert.Single(declared.Ast!.Functions).Effects!.Effects["io"]);
        Assert.True(GeneratedCSharpCompiler.Validate(declared.GeneratedCode).CompilationSuccess);
    }

    [Fact]
    public void Parse_FallibleCall_PreservesTerminalBang()
    {
        var source = """
            module Test
              func Risky () → void
                !
                return
              func Main public () → void
                !
                Risky()
            """;

        AssertRetiredMarker("§M{Test}\n  §F{Main:pub} () -> void\n    §C{Risky!} §/C", "§C");
        var invalid = "module Test\n  func Main () → void\n    Risky!()";
        Parse(invalid, out var diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken
            && invalid.Substring(diagnostic.Span.Start, diagnostic.Span.Length) == "!");

        var module = ParseNative(source);
        var call = Assert.IsType<CallExpressionNode>(Assert.IsType<ExpressionStatementNode>(
            Assert.Single(module.Functions.Single(function => function.Name == "Main").Body)).Expression);
        Assert.Equal("Risky", call.Target);
        Assert.Empty(call.Arguments);
        Assert.Equal("Risky()", call.Accept(new CSharpEmitter()));
        Assert.False(Program.Compile(source).HasErrors);
    }

    [Fact]
    public void Parse_FunctionWithCallStatement_ReturnsCallNode()
    {
        var source = """
            module Test
              func Main public () → void
                ! cw
                Console.WriteLine("Hello")
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var call = Assert.IsType<ExpressionCallNode>(Assert.IsType<ExpressionStatementNode>(func.Body[0]).Expression);
        var target = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal("Console", Assert.IsType<ReferenceNode>(target.Target).Name);
        Assert.Equal("WriteLine", target.FieldName);
        Assert.Single(call.Arguments);

        var arg = call.Arguments[0] as StringLiteralNode;
        Assert.NotNull(arg);
        Assert.Equal("Hello", arg.Value);
    }

    [Fact]
    public void Parse_FunctionWithEffects_ReturnsEffectsNode()
    {
        var source = """
            module Test
              func Main public () → void
                ! cw
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.NotNull(func.Effects);
        Assert.Equal("console_write", func.Effects.Effects["io"]);
    }

    [Fact]
    public void Parse_LegacyModuleCloser_ReportsError()
    {
        var source = """
            §M{Test}
            §/M
            """;

        AssertRetiredMarker(source, "§/M");
        AssertRetiredMarker("module Test\n§/M", "§/M");
    }

    [Fact]
    public void Parse_LegacyFuncCloser_ReportsError()
    {
        var source = """
            §M{Test}
            §F{Main:pub}
              §O{void}
            §/F
            §/M
            """;

        AssertRetiredMarker(source, "§/F");
        var nativeWithClosers = "module Test\n  func Main public () → void\n    return\n  §/F\n§/M";
        AssertRetiredMarker(nativeWithClosers, "§/F");
        AssertRetiredMarker(nativeWithClosers, "§/M");
    }

    [Fact]
    public void Parse_MissingRequiredAttribute_ReportsError()
    {
        var source = """
            §M{}
            """;

        AssertRetiredMarker(source, "§M");
        Parse("module", out var diagnostics);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.MissingRequiredAttribute
            && diagnostic.Message.Contains("name"));
    }

    [Fact]
    public void Parse_ReturnStatement_ReturnsReturnNode()
    {
        var source = """
            module Test
              func GetValue public () → int
                !
                return 42
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var ret = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(ret);
        Assert.NotNull(ret.Expression);

        var literal = ret.Expression as IntLiteralNode;
        Assert.NotNull(literal);
        Assert.Equal(42, literal.Value);
    }

    [Fact]
    public void Parse_HelloWorldProgram_Succeeds()
    {
        var source = """
            module Hello
              func Main public () → void
                ! cw
                Console.WriteLine("Hello from Calor!")
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal(module.LocalId, module.Id);
        Assert.Null(module.ToolingId);
        Assert.Equal("Hello", module.Name);
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Equal("Main", func.Name);
        Assert.Equal(Visibility.Public, func.Visibility);
        Assert.Single(func.Body);

        var call = Assert.IsType<ExpressionCallNode>(Assert.IsType<ExpressionStatementNode>(func.Body[0]).Expression);
        var target = Assert.IsType<FieldAccessNode>(call.TargetExpression);
        Assert.Equal("Console", Assert.IsType<ReferenceNode>(target.Target).Name);
        Assert.Equal("WriteLine", target.FieldName);
        Assert.Single(call.Arguments);

        var arg = call.Arguments[0] as StringLiteralNode;
        Assert.NotNull(arg);
        Assert.Equal("Hello from Calor!", arg.Value);
        var result = Program.Compile(source);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
    }

    /// <summary>
    /// Retains the historical identity as a rejection control; native modules use installed semantics.
    /// </summary>
    [Fact]
    public void Parse_SemverDirective_PopulatesDeclaredSemanticsVersion()
    {
        var source = $$"""
            §M{Test}
              §SEMVER{{{SemanticsVersion.VersionString}}}
              §F{Main:pub} () -> void
                §E{}
                §R
            """;

        AssertRetiredMarker(source, "§SEMVER");
        AssertRetiredMarker($"module Test\n  §SEMVER{{{SemanticsVersion.VersionString}}}", "§SEMVER");
        var module = ParseNative("module Test\n  func Main public () → void\n    !\n    return");
        Assert.Null(module.DeclaredSemanticsVersion);
        Assert.Single(module.Functions);
        var diagnostics = new DiagnosticBag();
        Assert.True(SemanticsVersion.ReportDeclaredVersion(diagnostics, default, SemanticsVersion.VersionString));
        Assert.Empty(diagnostics.Errors);
    }

    /// <summary>No native source-version directive exists; round-trip must not reintroduce the retired tag.</summary>
    [Fact]
    public void CalorEmitter_RoundTrips_SemverDirective()
    {
        var source = $$"""
            §M{Test}
              §SEMVER{{{SemanticsVersion.VersionString}}}
              §F{Main:pub} () -> void
                §E{}
                §R
            """;

        AssertRetiredMarker(source, "§SEMVER");
        AssertRetiredMarker($"module Test\n  §SEMVER{{{SemanticsVersion.VersionString}}}", "§SEMVER");
        var module = ParseNative("module Test\n  func Main public () → void\n    !\n    return");

        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain("§SEMVER", emitted);

        var reparsed = ParseNative(emitted);
        Assert.Null(reparsed.DeclaredSemanticsVersion);
        var function = Assert.Single(reparsed.Functions);
        Assert.Equal("Main", function.Name);
        Assert.Empty(function.Effects!.Effects);
        Assert.Null(Assert.IsType<ReturnStatementNode>(Assert.Single(function.Body)).Expression);
        var result = Program.Compile(emitted);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
        Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
    }
}
