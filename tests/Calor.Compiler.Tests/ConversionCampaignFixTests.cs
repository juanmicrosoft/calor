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

    /// <summary>
    /// Compiles Calor source back to C# (for round-trip testing).
    /// Returns null if there are parse errors.
    /// </summary>
    private static string? Compile(string calorSource)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(calorSource, diagnostics);
        var tokens = lexer.TokenizeAll();
        if (diagnostics.HasErrors) return null;

        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        if (diagnostics.HasErrors) return null;

        var emitter = new CSharpEmitter();
        return emitter.Emit(module);
    }

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

    #region Issue 339: Null-coalescing operator ?? silently converted to arithmetic +

    [Fact]
    public void Convert_NullCoalescing_EmitsConditionalNotArithmetic()
    {
        var csharp = @"
class Test
{
    string GetValue(string? input)
    {
        return input ?? ""default"";
    }
}";
        var result = _converter.Convert(csharp, "test");
        var calor = result.CalorSource;

        // Must NOT produce (+ input "default") — that's arithmetic addition
        Assert.DoesNotContain("(+ input", calor);

        // Should produce a conditional: (if (== input null) "default" input)
        Assert.Contains("== input null", calor);
        Assert.Contains("\"default\"", calor);
    }

    [Fact]
    public void Convert_NullCoalescing_RoundTripProducesValidCalor()
    {
        var csharp = @"
class Test
{
    string GetValue(string? input)
    {
        var result = input ?? ""fallback"";
        return result;
    }
}";
        var result = _converter.Convert(csharp, "test");
        var calor = result.CalorSource;

        // The Calor output should not contain any '+' operator for the coalescing
        Assert.DoesNotContain("(+ input", calor);
    }

    #endregion

    #region Issue 350: Null-coalescing assignment ??= not supported

    [Fact]
    public void Convert_NullCoalescingAssignment_EmitsIfNullAssign()
    {
        var csharp = @"
using System.Collections.Generic;
class Test
{
    void Init()
    {
        List<string>? items = null;
        items ??= new List<string>();
    }
}";
        var result = _converter.Convert(csharp, "test");
        var calor = result.CalorSource;

        // Should contain a null check and assignment, not arithmetic
        Assert.DoesNotContain("(+ items", calor);
        // Should have an if-null-assign pattern
        Assert.Contains("== items null", calor);
        Assert.Contains("§ASSIGN", calor);
    }

    #endregion

    #region Issue 341: typeof() expression not converted

    [Fact]
    public void Convert_TypeOfExpression_EmitsTypeOf()
    {
        var csharp = @"
using System;
class Test
{
    Type GetType() => typeof(string);
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.DoesNotContain("§ERR", calor);
        Assert.Contains("(typeof", calor);
    }

    #endregion

    #region Issue 344: lock statement body completely lost

    [Fact]
    public void Convert_LockStatement_PreservesBody()
    {
        var csharp = @"
class Test
{
    private readonly object _lock = new object();
    void DoWork()
    {
        lock (_lock)
        {
            var x = 1;
        }
    }
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        // Body should be preserved (the variable declaration inside lock)
        Assert.Contains("x", calor);
        // Should NOT produce an ERR for lock
        Assert.DoesNotContain("§ERR", calor);
    }

    #endregion

    #region Issue 353: Lambda assignment body dropped

    [Fact]
    public void Convert_LambdaAssignmentBody_EmitsAssign()
    {
        var csharp = @"
using System;
class Test
{
    int _value;
    void SetValue()
    {
        Action<int> setter = x => _value = x;
    }
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.Contains("§ASSIGN", calor);
    }

    #endregion

    #region Issue 356: Expression-bodied constructor assignment

    [Fact]
    public void Convert_ExpressionBodyCtor_EmitsAssignment()
    {
        var csharp = @"
class Test
{
    string _name;
    Test(string name) => _name = name;
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.NotNull(calor);
        Assert.Contains("§ASSIGN", calor);
        Assert.DoesNotContain("§R", calor!.Split('\n').Where(l => l.Contains("_name")).FirstOrDefault() ?? "");
    }

    [Fact]
    public void Convert_ExpressionBodyMethod_WithAssignment_EmitsAssign()
    {
        var csharp = @"
class Test
{
    string _name;
    void SetName(string name) => _name = name;
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.Contains("§ASSIGN", calor);
    }

    #endregion

    #region Issue 365: ValueTask mapped to Task

    [Fact]
    public void Convert_ValueTask_PreservesValueTask()
    {
        var csharp = @"
using System.Threading.Tasks;
class Test
{
    async ValueTask<int> GetValueAsync() => 42;
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        // Should NOT downgrade ValueTask to Task
        Assert.DoesNotContain("Task<", calor);
    }

    #endregion

    #region Issue 372: Empty collection [] emits default

    [Fact]
    public void Convert_EmptyCollectionExpression_EmitsEmptyList()
    {
        var csharp = @"
using System.Collections.Generic;
class Container
{
    public List<string> Items { get; set; } = [];
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.DoesNotContain("default", calor);
        Assert.Contains("§LIST", calor);
    }

    [Fact]
    public void Convert_EmptyCollectionExpressionInMethod_EmitsEmptyList()
    {
        var csharp = @"
using System.Collections.Generic;
class Processor
{
    public List<int> GetEmpty()
    {
        return [];
    }
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.DoesNotContain("default", calor);
        Assert.Contains("§LIST", calor);
    }

    #endregion

    #region Issue 374: PredefinedTypeSyntax (int.MaxValue, string.Empty)

    [Fact]
    public void Convert_IntMaxValue_EmitsLiteral()
    {
        var csharp = @"
class Test
{
    int GetMax() => int.MaxValue;
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.Contains("2147483647", calor);
    }

    [Fact]
    public void Convert_StringEmpty_EmitsEmptyLiteral()
    {
        var csharp = @"
class Test
{
    string GetEmpty() => string.Empty;
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.Contains("\"\"", calor);
    }

    #endregion

    #region Issue 388: Static properties lose static modifier

    [Fact]
    public void Convert_StaticProperty_PreservesStaticModifier()
    {
        var csharp = @"
class Config
{
    public static string DefaultName { get; set; } = ""test"";
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.Contains(":stat", calor);
    }

    [Fact]
    public void Convert_StaticProperty_RoundTrips()
    {
        var csharp = @"
class Config
{
    public static int MaxRetries { get; set; } = 3;
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        Assert.NotNull(calor);
        var emittedCSharp = ParseAndEmit(calor!);
        Assert.Contains("static", emittedCSharp);
    }

    #endregion

    #region Issue 339+: Declaration pattern variable binding (is Type var)

    [Fact]
    public void Convert_DeclarationPattern_BindsVariable()
    {
        var csharp = @"
class Test
{
    string Describe(object value)
    {
        if (value is string s)
        {
            return s;
        }
        return ""unknown"";
    }
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        // Should bind the variable 's' via a cast
        Assert.Contains("s", calor);
        // Should contain a type check
        Assert.Contains("(is value str)", calor);
        // Should contain a bind for the pattern variable
        Assert.Contains("§B", calor);
    }

    [Fact]
    public void Convert_DeclarationPattern_CastsVariable()
    {
        var csharp = @"
class Test
{
    int GetLength(object value)
    {
        if (value is string text)
        {
            return text.Length;
        }
        return 0;
    }
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        // Should have a cast for the pattern variable
        Assert.Contains("cast", calor);
        Assert.Contains("text", calor);
    }

    #endregion

    #region Issue 346+: out var declarations

    [Fact]
    public void Convert_OutVar_PreDeclaresVariable()
    {
        var csharp = @"
class Test
{
    bool Check(string input)
    {
        return int.TryParse(input, out var result);
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result);
        // If conversion crashed, show error details
        if (result.CalorSource == null)
        {
            var issues = result.Issues?.Select(i => i.ToString()) ?? Array.Empty<string>();
            Assert.Fail($"Conversion produced null CalorSource. Issues: {string.Join("; ", issues)}");
        }
        var calor = result.CalorSource!;

        // Should NOT produce ERR for the out var declaration
        Assert.DoesNotContain("§ERR", calor);
        // Should reference 'result'
        Assert.Contains("result", calor);
    }

    [Fact]
    public void Convert_OutVarTyped_PreDeclaresVariable()
    {
        var csharp = @"
class Test
{
    bool Parse(string input)
    {
        return int.TryParse(input, out int result);
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result);
        var calor = result.CalorSource;

        Assert.NotNull(calor);
        Assert.DoesNotContain("§ERR", calor!);
        Assert.Contains("result", calor);
    }

    #endregion

    #region Issue 361+: Method groups as arguments

    [Fact]
    public void Convert_MethodGroupOnPredefinedType_NoERR()
    {
        var csharp = @"
using System.Linq;
class Test
{
    bool AllUpper(string input)
    {
        return input.All(char.IsUpper);
    }
}";
        var result = _converter.Convert(csharp);
        var calor = result.CalorSource;

        // char.IsUpper should not produce ERR (PredefinedTypeSyntax now handled)
        Assert.DoesNotContain("§ERR", calor);
    }

    #endregion

    #region Issue 355: Primary constructor parameters lost

    [Fact]
    public void Convert_PrimaryConstructor_EmitsFields()
    {
        var csharp = @"
public class Service(string name, int retries)
{
    public string GetName() => name;
    public int GetRetries() => retries;
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Primary constructor parameters should appear as fields
        Assert.Contains("name", calor);
        Assert.Contains("retries", calor);
        // Should contain field declarations
        Assert.Contains("§FLD", calor);
    }

    [Fact]
    public void Convert_PrimaryConstructor_WithBase_EmitsFields()
    {
        var csharp = @"
public class BaseService
{
}

public class DerivedService(string connectionString) : BaseService
{
    public string GetConnection() => connectionString;
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        Assert.Contains("connectionString", calor);
        Assert.Contains("§FLD", calor);
    }

    #endregion

    #region Batch 5 — Protected Internal, Unchecked, Default Params, Chained Assignment

    [Fact]
    public void Convert_ProtectedInternalMethod_PreservesCompoundModifier()
    {
        var csharp = @"
public class MyClass
{
    protected internal void DoWork() { }
    protected internal int Value { get; set; }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Verify it converts without error
        Assert.Contains("DoWork", calor);
        Assert.Contains("Value", calor);

        // Compile back to C# and verify protected internal is preserved
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        Assert.Contains("protected internal", compiled!);
    }

    [Fact]
    public void Convert_UncheckedBlock_PreservesBody()
    {
        var csharp = @"
public class MathHelper
{
    public int Overflow(int x)
    {
        unchecked
        {
            int result = x * x;
            return result;
        }
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Body should be preserved
        Assert.Contains("result", calor);
        Assert.Contains("§R", calor);
    }

    [Fact]
    public void Convert_DefaultParameterValues_PreservedInAst()
    {
        var csharp = @"
public class Formatter
{
    public string Format(string text, int width = 80, bool uppercase = false)
    {
        return text;
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        Assert.Contains("text", calor);
        Assert.Contains("width", calor);

        // Verify AST has defaults by checking the module's class methods
        Assert.NotNull(result.Ast);
        var classNode = result.Ast!.Classes.FirstOrDefault();
        Assert.NotNull(classNode);
        var method = classNode!.Methods.FirstOrDefault(m => m.Name == "Format");
        Assert.NotNull(method);
        var widthParam = method!.Parameters.FirstOrDefault(p => p.Name == "width");
        Assert.NotNull(widthParam);
        Assert.NotNull(widthParam!.DefaultValue);
        var uppercaseParam = method.Parameters.FirstOrDefault(p => p.Name == "uppercase");
        Assert.NotNull(uppercaseParam);
        Assert.NotNull(uppercaseParam!.DefaultValue);
    }

    [Fact]
    public void Convert_ChainedAssignment_ProducesStatements()
    {
        var csharp = @"
public class Example
{
    public void Test()
    {
        int a, b;
        a = b = 5;
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should not contain ERR — the chained assignment should convert
        Assert.DoesNotContain("ERR", calor);
        // Should contain assignment for both a and b
        Assert.Contains("§ASSIGN", calor);
    }

    [Fact]
    public void Convert_CheckedBlock_PreservesBody()
    {
        var csharp = @"
public class Calculator
{
    public int Safe(int x, int y)
    {
        checked
        {
            return x + y;
        }
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        Assert.Contains("§R", calor);
    }

    [Fact]
    public void Convert_DefaultParameterStringAndNull_PreservedInAst()
    {
        var csharp = @"
public class Logger
{
    public void Log(string message, string level = ""info"", object? context = null)
    {
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        Assert.Contains("message", calor);
        Assert.Contains("level", calor);

        // Verify AST preserves defaults
        Assert.NotNull(result.Ast);
        var classNode = result.Ast!.Classes.FirstOrDefault();
        Assert.NotNull(classNode);
        var method = classNode!.Methods.FirstOrDefault(m => m.Name == "Log");
        Assert.NotNull(method);
        var levelParam = method!.Parameters.FirstOrDefault(p => p.Name == "level");
        Assert.NotNull(levelParam);
        Assert.NotNull(levelParam!.DefaultValue);
        var contextParam = method.Parameters.FirstOrDefault(p => p.Name == "context");
        Assert.NotNull(contextParam);
        Assert.NotNull(contextParam!.DefaultValue);
    }

    #endregion

    #region Batch 6 — Explicit Interface, Postfix Sub-Expr, Target-Typed New, Cast-Then-Call

    [Fact]
    public void Convert_ExplicitInterfaceImplementation_PreservesQualifier()
    {
        var csharp = @"
using System;
public class MyResource : IDisposable
{
    void IDisposable.Dispose()
    {
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should preserve the interface qualifier
        Assert.Contains("IDisposable.Dispose", calor);
    }

    [Fact]
    public void Convert_PostfixIncrementSubExpression_NoERR()
    {
        var csharp = @"
public class Example
{
    public int Test()
    {
        int i = 0;
        int x = i++;
        return x;
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should not contain ERR
        Assert.DoesNotContain("ERR", calor);
        // Should have both i and x
        Assert.Contains("§B", calor);
    }

    [Fact]
    public void Convert_TargetTypedNew_InfersTypeFromDeclaration()
    {
        var csharp = @"
using System.Collections.Generic;
public class Example
{
    public void Test()
    {
        List<string> items = new();
        Dictionary<string, int> map = new();
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should infer List type, not emit "default"
        Assert.Contains("§NEW", calor);
        Assert.DoesNotContain("default", calor);
    }

    [Fact]
    public void Convert_CastThenCall_HoistsToTemp()
    {
        var csharp = @"
using System;
public class Example
{
    public string Test(object obj)
    {
        return ((IFormattable)obj).ToString(""N"", null);
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should not contain ERR
        Assert.DoesNotContain("ERR", calor);
        // Should have a temp bind for the cast
        Assert.Contains("_cast", calor);
    }

    #endregion

    #region Batch 7: Tuples, Generic Static Access, Variance

    [Fact]
    public void Convert_TupleDeconstruction_ProducesBindStatements()
    {
        var csharp = @"
public class Example
{
    public (int, string) GetPair() => (1, ""hello"");
    public void Test()
    {
        var (a, b) = GetPair();
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should produce temp bind and individual item binds
        Assert.Contains("_tup", calor);
        Assert.Contains("Item1", calor);
        Assert.Contains("Item2", calor);
        // The deconstruction should produce bind statements, not an ERR for "var (a, b)"
        Assert.DoesNotContain("var (a, b)", calor);
    }

    [Fact]
    public void Convert_GenericStaticMemberAccess_Preserved()
    {
        var csharp = @"
using System.Collections.Generic;
public class Example
{
    public IEqualityComparer<string> GetComparer()
    {
        return EqualityComparer<string>.Default;
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should not produce an ERR for generic static member access
        Assert.DoesNotContain("ERR", calor);
        // Should preserve the generic type and member
        Assert.Contains("EqualityComparer", calor);
        Assert.Contains("Default", calor);
    }

    [Fact]
    public void Convert_VarianceModifiers_PreservedInAst()
    {
        var csharp = @"
public interface IProducer<out T>
{
    T Produce();
}
public interface IConsumer<in T>
{
    void Consume(T item);
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        // Check the IProducer interface has out variance on T
        var producer = result.Ast!.Interfaces.FirstOrDefault(i => i.Name == "IProducer");
        Assert.NotNull(producer);
        Assert.Single(producer!.TypeParameters);
        Assert.Equal(Calor.Compiler.Ast.VarianceKind.Out, producer.TypeParameters[0].Variance);

        // Check the IConsumer interface has in variance on T
        var consumer = result.Ast.Interfaces.FirstOrDefault(i => i.Name == "IConsumer");
        Assert.NotNull(consumer);
        Assert.Single(consumer!.TypeParameters);
        Assert.Equal(Calor.Compiler.Ast.VarianceKind.In, consumer.TypeParameters[0].Variance);
    }

    [Fact]
    public void Convert_VarianceModifiers_RoundTripEmitsInOut()
    {
        var csharp = @"
public interface IReadOnly<out T>
{
    T Get();
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Calor source should contain 'out T' in the type parameter list
        Assert.Contains("out T", calor);

        // Compile Calor back to C# and verify variance is preserved
        var compiled = Compile(calor);
        Assert.Contains("out T", compiled);
    }

    [Fact]
    public void Convert_SystemStringEmpty_ToEmptyLiteral()
    {
        var csharp = @"
public class Example
{
    public string Test()
    {
        return System.String.Empty;
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should produce empty string literal, not a field access
        Assert.DoesNotContain("System.String.Empty", calor);
        Assert.Contains("\"\"", calor);
    }

    [Fact]
    public void Convert_GenericNameExpression_NotFallback()
    {
        var csharp = @"
using System;
using System.Collections.Generic;
public class Example
{
    public string[] Test()
    {
        return Array.Empty<string>();
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should not produce ERR for generic method invocation
        Assert.DoesNotContain("ERR", calor);
    }

    #endregion

    #region Batch 8: Named Args, Getter-Only Props, Tuple Literals

    [Fact]
    public void Convert_NamedArguments_PreservedInAst()
    {
        var csharp = @"
public class Example
{
    public void Target(int x, bool flag) { }
    public void Test()
    {
        Target(x: 42, flag: true);
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        // Find the CallStatementNode in the Test method (standalone invocations become CallStatementNode)
        var testMethod = result.Ast!.Classes[0].Methods.First(m => m.Name == "Test");
        var callStmt = testMethod.Body.OfType<Calor.Compiler.Ast.CallStatementNode>().First();
        Assert.NotNull(callStmt.ArgumentNames);
        Assert.Equal(2, callStmt.ArgumentNames!.Count);
        Assert.Equal("x", callStmt.ArgumentNames[0]);
        Assert.Equal("flag", callStmt.ArgumentNames[1]);
    }

    [Fact]
    public void Convert_GetterOnlyProperty_NoSetterInRoundTrip()
    {
        var csharp = @"
public class Example
{
    public int ReadOnly { get; }
    public int ReadWrite { get; set; }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Compile round-trip
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        // ReadOnly should NOT have set
        Assert.Contains("ReadOnly { get; }", compiled);
        // ReadWrite SHOULD have set
        Assert.Contains("ReadWrite { get; set; }", compiled);
    }

    [Fact]
    public void Convert_TupleLiteral_NotFallback()
    {
        var csharp = @"
public class Example
{
    public (int, string) GetPair()
    {
        return (42, ""hello"");
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Tuple literal should not produce ERR
        Assert.DoesNotContain("ERR", calor);
        // Should contain the tuple elements
        Assert.Contains("42", calor);
        Assert.Contains("hello", calor);
    }

    [Fact]
    public void Convert_TupleLiteral_InAst()
    {
        var csharp = @"
public class Example
{
    public object GetPair()
    {
        return (1, 2, 3);
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        // Find the return statement and check for TupleLiteralNode
        var method = result.Ast!.Classes[0].Methods.First(m => m.Name == "GetPair");
        var returnStmt = method.Body.OfType<Calor.Compiler.Ast.ReturnStatementNode>().First();
        var tuple = returnStmt.Expression as Calor.Compiler.Ast.TupleLiteralNode;
        Assert.NotNull(tuple);
        Assert.Equal(3, tuple!.Elements.Count);
    }

    [Fact]
    public void Convert_VerbatimStringRegex_RoundTrip()
    {
        var csharp = @"
public class Example
{
    const string pattern = @""(\p{Lu}?\p{Ll}+)"";
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Compile round-trip
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        // Should contain the regex pattern (escaped correctly for C#)
        Assert.Contains(@"\\p{Lu}", compiled);
        Assert.Contains(@"\\p{Ll}", compiled);
    }

    #endregion

    #region Batch 9: notnull constraint, static lambdas

    [Fact]
    public void Convert_NotNullConstraint_PreservedInAst()
    {
        var csharp = @"
public class Box<T> where T : notnull
{
    public T Value { get; set; }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        var classNode = result.Ast!.Classes[0];
        var typeParam = classNode.TypeParameters[0];
        Assert.Single(typeParam.Constraints);
        Assert.Equal(Calor.Compiler.Ast.TypeConstraintKind.NotNull, typeParam.Constraints[0].Kind);
    }

    [Fact]
    public void Convert_NotNullConstraint_RoundTrip()
    {
        var csharp = @"
public class Box<T> where T : notnull
{
    public T Value { get; set; }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Compile back to C#
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        Assert.Contains("where T : notnull", compiled);
    }

    [Fact]
    public void Convert_StaticLambda_PreservedInAst()
    {
        var csharp = @"
using System;
public class Example
{
    public void Test()
    {
        Func<int, int> doubler = static (int x) => x * 2;
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        // Find the lambda in the AST
        var method = result.Ast!.Classes[0].Methods.First(m => m.Name == "Test");
        var bind = method.Body.OfType<Calor.Compiler.Ast.BindStatementNode>().First();
        var lambda = bind.Initializer as Calor.Compiler.Ast.LambdaExpressionNode;
        Assert.NotNull(lambda);
        Assert.True(lambda!.IsStatic);
    }

    [Fact]
    public void Convert_StaticLambda_EmitsStaticKeyword()
    {
        var csharp = @"
using System;
public class Example
{
    public void Test()
    {
        Func<int, int> doubler = static (int x) => x * 2;
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Compile back to C# and check static is emitted
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        Assert.Contains("static", compiled);
    }

    [Fact]
    public void Convert_NotNullWithOtherConstraints_PreservedInAst()
    {
        var csharp = @"
public class Dict<TKey, TValue> where TKey : notnull where TValue : class
{
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        var classNode = result.Ast!.Classes[0];
        Assert.Equal(2, classNode.TypeParameters.Count);

        var keyParam = classNode.TypeParameters.First(tp => tp.Name == "TKey");
        Assert.Single(keyParam.Constraints);
        Assert.Equal(Calor.Compiler.Ast.TypeConstraintKind.NotNull, keyParam.Constraints[0].Kind);

        var valueParam = classNode.TypeParameters.First(tp => tp.Name == "TValue");
        Assert.Single(valueParam.Constraints);
        Assert.Equal(Calor.Compiler.Ast.TypeConstraintKind.Class, valueParam.Constraints[0].Kind);
    }

    #endregion

    #region Batch 10: delegates, parameter attributes, generic interface overloads

    [Fact]
    public void Convert_DelegateDeclaration_RoundTrip()
    {
        var csharp = @"
public delegate void MyHandler(int x);
public delegate bool Predicate<T>(T item);";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);
        Assert.NotNull(result.CalorSource);

        // Verify delegates are in the AST
        Assert.Equal(2, result.Ast!.Delegates.Count);
        Assert.Equal("MyHandler", result.Ast.Delegates[0].Name);

        // Verify delegates appear in Calor output
        var calor = result.CalorSource!;
        Assert.Contains("§DEL", calor);

        // Compile round-trip
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        Assert.Contains("delegate", compiled);
        Assert.Contains("MyHandler", compiled);
    }

    [Fact]
    public void Convert_ParameterAttributes_Preserved()
    {
        var csharp = @"
using System.ComponentModel;
public class Example
{
    public void Process([Description(""The input value"")] string input, [Obsolete] int count) { }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        var method = result.Ast!.Classes[0].Methods.First(m => m.Name == "Process");
        // First param should have Description attribute
        Assert.NotEmpty(method.Parameters[0].CSharpAttributes);
        Assert.Contains(method.Parameters[0].CSharpAttributes, a => a.Name.Contains("Description"));
        // Second param should have Obsolete attribute
        Assert.NotEmpty(method.Parameters[1].CSharpAttributes);
        Assert.Contains(method.Parameters[1].CSharpAttributes, a => a.Name.Contains("Obsolete"));
    }

    [Fact]
    public void Convert_GenericInterfaceOverloads_BothPreserved()
    {
        var csharp = @"
public interface IValidator { void Validate(); }
public interface IValidator<T> { void Validate(T item); }";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        // Both interfaces should be in the AST
        Assert.Equal(2, result.Ast!.Interfaces.Count);

        // One should have type parameters, one should not
        var nonGeneric = result.Ast.Interfaces.First(i => i.TypeParameters.Count == 0);
        var generic = result.Ast.Interfaces.First(i => i.TypeParameters.Count > 0);
        Assert.Equal("IValidator", nonGeneric.Name);
        Assert.Equal("IValidator", generic.Name);
        Assert.Single(generic.TypeParameters);
    }

    [Fact]
    public void Convert_ParameterAttributes_RoundTrip()
    {
        var csharp = @"
public class Api
{
    public void Handle([Obsolete] string input) { }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Compile round-trip
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        // Attribute should survive the round-trip
        Assert.Contains("Obsolete", compiled);
    }

    #endregion

    #region Batch 11: Required modifier and Partial methods

    [Fact]
    public void Convert_RequiredProperty_PreservedInAst()
    {
        var csharp = @"
public class UserDto
{
    public required string Name { get; set; }
    public required int Age { get; set; }
    public string? Email { get; set; }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        var cls = result.Ast!.Classes.First(c => c.Name == "UserDto");
        var nameProp = cls.Properties.First(p => p.Name == "Name");
        var ageProp = cls.Properties.First(p => p.Name == "Age");
        var emailProp = cls.Properties.First(p => p.Name == "Email");

        Assert.True(nameProp.IsRequired);
        Assert.True(ageProp.IsRequired);
        Assert.False(emailProp.IsRequired);
    }

    [Fact]
    public void Convert_RequiredProperty_RoundTrip()
    {
        var csharp = @"
public class UserDto
{
    public required string Name { get; set; }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Calor should contain "req" modifier
        Assert.Contains("req", calor);

        // Compile round-trip
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        Assert.Contains("required", compiled);
        Assert.Contains("public", compiled);
        Assert.Contains("string Name", compiled);
    }

    [Fact]
    public void Convert_RequiredField_RoundTrip()
    {
        var csharp = @"
public class Config
{
    public required string ConnectionString;
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Calor should contain "req" modifier
        Assert.Contains("req", calor);

        // Compile round-trip
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        Assert.Contains("required", compiled);
    }

    [Fact]
    public void Convert_PartialMethodStub_PreservedInAst()
    {
        var csharp = @"
public partial class MyClass
{
    partial void OnNameChanged();
    public void DoWork() { }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.Ast);

        var cls = result.Ast!.Classes.First(c => c.Name == "MyClass");
        // Partial method stub should NOT be dropped
        Assert.Equal(2, cls.Methods.Count);

        var partialMethod = cls.Methods.First(m => m.Name == "OnNameChanged");
        Assert.True(partialMethod.IsPartial);
        Assert.Empty(partialMethod.Body);
    }

    [Fact]
    public void Convert_PartialMethod_RoundTrip()
    {
        var csharp = @"
public partial class MyClass
{
    partial void OnNameChanged();
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Calor should contain "part" modifier
        Assert.Contains("part", calor);

        // Compile round-trip
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        Assert.Contains("partial", compiled);
        Assert.Contains("OnNameChanged", compiled);
        // Partial stub should end with semicolon, not have braces
        Assert.DoesNotContain("OnNameChanged()\n{", compiled);
    }

    #endregion

    #region Batch 12: Chain hoisting and §NEW in call arguments

    [Fact]
    public void Convert_ChainInIfCondition_HoistedBeforeIf()
    {
        var csharp = @"
using System.Linq;
using System.Collections.Generic;

public class Demo
{
    public void Work(List<int> items)
    {
        if (items.Where(x => x > 0).Any())
        {
            System.Console.WriteLine(""Found"");
        }
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // The chain binding should be on its own line BEFORE the §IF
        // and the §IF condition should reference the bound variable
        var lines = calor.Split('\n');
        var chainLine = lines.FirstOrDefault(l => l.Contains("_chain") && (l.TrimStart().StartsWith("§B") || l.Contains("Where")));
        var ifLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("§IF"));

        // Both should exist — if chain decomposition worked, there's a §B{_chainXXX} line
        Assert.True(chainLine != null, $"Expected chain binding before §IF. Output:\n{calor}");
        Assert.NotNull(ifLine);

        // Chain line should come before IF line
        var chainIdx = Array.IndexOf(lines, chainLine);
        var ifIdx = Array.IndexOf(lines, ifLine);
        Assert.True(chainIdx < ifIdx, $"Chain binding (line {chainIdx}) should come before §IF (line {ifIdx}). Output:\n{calor}");
    }

    [Fact]
    public void Convert_ChainInIfCondition_RoundTrip()
    {
        var csharp = @"
using System.Linq;
using System.Collections.Generic;

public class Demo
{
    public bool Work(List<int> items)
    {
        if (items.Where(x => x > 0).Any())
        {
            return true;
        }
        return false;
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Should compile without errors
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        // The compiled C# should still have the chain structure
        Assert.Contains("Where", compiled);
        Assert.Contains("Any", compiled);
    }

    [Fact]
    public void Convert_NewInCallArgument_HoistedToTempBinding()
    {
        var csharp = @"
public class Args { public Args(string ctx) { } }

public class Demo
{
    public void Process(Args args) { }

    public void Work(string ctx)
    {
        Process(new Args(ctx));
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // The §NEW should be hoisted to a temp binding, not inlined in §C
        Assert.Contains("_new", calor);
        Assert.Contains("§NEW", calor);

        // Compile round-trip
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        Assert.Contains("new Args", compiled);
        Assert.Contains("Process", compiled);
    }

    [Fact]
    public void Convert_IsPatternInIfCondition_BindingStaysInThenBody()
    {
        var csharp = @"
public class Demo
{
    public void Work(object obj)
    {
        if (obj is string s)
        {
            System.Console.WriteLine(s);
        }
    }
}";
        var result = _converter.Convert(csharp);
        Assert.NotNull(result.CalorSource);
        var calor = result.CalorSource!;

        // Compile round-trip
        var compiled = Compile(calor);
        Assert.NotNull(compiled);
        // The pattern variable 's' should be available inside the if body
        Assert.Contains("string", compiled);
    }

    #endregion
}
