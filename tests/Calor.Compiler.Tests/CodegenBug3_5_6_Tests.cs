using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for codegen bugs #3 (class inheritance lost), #5 (string interpolation dotted access),
/// and #6 (static class not emitted).
/// </summary>
public class CodegenBug3_5_6_Tests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Bug 6: static class not emitted

    [Fact]
    public void Bug6_Parser_StaticModifier_SetsIsStatic()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Helper:static}
§/CL{c1}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.True(cls.IsStatic, "IsStatic should be true");
        Assert.False(cls.IsAbstract);
        Assert.False(cls.IsSealed);
    }

    [Fact]
    public void Bug6_ParseAndEmit_StaticClass_EmitsStaticKeyword()
    {
        var source = @"
§M{m1:Test}
§CL{c1:Helper:static}
§/CL{c1}
§/M{m1}
";
        var result = ParseAndEmit(source);
        Assert.Contains("public static class Helper", result);
    }

    [Fact]
    public void Bug6_CSharpRoundtrip_StaticClass()
    {
        var csharp = """
            public static class StringExtensions
            {
            }
            """;
        var convResult = _converter.Convert(csharp);
        Assert.True(convResult.Success, GetErrorMessage(convResult));
        var cls = Assert.Single(convResult.Ast!.Classes);
        Assert.True(cls.IsStatic, "Converter should set IsStatic");

        // Roundtrip: Calor → C#
        var compilationResult = Program.Compile(convResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip parse failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("static class StringExtensions", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Bug6_Parser_PartialModifier_SetsIsPartial()
    {
        var source = @"
§M{m1:Test}
§CL{c1:MyClass:partial}
§/CL{c1}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.True(cls.IsPartial, "IsPartial should be true");
    }

    #endregion

    #region Bug 3: Class inheritance lost in roundtrip

    [Fact]
    public void Bug3_Parser_FourPositionals_ParsesBaseClassAndModifiers()
    {
        // §CL{id:name:BaseClass:modifiers}
        var source = @"
§M{m1:Test}
§CL{c1:Child:Parent:sealed}
§/CL{c1}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.Equal("Parent", cls.BaseClass);
        Assert.True(cls.IsSealed, "IsSealed should be true");
    }

    [Fact]
    public void Bug3_Parser_ThreePositionals_ModifiersOnly_BackwardCompat()
    {
        // §CL{id:name:abs} — original 3-positional format
        var source = @"
§M{m1:Test}
§CL{c1:Shape:abs}
§/CL{c1}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.True(cls.IsAbstract, "IsAbstract should be true");
        Assert.Null(cls.BaseClass);
    }

    [Fact]
    public void Bug3_ExtTag_StillWorks()
    {
        // §EXT{Parent} as child tag
        var source = @"
§M{m1:Test}
§CL{c1:Child}
§EXT{Parent}
§/CL{c1}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.Equal("Parent", cls.BaseClass);
    }

    [Fact]
    public void Bug3_CalorEmitter_EmitsExtTag_NotPositional()
    {
        var csharp = """
            public class Child : Parent
            {
            }
            """;
        var convResult = _converter.Convert(csharp);
        Assert.True(convResult.Success, GetErrorMessage(convResult));

        var calor = convResult.CalorSource!;
        // Should contain §EXT{Parent} as a child tag
        Assert.Contains("§EXT{Parent}", calor);
        // The §CL line should NOT have Parent as a positional
        // (it should be §CL{...:Child} or §CL{...:Child:modifiers}, not §CL{...:Child:Parent:...})
        var clLine = calor.Split('\n').First(l => l.Contains("§CL{"));
        Assert.DoesNotContain(":Parent", clLine);
    }

    [Fact]
    public void Bug3_CSharpRoundtrip_ClassWithBaseClass()
    {
        var csharp = """
            public class MyAttribute : Attribute
            {
            }
            """;
        var convResult = _converter.Convert(csharp);
        Assert.True(convResult.Success, GetErrorMessage(convResult));

        // Roundtrip: Calor → C#
        var compilationResult = Program.Compile(convResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip parse failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains(": Attribute", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Bug3_CSharpRoundtrip_SealedClassWithBaseClass()
    {
        var csharp = """
            public sealed class ConcreteService : BaseService
            {
            }
            """;
        var convResult = _converter.Convert(csharp);
        Assert.True(convResult.Success, GetErrorMessage(convResult));

        var compilationResult = Program.Compile(convResult.CalorSource!);
        Assert.False(compilationResult.HasErrors,
            "Roundtrip parse failed:\n" +
            string.Join("\n", compilationResult.Diagnostics.Select(d => d.Message)));
        Assert.Contains("sealed class ConcreteService", compilationResult.GeneratedCode);
        Assert.Contains(": BaseService", compilationResult.GeneratedCode);
    }

    [Fact]
    public void Bug3_FourPositionals_VisibilityInPos2_IgnoredCorrectly()
    {
        // §CL{c0:Foo:pub:abs} — pos2 is visibility keyword, should be ignored; pos3 is modifiers
        var source = @"
§M{m1:Test}
§CL{c0:Foo:pub:abs}
§/CL{c0}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.True(cls.IsAbstract, "IsAbstract should be true from pos3");
        Assert.Null(cls.BaseClass); // Visibility keyword 'pub' in pos2 should not become base class
    }

    [Fact]
    public void Bug3_FourPositionals_BaseClassInPos2_ParsedCorrectly()
    {
        // §CL{c0:Foo:MyBase:sealed} — pos2 is base class, pos3 is modifiers
        var source = @"
§M{m1:Test}
§CL{c0:Foo:MyBase:sealed}
§/CL{c0}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.Equal("MyBase", cls.BaseClass);
        Assert.True(cls.IsSealed);
    }

    [Fact]
    public void Bug3_ThreePositionals_SpaceSeparatedModifiers()
    {
        // §CL{c1:Base:abs seal} — space-separated modifiers in 3-positional format
        var source = @"
§M{m1:Test}
§CL{c1:Base:abs seal}
§/CL{c1}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.True(cls.IsAbstract);
        Assert.True(cls.IsSealed);
        Assert.Null(cls.BaseClass);
    }

    [Fact]
    public void Bug3_ThreePositionals_BaseClassNamedLikeKeyword_TreatedAsModifier()
    {
        // Known limitation: a 3-positional where pos2 happens to be a keyword name
        // (e.g., a class named "Partial") will be misinterpreted as a modifier.
        // This only affects legacy Calor files; the new CalorEmitter uses §EXT tags,
        // and old files with base classes always used 4 positionals.
        var source = @"
§M{m1:Test}
§CL{c1:Foo:partial}
§/CL{c1}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        // "partial" is a known modifier keyword, so it's treated as a modifier, not a base class
        Assert.True(cls.IsPartial);
        Assert.Null(cls.BaseClass);
    }

    [Fact]
    public void Bug3_ThreePositionals_NonKeywordPos2_TreatedAsBaseClass()
    {
        // §CL{c1:Child:ParentWidget} — "ParentWidget" is not a known modifier → treated as base class
        var source = @"
§M{m1:Test}
§CL{c1:Child:ParentWidget}
§/CL{c1}
§/M{m1}
";
        var module = ParseModule(source);
        var cls = Assert.Single(module.Classes);
        Assert.Equal("ParentWidget", cls.BaseClass);
        Assert.False(cls.IsAbstract);
        Assert.False(cls.IsSealed);
    }

    #endregion

    #region Bug 5: String interpolation drops dotted access

    [Fact]
    public void Bug5_DottedAccess_InterpolatedCorrectly()
    {
        var source = """
            §M{m1:Test}
            §F{f001:Greet:pub}
              §O{str}
              §B{~result:str} STR:"Hello ${p.Name}"
            §/F{f001}
            §/M{m1}
            """;

        var result = ParseAndEmit(source);
        Assert.Contains("$\"Hello {p.Name}\"", result);
    }

    [Fact]
    public void Bug5_MultiLevelDottedAccess()
    {
        var source = """
            §M{m1:Test}
            §F{f001:Get:pub}
              §O{str}
              §B{~result:str} STR:"${a.B.C}"
            §/F{f001}
            §/M{m1}
            """;

        var result = ParseAndEmit(source);
        Assert.Contains("$\"{a.B.C}\"", result);
    }

    [Fact]
    public void Bug5_NullConditionalAccess()
    {
        var source = """
            §M{m1:Test}
            §F{f001:Get:pub}
              §O{str}
              §B{~result:str} STR:"${x?.Y}"
            §/F{f001}
            §/M{m1}
            """;

        var result = ParseAndEmit(source);
        Assert.Contains("$\"{x?.Y}\"", result);
    }

    [Fact]
    public void Bug5_SimpleInterpolation_BackwardCompat()
    {
        var source = """
            §M{m1:Test}
            §F{f001:Get:pub}
              §O{str}
              §B{~result:str} STR:"Hello ${name}"
            §/F{f001}
            §/M{m1}
            """;

        var result = ParseAndEmit(source);
        Assert.Contains("$\"Hello {name}\"", result);
    }

    #endregion

    #region Helpers

    private static Ast.ModuleNode ParseModule(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        return module;
    }

    private static string ParseAndEmit(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();

        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

    private static string GetErrorMessage(ConversionResult result)
    {
        if (result.Success) return string.Empty;
        return string.Join("\n", result.Issues.Select(i => i.ToString()));
    }

    #endregion
}
