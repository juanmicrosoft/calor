using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Tests for fixes discovered during the C# to Calor conversion campaign.
/// Each test corresponds to a GitHub issue from the campaign.
/// </summary>
public class ConversionCampaignFixTests
{
    #region Helpers

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

    private static DiagnosticBag ParseWithDiagnostics(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAll();

        var parser = new Parser(tokens, diagnostics);
        parser.Parse();

        return diagnostics;
    }

    private static ModuleNode ParseCalor(string source)
    {
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        Assert.Empty(diag.Errors);
        var parser = new Parser(tokens, diag);
        var ast = parser.Parse();
        Assert.Empty(diag.Errors);
        return ast;
    }

    private readonly CSharpToCalorConverter _converter = new();

    #endregion

    #region Issue 289: Emit default: instead of case _: in switch

    [Fact]
    public void Emit_WildcardMatchCase_EmitsDefault()
    {
        var source = @"
§M{m001:Test}
§F{f001:MatchTest:pub}
§I{i32:x}
§O{str}
§W{m1} x
§K 1
§R ""one""
§K _
§R ""other""
§/W{m1}
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("default:", csharp);
        Assert.DoesNotContain("case _:", csharp);
    }

    [Fact]
    public void Emit_MatchWithMultipleCasesAndWildcard_OnlyWildcardIsDefault()
    {
        var source = @"
§M{m001:Test}
§F{f001:MatchTest:pub}
§I{i32:x}
§O{str}
§W{m1} x
§K 1
§R ""one""
§K 2
§R ""two""
§K _
§R ""other""
§/W{m1}
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("case 1:", csharp);
        Assert.Contains("case 2:", csharp);
        Assert.Contains("default:", csharp);
        Assert.DoesNotContain("case _:", csharp);
    }

    #endregion

    #region Issue 290: Read-only properties should emit { get; } not { get; set; }

    [Fact]
    public void Emit_ReadOnlyAutoProperty_EmitsGetOnly()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Foo:pub}
§PROP{p001:Name:str:pub}
§GET §/GET
§/PROP{p001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("{ get; }", csharp);
        Assert.DoesNotContain("get; set;", csharp);
    }

    [Fact]
    public void Emit_ReadWriteAutoProperty_EmitsGetSet()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Foo:pub}
§PROP{p001:Name:str:pub}
§GET §/GET
§SET §/SET
§/PROP{p001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("get; set;", csharp);
    }

    #endregion

    #region Issue 291: Remove @ prefix from this and double/float keywords

    [Fact]
    public void Emit_ThisMemberAccess_NoAtPrefix()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Account:pub}
