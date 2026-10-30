using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
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
        var source = $"§M{{m1:Modifiers}}\n  §CL{{c1:Widget:{alias}}}";
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnknownVisibility
            && diagnostic.Message.Contains($"'{canonical}'"));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", false)]
    [InlineData("False", true)]
    [InlineData("TRUE", true)]
    public void ConfigureAwait_RequiresExactBooleanSpelling(string value, bool hasError)
    {
        var source = $"§M{{m1:Modifiers}}\n  §AF{{f1:Run:pub}} () -> i32\n    §R §AWAIT{{{value}}} §C{{GetAsync}} §/C";
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Equal(hasError, diagnostics.HasErrors);
        if (hasError)
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Message.Contains("exact-case 'true' or 'false'"));
    }

    [Theory]
    [InlineData("class", TypeConstraintKind.Class)]
    [InlineData("CLASS", TypeConstraintKind.TypeName)]
    public void ConstraintKeywords_AreNotCaseFolded(string constraint, TypeConstraintKind expected)
    {
        var source = $"§M{{m1:Modifiers}}\n  §F{{f1:Run:pub}}<T> () -> void\n    §WHERE T : {constraint}";
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(expected, module.Functions.Single().TypeParameters.Single().Constraints.Single().Kind);
    }

    [Theory]
    [InlineData("(i32:x:REF)", "")]
    [InlineData("", "    §I{i32:x:REF}")]
    public void ParameterModifierCaseVariants_ReportAnError(string signature, string taggedParameter)
    {
        var source = $"§M{{m1:Modifiers}}\n  §F{{f1:Run:pub}} {signature}\n{taggedParameter}\n    §R";
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InvalidModifier
            && diagnostic.Message.Contains("'ref'"));
    }

    [Theory]
    [InlineData("MT")]
    [InlineData("AMT")]
    public void CompactMethodWithoutModifiers_DoesNotTreatDefaultVisibilityAsAModifier(string marker)
    {
        var source = $"§M{{m1:Modifiers}}\n  §CL{{c1:Widget:pub}}\n    §{marker}{{Run:pub}} () -> void";
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Errors));
        Assert.Equal(MethodModifiers.None, module.Classes.Single().Methods.Single().Modifiers);
    }

    [Theory]
    [InlineData("STATIC", "static")]
    [InlineData("STAT", "stat")]
    [InlineData("VIRTUAL", "virtual")]
    [InlineData("OV", "ov")]
    [InlineData("Readonly", "readonly")]
    [InlineData("Required", "required")]
    [InlineData("Unsafe", "unsafe")]
    [InlineData("VOL", "vol")]
    public void MemberModifierCaseVariants_ReportCanonicalSpelling(string modifier, string canonical)
    {
        var source = $$"""
            §M{m1:Modifiers}
              §CL{c1:Widget:pub}
                §MT{mt1:Run:pub:{{modifier}}} () -> void
            """;
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InvalidModifier
            && diagnostic.Message.Contains($"'{canonical}'"));
    }

    [Theory]
    [InlineData("STATIC", "static")]
    [InlineData("ABS", "abs")]
    [InlineData("Struct", "struct")]
    [InlineData("Readonly", "readonly")]
    public void ClassModifierCaseVariants_ReportCanonicalSpelling(string modifier, string canonical)
    {
        var source = $"§M{{m1:Modifiers}}\n  §CL{{c1:Widget:pub:{modifier}}}";
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.InvalidModifier
            && diagnostic.Message.Contains($"'{canonical}'"));
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
        Parse($"§M{{m001:Test}}\n  §F{{f001:Test:pub}} () -> i32\n    §R ({word} 1 2)",
            out var diagnostics);

        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.InvalidLispExpression &&
            diagnostic.Message.Contains($"Use '{symbol}'"));
        Assert.Null(BinaryOperatorExtensions.FromString(word));
        Assert.Null(UnaryOperatorExtensions.FromString(word));
    }

    [Theory]
    [InlineData("LEN", "\"x\"")]
    [InlineData("Cast", "i32 1")]
    [InlineData("IS-LETTER", "'A'")]
    [InlineData("SB-NEW", "")]
    [InlineData("INC", "1")]
    public void Parse_OperatorNames_AreExactCase(string name, string arguments)
    {
        Parse($"§M{{m001:Test}}\n  §F{{f001:Test:pub}} () -> i32\n    §R ({name} {arguments})",
            out var diagnostics);

        Assert.True(diagnostics.HasErrors);
    }

    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    [Fact]
    public void Parse_MinimalModule_ReturnsModuleNode()
    {
        var source = """
            §M{m001:Test}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("m001", module.Id);
        Assert.Equal("Test", module.Name);
        Assert.Empty(module.Functions);
    }

    [Fact]
    public void Parse_ModuleWithFunction_ReturnsFunctionNode()
    {
        var source = """
            §M{m001:Test}
              §F{f001:MyFunc:pub}
                  §O{void}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Equal("f001", func.Id);
        Assert.Equal("MyFunc", func.Name);
        Assert.Equal(Visibility.Public, func.Visibility);
    }

    [Fact]
    public void Parse_FunctionWithParameters_ReturnsParameterNodes()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Add:pub}
                  §I{i32:a}
                  §I{i32:b}
                  §O{i32}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Equal(2, func.Parameters.Count);
        Assert.Equal("a", func.Parameters[0].Name);
        Assert.Equal("INT", func.Parameters[0].TypeName);
        Assert.Equal("b", func.Parameters[1].Name);
    }

    [Fact]
    public void Parse_UnknownPrefixBangEffect_PreservesDiagnosticText()
    {
        foreach (var effects in new[] { "!e", "alloc, !e" })
        {
            var source = $$"""
                §M{m001:Test}
                  §F{f001:Main:pub} () -> void
                    §E{{{effects}}}
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

    [Fact]
    public void Parse_FallibleCall_PreservesTerminalBang()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub} () -> void
                §C{Risky!} §/C
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.IsType<CallStatementNode>(Assert.Single(module.Functions[0].Body));
        Assert.Equal("Risky", call.Target);
        Assert.True(call.Fallible);
    }

    [Fact]
    public void Parse_FunctionWithCallStatement_ReturnsCallNode()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §O{void}
                  §E{cw}
                  §C{Console.WriteLine}
                    §A "Hello"
                  §/C
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        var func = module.Functions[0];
        Assert.Single(func.Body);

        var call = func.Body[0] as CallStatementNode;
        Assert.NotNull(call);
        Assert.Equal("Console.WriteLine", call.Target);
        Assert.False(call.Fallible);
        Assert.Single(call.Arguments);

        var arg = call.Arguments[0] as StringLiteralNode;
        Assert.NotNull(arg);
        Assert.Equal("Hello", arg.Value);
    }

    [Fact]
    public void Parse_FunctionWithEffects_ReturnsEffectsNode()
    {
        var source = """
            §M{m001:Test}
              §F{f001:Main:pub}
                  §O{void}
                  §E{cw}
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
        // §/M is removed closer-form (Phase 4d): rejected with Calor0830.
        // The obsolete id-mismatch diagnostic no longer fires for structural closers.
        var source = """
            §M{m001:Test}
            §/M{m002}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.LegacyCloserForm);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.MismatchedId);
    }

    [Fact]
    public void Parse_LegacyFuncCloser_ReportsError()
    {
        // §/F is removed closer-form (Phase 4d): rejected with Calor0830, not MismatchedId.
        var source = """
            §M{m001:Test}
            §F{f001:Main:pub}
              §O{void}
            §/F{f999}
            §/M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.LegacyCloserForm);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.MismatchedId);
    }

    [Fact]
    public void Parse_MissingRequiredAttribute_ReportsError()
    {
        var source = """
            §M{m001}
            """;

        var module = Parse(source, out var diagnostics);

        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.MissingRequiredAttribute);
    }

    [Fact]
    public void Parse_ReturnStatement_ReturnsReturnNode()
    {
        var source = """
            §M{m001:Test}
              §F{f001:GetValue:pub}
                  §O{i32}
                  §R 42
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
            §M{m001:Hello}
              §F{f001:Main:pub}
                  §O{void}
                  §E{cw}
                  §C{Console.WriteLine}
                    §A "Hello from Calor!"
                  §/C
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("m001", module.Id);
        Assert.Equal("Hello", module.Name);
        Assert.Single(module.Functions);

        var func = module.Functions[0];
        Assert.Equal("Main", func.Name);
        Assert.Equal(Visibility.Public, func.Visibility);
        Assert.Single(func.Body);

        var call = func.Body[0] as CallStatementNode;
        Assert.NotNull(call);
        Assert.Equal("Console.WriteLine", call.Target);
        Assert.Single(call.Arguments);

        var arg = call.Arguments[0] as StringLiteralNode;
        Assert.NotNull(arg);
        Assert.Equal("Hello from Calor!", arg.Value);
    }

    /// <summary>
    /// §SEMVER{MAJOR.MINOR.PATCH} is a module-level directive: the lexer captures the
    /// version text verbatim and the parser stores it on the module (#1084 item 1).
    /// </summary>
    [Fact]
    public void Parse_SemverDirective_PopulatesDeclaredSemanticsVersion()
    {
        var source = $$"""
            §M{m001:Test}
              §SEMVER{{{SemanticsVersion.VersionString}}}
              §F{f001:Main:pub} () -> void
                §E{}
                §R
            """;

        var module = Parse(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(SemanticsVersion.VersionString, module.DeclaredSemanticsVersion);
        Assert.Single(module.Functions);
    }

    /// <summary>Calor→Calor emission preserves the directive, so formatting/round-trip does not drop it.</summary>
    [Fact]
    public void CalorEmitter_RoundTrips_SemverDirective()
    {
        var source = $$"""
            §M{m001:Test}
              §SEMVER{{{SemanticsVersion.VersionString}}}
              §F{f001:Main:pub} () -> void
                §E{}
                §R
            """;

        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors);

        var emitted = new Migration.CalorEmitter().Emit(module);
        Assert.Contains($"§SEMVER{{{SemanticsVersion.VersionString}}}", emitted);

        var reparsed = Parse(emitted, out var reparseDiagnostics);
        Assert.False(reparseDiagnostics.HasErrors, string.Join("; ", reparseDiagnostics.Errors.Select(e => e.Message)));
        Assert.Equal(SemanticsVersion.VersionString, reparsed.DeclaredSemanticsVersion);
    }
}
