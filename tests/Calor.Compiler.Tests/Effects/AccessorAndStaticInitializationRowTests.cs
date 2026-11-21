using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.30 (D1 remainder and soundness gaps A/B):
/// <list type="bullet">
/// <item>accessors may declare an own row, written like a constructor's (Calor0423 otherwise);</item>
/// <item>a type's implicit static initialization may declare a header-clause row (Calor0422 otherwise);</item>
/// <item>the converter writes those rows instead of preserving the whole file;</item>
/// <item>gap A: a static member of a type with an explicit static constructor is charged that constructor;</item>
/// <item>gap B: reading a static field declared in a preserved raw member charges its type's initialization.</item>
/// </list>
/// </summary>
public class AccessorAndStaticInitializationRowTests
{
    private static IReadOnlyList<Diagnostic> Errors(string source)
        => Program.Compile(source, "rows.calr").Diagnostics.Errors.ToList();

    private static IReadOnlyList<string> ErrorCodes(string source)
        => Errors(source).Select(error => error.Code).ToList();

    private static string Account(string getterRow, string readerRow = "! throw, alloc") => $$"""
        module Accessors
          class Account
            field int:balance private
            property int:Balance
              get
        {{(getterRow.Length == 0 ? "" : "        " + getterRow + "\n")}}        if balance < 0
                  throw new InvalidOperationException("corrupt")
                return balance

          func Read public (Account:account) → int
            {{readerRow}}
            return account.Balance
        """;

    [Fact]
    public void Accessor_WithoutARow_AnEffectBeyondItsContract_IsCalor0423WithTheRowHint()
    {
        var error = Assert.Single(Errors(Account("")),
            e => e.Code == DiagnosticCode.AccessorEffectContractUnavailable);
        Assert.Contains("Declare them on the accessor", error.Message);
    }

    [Theory]
    [InlineData("! throw")]
    [InlineData("! unknown")]
    public void Accessor_ADeclaredRow_AdmitsTheBodysEffects(string row)
    {
        var reader = row == "! unknown" ? "! unknown" : "! throw, alloc";
        Assert.Empty(Errors(Account(row, reader)));
    }

    [Fact]
    public void Accessor_ADeclaredRow_DoesNotAdmitAnUndeclaredEffect()
    {
        var codes = ErrorCodes(Account("! cw", "! cw, throw, alloc"));
        Assert.Contains(DiagnosticCode.ForbiddenEffect, codes);
        Assert.DoesNotContain(DiagnosticCode.AccessorEffectContractUnavailable, codes);
    }

    [Fact]
    public void Accessor_ReadersAreChargedTheBodyAndTheDeclaredRow()
    {
        // The body throws: a reader that does not declare throw fails.
        Assert.Contains(DiagnosticCode.ForbiddenEffect, ErrorCodes(Account("! throw", "! alloc")));
        // The row is a contract: `! unknown` is charged even though the body is known.
        Assert.Contains(Errors(Account("! unknown", "! throw, alloc")), error =>
            error.Code == DiagnosticCode.ForbiddenEffect && error.Message.Contains("'unknown'"));
    }

