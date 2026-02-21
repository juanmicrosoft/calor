using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Migration;
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
        Assert.Contains("get; set;", csharp);
        Assert.Contains("this._id", csharp);
        Assert.DoesNotContain("@this", csharp);
        Assert.Contains("new System.Text.StringBuilder()", csharp);
        Assert.DoesNotContain("System_Text_StringBuilder", csharp);
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
        // Property should appear (either get-only or get-set depending on branch)
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
/// </summary>
public class ConversionCampaignFixTests
{
    private readonly CSharpToCalorConverter _converter = new();

    #region Issue 301: Convert nameof() to string literal and string.Empty to ""

    [Fact]
    public void Convert_Nameof_ProducesStringLiteral()
    #region Issue 300: Convert postfix/prefix increment to compound assignment

    [Fact]
    public void Convert_PostfixIncrement_ProducesCompoundAssignment()
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
    public string GetDefault()
    {
        return string.Empty;
    }
}");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(@"""""", result.CalorSource);
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
        // Should have two §ASSIGN statements, not §ERR
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
}