§FLD{str:_name:priv}
§MT{m001:SetName:pub}
§I{str:name}
§ASSIGN this._name name
§/MT{m001}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("this._name", csharp);
        Assert.DoesNotContain("@this", csharp);
    }

    [Fact]
    public void Emit_ThisInConstructor_NoAtPrefix()
    {
        var source = @"
§M{m001:Test}
§CL{c001:Account:pub}
§FLD{str:_id:priv}
§CTOR{ctor1:pub}
§I{str:id}
§ASSIGN this._id id
§/CTOR{ctor1}
§/CL{c001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("this._id", csharp);
        Assert.DoesNotContain("@this", csharp);
    }

    #endregion

    #region Issue 292: Preserve namespace dots in type names

    [Fact]
    public void Emit_NewWithNamespacedType_PreservesDots()
    {
        var source = @"
§M{m001:Test}
§F{f001:BuildReport:pub}
§O{str}
§E{cw}
§B{sb} §NEW{System.Text.StringBuilder} §/NEW
§R (str sb)
§/F{f001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("new System.Text.StringBuilder()", csharp);
        Assert.DoesNotContain("System_Text_StringBuilder", csharp);
    }

    #endregion

    #region Issue 294: Support §PROP inside §IFACE for interface properties

    [Fact]
    public void Emit_InterfaceWithProperty_EmitsPropertyNotMethod()
    {
        var source = @"
§M{m001:Test}
§IFACE{i001:IOrder}
§PROP{p001:Purchased:datetime:pub}
§GET §/GET
§/PROP{p001}
§/IFACE{i001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("interface IOrder", csharp);
        Assert.Contains("DateTime Purchased", csharp);
        Assert.Contains("get;", csharp);
        Assert.DoesNotContain("DateTime Purchased()", csharp);
    }

    [Fact]
    public void Emit_InterfaceWithPropertyAndMethod_EmitsBoth()
    {
        var source = @"
§M{m001:Test}
§IFACE{i001:IOrder}
§PROP{p001:Cost:f64:pub}
§GET §/GET
§/PROP{p001}
§MT{m001:GetDescription}
§O{str}
§/MT{m001}
§/IFACE{i001}
§/M{m001}
";
        var csharp = ParseAndEmit(source);
        Assert.Contains("interface IOrder", csharp);
        Assert.Contains("double Cost", csharp);
        Assert.Contains("get;", csharp);
        Assert.Contains("string GetDescription()", csharp);
    }

    #endregion

    #region Issue 300: Convert postfix/prefix increment to compound assignment

    [Fact]
    public void Convert_PostfixIncrement_ProducesCompoundAssignment()
    {
        var result = _converter.Convert(@"
public class Test
{
    public void Count()
    {
        int i = 0;
        i++;
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("§ERR", result.CalorSource);
        Assert.DoesNotContain("postfix", result.CalorSource?.ToLower() ?? "");
    }

    [Fact]
    public void Convert_PostfixDecrement_ProducesCompoundAssignment()
    {
        var result = _converter.Convert(@"
public class Test
{
    public void CountDown()
    {
        int i = 10;
        i--;
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    [Fact]
    public void Convert_PrefixIncrement_ProducesCompoundAssignment()
    {
        var result = _converter.Convert(@"
public class Test
{
    public void Count()
    {
        int i = 0;
        ++i;
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotContain("§ERR", result.CalorSource);
    }

    #endregion

    #region Issue 301: Convert nameof() to string literal and string.Empty to ""

    [Fact]
    public void Convert_Nameof_ProducesStringLiteral()
    {
        var result = _converter.Convert(@"
public class Test
{
    public void Check(string name)
    {
        var x = nameof(name);
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(@"""name""", result.CalorSource);
        Assert.DoesNotContain("nameof", result.CalorSource);
    }

    [Fact]
    public void Convert_StringEmpty_ProducesEmptyStringLiteral()
    {
        var result = _converter.Convert(@"
public class Test
{
    public string GetDefault()
    {
        return string.Empty;
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(@"""""", result.CalorSource);
    }

    #endregion

    #region Issue 302: Emit §MT instead of §SIG for interface method signatures

    [Fact]
    public void Convert_InterfaceMethod_EmitsMTNotSIG()
    {
        var result = _converter.Convert(@"
public interface IAnimal
{
    string Speak();
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains("§MT{", result.CalorSource);
        Assert.Contains("§/MT{", result.CalorSource);
        Assert.DoesNotContain("§SIG", result.CalorSource);
    }

    [Fact]
    public void Convert_InterfaceMethodWithParams_EmitsMTWithParams()
    {
        var result = _converter.Convert(@"
public interface ICalculator
{
    int Add(int a, int b);
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains("§MT{", result.CalorSource);
        Assert.Contains("§I{", result.CalorSource);
        Assert.Contains("§O{", result.CalorSource);
        Assert.DoesNotContain("§SIG", result.CalorSource);
    }

    #endregion

    #region Issue 303: Convert §DICT/§LIST to §FLD inside class bodies

    [Fact]
    public void Convert_DictionaryField_EmitsFLDNotDICT()
    {
        var result = _converter.Convert(@"
public class Cache
{
    private Dictionary<string, int> _lookup = new();
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains("§FLD{", result.CalorSource);
        Assert.DoesNotContain("§DICT{", result.CalorSource);
        Assert.DoesNotContain("§/DICT{", result.CalorSource);
    }

    [Fact]
    public void Convert_ListField_EmitsFLDNotLIST()
    {
        var result = _converter.Convert(@"
public class Service
{
    private List<string> _items = new();
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains("§FLD{", result.CalorSource);
        Assert.DoesNotContain("§LIST{", result.CalorSource);
        Assert.DoesNotContain("§/LIST{", result.CalorSource);
    }

    [Fact]
    public void Convert_ComplexDictionaryField_EmitsFLDNotDICT()
    {
        var result = _converter.Convert(@"
public class Cache
{
    private readonly Dictionary<string, List<int>> _cache = new Dictionary<string, List<int>>();
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains("§FLD{", result.CalorSource);
        Assert.DoesNotContain("§DICT{", result.CalorSource);
        Assert.DoesNotContain("§/DICT{", result.CalorSource);
    }

    #endregion

    #region Issue 304: Convert tuple deconstruction to individual §ASSIGN statements

    [Fact]
    public void Convert_TupleDeconstruction_EmitsIndividualAssignments()
    {
        var result = _converter.Convert(@"
public class Example
{
    private int _a;
    private int _b;

    public void SetValues(int x, int y)
    {
        (_a, _b) = (x, y);
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var source = result.CalorSource ?? "";
        Assert.DoesNotContain("§ERR", source);
        Assert.Contains("§ASSIGN", source);
    }

    [Fact]
    public void Convert_TupleDeconstruction_ThreeElements()
    {
        var result = _converter.Convert(@"
public class Example
{
    private int _a;
    private int _b;
    private int _c;

    public void SetValues(int x, int y, int z)
    {
        (_a, _b, _c) = (x, y, z);
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var source = result.CalorSource ?? "";
        Assert.DoesNotContain("§ERR", source);
    }

    #endregion

    #region Issue 309: Handle @-prefixed C# parameter names

    [Fact]
    public void Convert_AtPrefixedParam_StripsAtPrefix()
    {
        var result = _converter.Convert(@"
public class Example
{
    public void Process(string @class, int @event)
    {
        var x = @class;
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var source = result.CalorSource ?? "";
        Assert.Contains("§I{str:class}", source);
        Assert.Contains("§I{i32:event}", source);
        Assert.DoesNotContain("@class", source);
        Assert.DoesNotContain("@event", source);
    }

    [Fact]
    public void Convert_AtPrefixedLocalVariable_StripsAtPrefix()
    {
        var result = _converter.Convert(@"
public class Example
{
    public void Process()
    {
        var @object = 42;
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var source = result.CalorSource ?? "";
        Assert.Contains("object", source);
        Assert.DoesNotContain("@object", source);
    }

    #endregion

    #region Issue 310: Async console operations in known effects

    [Fact]
    public void BuiltInEffects_TextWriterWriteLineAsync_IsKnown()
    {
        Assert.True(BuiltInEffects.IsKnown("System.IO.TextWriter::WriteLineAsync(System.String)"));
    }

    [Fact]
    public void BuiltInEffects_StreamReaderReadLineAsync_IsKnown()
    {
        Assert.True(BuiltInEffects.IsKnown("System.IO.StreamReader::ReadLineAsync()"));
    }

    [Fact]
    public void BuiltInEffects_TextWriterFlushAsync_IsKnown()
    {
        Assert.True(BuiltInEffects.IsKnown("System.IO.TextWriter::FlushAsync()"));
    }

    #endregion

    #region Issue 311: Math functions as known pure methods

    [Fact]
    public void BuiltInEffects_MathFloor_IsPure()
    {
        Assert.True(BuiltInEffects.IsKnownPure("System.Math::Floor(System.Double)"));
    }

    [Fact]
    public void BuiltInEffects_MathClamp_IsPure()
    {
        Assert.True(BuiltInEffects.IsKnownPure("System.Math::Clamp(System.Int32,System.Int32,System.Int32)"));
    }

    [Fact]
    public void BuiltInEffects_MathSin_IsPure()
    {
        Assert.True(BuiltInEffects.IsKnownPure("System.Math::Sin(System.Double)"));
    }

    [Fact]
    public void BuiltInEffects_MathRound_IsPure()
    {
        Assert.True(BuiltInEffects.IsKnownPure("System.Math::Round(System.Double,System.Int32)"));
    }

    [Fact]
    public void BuiltInEffects_MathLog_IsPure()
    {
        Assert.True(BuiltInEffects.IsKnownPure("System.Math::Log(System.Double)"));
    }

    #endregion

    #region Issue 312: Single quotes and line comments in lexer

    [Fact]
    public void Lexer_LineComment_SkipsContent()
    {
        var source = "§M{m001:Test}\n// This is a comment with apostrophe: it's fine\n§/M{m001}";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        Assert.Empty(diag.Errors);
    }

    [Fact]
    public void Lexer_LineCommentAtEndOfFile_SkipsContent()
    {
        var source = "§M{m001:Test}\n§/M{m001}\n// trailing comment";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        Assert.Empty(diag.Errors);
    }

    [Fact]
    public void Lexer_CharLiteral_DoesNotCrash()
    {
        var source = "§M{m001:Test}\n§F{f001:hello}\n§O{str}\n§R 'x'\n§/F{f001}\n§/M{m001}";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        Assert.NotNull(tokens);
        Assert.True(tokens.Count > 0);
    }

    [Fact]
    public void Lexer_SlashNotFollowedBySlash_IsSlashToken()
    {
        var source = "§M{m001:Test}\n§F{f001:div}\n§O{i32}\n§R (/ 10 2)\n§/F{f001}\n§/M{m001}";
        var diag = new DiagnosticBag();
        var lexer = new Lexer(source, diag);
        var tokens = lexer.TokenizeAll();
        Assert.Contains(tokens, t => t.Kind == TokenKind.Slash);
    }

    #endregion

    #region Issue 314: LINQ extension method effect recognition

    [Fact]
    public void LinqMethods_DoNotTriggerCalor0411_Errors()
    {
        var source = @"
§M{m001:TestModule}
§F{f001:process}
  §E{cw}
  §I{List<i32>:items}
  §O{i32}
  §B{filtered} §C{items.Where} §A §LAM{lam001:x:i32} (> x 5) §/LAM{lam001} §/C
  §B{count} §C{filtered.Count} §/C
  §P count
  §R count
§/F{f001}
§/M{m001}";

        var ast = ParseCalor(source);

        var diag = new DiagnosticBag();
        var pass = new EffectEnforcementPass(diag);
        pass.Enforce(ast);

        var errors = diag.Errors.Where(d =>
            d.Code == DiagnosticCode.UnknownExternalCall).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void LinqToList_DoesNotTriggerCalor0411_Errors()
    {
        var source = @"
§M{m001:TestModule}
§F{f001:sortItems}
  §I{List<i32>:items}
  §O{List<i32>}
  §B{sorted} §C{items.OrderBy} §A §LAM{lam001:x:i32} x §/LAM{lam001} §/C
  §R §C{sorted.ToList} §/C
§/F{f001}
§/M{m001}";

        var ast = ParseCalor(source);

        var diag = new DiagnosticBag();
        var pass = new EffectEnforcementPass(diag);
        pass.Enforce(ast);

        var errors = diag.Errors.Where(d =>
            d.Code == DiagnosticCode.UnknownExternalCall).ToList();
        Assert.Empty(errors);
    }

    #endregion

    #region Issue 365: ValueTask<T> silently downgraded to Task<T>

    [Fact]
    public void Convert_ValueTaskReturnType_PreservesValueTask()
    {
        var csharp = @"
using System.Threading.Tasks;
class Test
{
    async ValueTask<int> GetValue()
    {
        return 42;
    }
}";
        var result = _converter.Convert(csharp, "test");
        var calor = result.CalorSource;

        // Should preserve ValueTask, not downgrade to Task
        Assert.DoesNotContain("Task<i32>", calor);
    }

    #endregion
}