    [Fact]
    public void Accessor_UnknownRow_AdmitsUncontractedCallsWithoutCalor0411()
    {
        const string source = """
            module Encoding
              class Codec
                field string:text private
                property string?:Encoded
                  get
                    ! unknown
                    return System.Web.HttpUtility.UrlEncode(text)
            """;
        var compiled = Program.Compile(source, "codec.calr");
        Assert.DoesNotContain(compiled.Diagnostics, d => d.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(compiled.Diagnostics.Errors, d => d.Code is DiagnosticCode.AccessorEffectContractUnavailable
            or DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void AutoAccessor_CannotCarryARow()
    {
        const string source = """
            module Auto
              class Box
                property int:Value
                  get
                    ! cw
            """;
        Assert.Contains(DiagnosticCode.EffectRowMisplaced, ErrorCodes(source));
    }

    private static string Defaults(string typeRow, string showRow) => $$"""
        module Settings
          class Defaults
        {{(typeRow.Length == 0 ? "" : "    " + typeRow + "\n")}}    field string:Text static = Load()

            func Load private static () → string
              return System.IO.File.ReadAllText("settings.txt")

          func Show public () → string
            {{showRow}}
            return Defaults.Text
        """;

    [Fact]
    public void StaticInitialization_WithoutARow_IsCalor0422WithTheTypeRowHint()
    {
        var error = Assert.Single(Errors(Defaults("", "! fs:r")),
            e => e.Code == DiagnosticCode.ConstructorEffectContractUnavailable);
        Assert.Contains("static-initialization row", error.Message);
    }

    [Fact]
    public void StaticInitialization_ADeclaredRow_AdmitsTheInitializersAndChargesTheReader()
    {
        Assert.Empty(Errors(Defaults("! fs:r", "! fs:r")));
        Assert.Contains(Errors(Defaults("! fs:r", "!")), error =>
            error.Code == DiagnosticCode.ForbiddenEffect && error.Message.Contains("'fs:r'"));
        Assert.Contains(DiagnosticCode.ForbiddenEffect, ErrorCodes(Defaults("! cw", "! cw, fs:r")));
    }

    [Fact]
    public void StaticInitialization_UnknownRow_IsChargedAsUnknown()
    {
        Assert.Empty(Errors(Defaults("! unknown", "! unknown")));
        Assert.Contains(Errors(Defaults("! unknown", "! fs:r")), error =>
            error.Code == DiagnosticCode.ForbiddenEffect && error.Message.Contains("'unknown'"));
    }

    [Fact]
    public void StaticInitialization_RowIsAHeaderClause_AndNotBesideAnExplicitStaticConstructor()
    {
        const string afterMember = """
            module Settings
              class Defaults
                field int:Count static = 1
                ! cw
            """;
        Assert.Contains(DiagnosticCode.EffectRowMisplaced, ErrorCodes(afterMember));

        const string withStaticConstructor = """
            module Settings
              class Defaults
                ! cw
                field int:Count static

                ctor static ()
                  set Count 1
            """;
        Assert.Contains(DiagnosticCode.EffectRowMisplaced, ErrorCodes(withStaticConstructor));
    }

    [Fact]
    public void Rows_SurviveEmitAndReparse()
    {
        var source = Defaults("! fs:r", "! fs:r");
        var module = Parse(source);
        var emitted = new CalorEmitter().Emit(module);
        var reparsed = Parse(emitted);
        var type = Assert.Single(reparsed.Classes, cls => cls.Name == "Defaults");
        Assert.Equal("fs:r", Calor.Compiler.Effects.EffectEnforcementPass
            .GetDeclaredEffects(type.StaticInitializerEffects).ToDisplayString());

        var accessors = Parse(new CalorEmitter().Emit(Parse(Account("! throw"))));
        var getter = Assert.Single(accessors.Classes, cls => cls.Name == "Account").Properties[0].Getter!;
        Assert.Equal("throw", Calor.Compiler.Effects.EffectEnforcementPass
            .GetDeclaredEffects(getter.Effects).ToDisplayString());
    }

    [Fact]
    public void Converter_DeclaresAnAccessorRowInsteadOfPreservingTheFile()
    {
        const string csharp = """
            public class Account
            {
                private int balance;
                public int Balance
                {
                    get
                    {
                        if (balance < 0) throw new System.InvalidOperationException("corrupt");
                        return balance;
                    }
                }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(csharp, "Account.cs");
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Matches(@"get( public)?\r?\n\s+! throw\r?\n", conversion.CalorSource);
        Assert.False(Program.Compile(conversion.CalorSource!, "Account.calr").HasErrors);
    }

    [Theory]
    [InlineData("System.Console.WriteLine(\"init\"); return 1;", "! cw")]
    [InlineData("return System.Runtime.GCSettings.IsServerGC ? 1 : 0;", "! unknown")]
    public void Converter_DeclaresAStaticInitializationRowInsteadOfPreservingTheFile(string body, string row)
    {
        var csharp = $$"""
            public class Holder
            {
                public static int Value = Start();
                private static int Start() { {{body}} }
                public static int Read() { return Value; }
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(csharp, "Holder.cs");
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        var calor = conversion.CalorSource!.ReplaceLineEndings("\n");
        Assert.Contains($"class Holder\n    {row}\n", calor);
        // The reader is charged the initialization, so its own row carries it too.
        Assert.Matches(@"func Read static \(\) → int\n\s+" + System.Text.RegularExpressions.Regex.Escape(row) + "\n", calor);
        Assert.False(Program.Compile(conversion.CalorSource!, "Holder.calr").HasErrors);
        // Without the type row the initializer is rejected again: the row is load-bearing.
        Assert.Contains(DiagnosticCode.ConstructorEffectContractUnavailable,
            ErrorCodes(calor.Replace($"class Holder\n    {row}\n", "class Holder\n")));
    }

    private static string StaticConstructorProbe(string valueRow, string useRow, string constructor = """
            ctor static ()
              ! cw
              Console.WriteLine("init")
        """) => $$"""
        module GapA
          class Probe
            field int:Count static

        {{constructor}}
            func Value public static () → int
              {{valueRow}}
              return 1

          class User
            func Use public static () → int
              {{useRow}}
              return Probe.Value()
        """;

    [Fact]
    public void GapA_AStaticMethodOfATypeWithAStaticConstructor_IsChargedTheConstructor()
    {
        // Calling Probe.Value() may run Probe's static constructor first (C# spec,
        // "Static constructors"), so Value's own row must cover it, and so must Use's.
        Assert.Contains(Errors(StaticConstructorProbe("!", "! cw")), error =>
            error.Code == DiagnosticCode.ForbiddenEffect && error.Message.Contains("'Value'"));
        Assert.Contains(Errors(StaticConstructorProbe("! cw", "!")), error =>
            error.Code == DiagnosticCode.ForbiddenEffect && error.Message.Contains("'Use'"));
        Assert.Empty(Errors(StaticConstructorProbe("! cw", "! cw")));
    }

    [Fact]
    public void GapA_ABeforeFieldInitType_DoesNotChargeItsStaticMethods()
    {
        // Without an explicit static constructor the initializers run only before the first
        // static field access, so a static method that touches no static field stays pure.
        const string source = """
            module GapA
              class Probe
                ! cw
                field int:Count static = Start()

                func Start public static () → int
                  ! cw
                  Console.WriteLine("init")
                  return 1

                func Value public static () → int
                  !
                  return 1

              class User
                func Use public static () → int
                  !
                  return Probe.Value()

                func Read public static () → int
                  !
                  return Probe.Count
            """;
        var errors = Errors(source);
        Assert.DoesNotContain(errors, error => error.Message.Contains("'Value'") || error.Message.Contains("'Use'"));
        Assert.Contains(errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'Read'") && error.Message.Contains("'cw'"));
    }

    [Fact]
    public void StaticAutoPropertyRead_ChargesTheTypeInitialization()
    {
        // A static auto-property reads a static backing field, which triggers type
        // initialization even without a static constructor.
        const string source = """
            module AutoProperty
              class Probe
                ! cw
                property int:P static = Start()
                  get

                func Start public static () → int
                  ! cw
                  Console.WriteLine("init")
                  return 1

              class User
                func Use public static () → int
                  !
                  return Probe.P
            """;
        Assert.Contains(Errors(source), error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'Use'") && error.Message.Contains("'cw'"));
    }

    private static string RawMember(string payload, string read) => $$"""
        module GapB
          class Probe
            raw member:unsafe:Other:Preserved {{System.Text.Encoding.UTF8.GetByteCount(payload)}}
        {{payload}}
            func Start public static () → int
              ! cw
              Console.WriteLine("init")
              return 1

          class User
            func Use public (Probe:probe) → int
              !
              return {{read}}
        """;

    [Fact]
    public void GapB_ReadingAStaticFieldDeclaredInARawMember_ChargesTheTypeInitialization()
    {
        var errors = Errors(RawMember("public static int X = Start();", "Probe.X"));
        Assert.Contains(errors, error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'Use'") && error.Message.Contains("'unknown'"));
    }

    [Fact]
    public void GapB_ABareReadInsideTheClass_IsChargedToo()
    {
        const string payload = "public static int X = Start();";
        var source = $$"""
            module GapB
              class Probe
                ! cw
                raw member:unsafe:Other:Preserved {{payload.Length}}
            {{payload}}
                func Start public static () → int
                  ! cw
                  Console.WriteLine("init")
                  return 1

                func Use public static () → int
                  !
                  return X
            """;
        Assert.Contains(Errors(source), error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'Use'") && error.Message.Contains("'unknown'"));
    }

    [Theory]
    [InlineData("public int X = 1;", "probe.X")]
    [InlineData("public const int X = 1;", "Probe.X")]
    public void GapB_InstanceAndConstantRawFields_AreNotChargedTypeInitialization(string payload, string read)
    {
        Assert.DoesNotContain(Errors(RawMember(payload, read)), error =>
            error.Code == DiagnosticCode.ForbiddenEffect && error.Message.Contains("'Use'"));
    }

    [Theory]
    [InlineData("return Value", true)]
    [InlineData("set Value 2\n      return 0", true)]
    [InlineData("return Limit", false)]
    public void BareStaticFieldAccess_InsideItsClass_ChargesTheTypeInitialization(string body, bool charged)
    {
        // `Value` inside Holder is the same static field access as `Holder.Value`: under
        // beforefieldinit it may be the first one, so it runs the initializers. A constant
        // runs nothing.
        var source = $$"""
            module Bare
              class Holder
                ! cw
                field int:Value static = Start()
                field int:Limit const = 3

                func Start private static () → int
                  Console.WriteLine("init")
                  return 1

                func Read public static () → int
                  !
                  {{body}}
            """;
        var errors = Errors(source);
        Assert.Equal(charged, errors.Any(error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains("'Read'") && error.Message.Contains("'cw'")));
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics) + "\n" + source);
        return module;
    }
}

