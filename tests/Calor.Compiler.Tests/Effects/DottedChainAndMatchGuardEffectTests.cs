using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.30 (i8): two effect-soundness gaps that let a pure (<c>!</c>) callable perform effects.
/// <list type="bullet">
/// <item>A dotted name rooted at a type whose qualifier is itself a member read
/// (<c>DateTime.Now.Year</c>, <c>Console.Out.NewLine</c>, <c>Outer.Holder.Instance.Size</c>)
/// stays one dotted name in the parser, and the effect pass charged nothing for it. It is now
/// charged exactly as the member-access chain: the qualifier's getter or type initialization,
/// then the member's getter. A dotted name the pass cannot resolve is <c>unknown</c>.</item>
/// <item>A match case's <c>when</c> guard was never inferred, in either match form.</item>
/// </list>
/// </summary>
public class DottedChainAndMatchGuardEffectTests
{
    private static IReadOnlyList<Diagnostic> Errors(string source, bool strict = false)
        => Program.Compile(source, "chains.calr", new CompilationOptions { StrictEffects = strict })
            .Diagnostics.Errors.ToList();

    private static void AssertUses(string source, string function, string effect, bool strict = false)
        => Assert.Contains(Errors(source, strict), error => error.Code == DiagnosticCode.ForbiddenEffect
            && error.Message.Contains($"Function '{function}' uses effect '{effect}'", StringComparison.Ordinal));

    private static void AssertClean(string source, bool strict = true)
    {
        var errors = Errors(source, strict);
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(error => $"{error.Code} {error.Message}")));
    }

    private const string Holders = """
          class Holder
            field Holder:Instance public static = new Holder()
            field int:Size public
            field int:Max public const = 10

          class Outer
            class Inner public
              field Inner:Instance public static = new Inner()
              field int:Size public

          enum Color
            Red
        """;

    private static string Module(string row, string body) => $$"""
        module Chains
        {{Holders}}
          func Read public () → int
            {{row}}
            {{body}}
        """;

    // ---- Gap 1: type-rooted dotted chains ----

    [Fact]
    public void DateTimeNowYear_UnderPure_IsChargedTheClock()
        => AssertUses(Module("!", "return DateTime.Now.Year"), "Read", "time");

    [Fact]
    public void DateTimeNowYear_ChargesOnlyTheClock()
        // DateTime.Now is `time` in the BCL manifest; DateTime.Year is pure.
        => AssertClean(Module("! time", "return DateTime.Now.Year"));

    [Fact]
    public void NamespaceQualifiedDateTimeNowYear_IsChargedTheClock()
    {
        AssertUses(Module("!", "return System.DateTime.Now.Year"), "Read", "time");
        AssertClean(Module("! time", "return System.DateTime.Now.Year"));
    }

    [Fact]
    public void ConsoleOutNewLine_UnderPure_IsRejected()
        // TextWriter.NewLine has no manifest entry: the read is unknown, never pure.
        => AssertUses(Module("!", "return Console.Out.NewLine.Length"), "Read", "unknown");

    [Fact]
    public void ConsoleOutNewLine_UnderUnknown_IsAccepted()
        => AssertClean(Module("! unknown", "return Console.Out.NewLine.Length"), strict: false);

    [Fact]
    public void HolderInstanceSize_UnderPure_IsChargedTheStaticInitializersAlloc()
        => AssertUses(Module("!", "return Holder.Instance.Size"), "Read", "alloc");

    [Fact]
    public void NestedHolderInstanceSize_UnderPure_IsChargedTheStaticInitializersAlloc()
    {
        AssertUses(Module("!", "return Outer.Inner.Instance.Size"), "Read", "alloc");
        AssertClean(Module("! alloc", "return Outer.Inner.Instance.Size"));
    }

    [Fact]
    public void StoreThroughATypeRootedChain_ChargesMutAndTheQualifiersInitialization()
    {
        const string source = """
            module Chains
              class Outer
                class Inner public
                  field Inner:Instance public static = new Inner()
                  field int:Size public

              func Write public () → void
                ! mut
                set Outer.Inner.Instance.Size 3
            """;
        AssertUses(source, "Write", "alloc");
        AssertClean(source.Replace("! mut", "! mut, alloc", StringComparison.Ordinal));
    }

    [Fact]
    public void UnresolvableDottedName_IsUnknownNotPure()
    {
        const string source = """
            module Chains
              func Read public () → int
                !
                return Nowhere.Clock.Ticks
            """;
        AssertUses(source, "Read", "unknown");
    }

    [Theory]
    [InlineData("return Math.PI > 3.0", "bool")]
    [InlineData("return System.Math.PI > 3.0", "bool")]
    [InlineData("return int.MaxValue", "int")]
    [InlineData("return string.Empty.Length", "int")]
    [InlineData("return Holder.Max", "int")]
    [InlineData("return Color.Red == Color.Red", "bool")]
    public void ManifestOrSourceProvedPureReads_StayPure(string body, string type)
    {
        var source = Module("!", body).Replace("→ int", $"→ {type}", StringComparison.Ordinal);
        AssertClean(source);
    }

    [Fact]
    public void ModuleFunctionReference_StaysPure()
    {
        const string source = """
            module Chains
              func Twice public (int:x) → int
                !
                return x * 2

              func Apply public () → int
                !
                let Func<int, int>:f = Twice
                return f(4)
            """;
        AssertClean(source);
    }

    // ---- Gap 2: match-case `when` guards ----

    private const string GuardCallees = """
          class Counter
            field int:Hits public static

          func Bump public (int:n) → bool
            ! mut
            set Counter.Hits n
            return n > 0
        """;

    [Fact]
    public void StatementMatchGuard_ConsoleRead_IsCharged()
    {
        const string source = """
            module Guards
              func Pick public (int:n) → int
                !
                match n
                  case 1 when Console.ReadLine() == "y"
                    return 1
                  case _
                    return 0
            """;
        AssertUses(source, "Pick", "cr");
        AssertClean(source.Replace("    !\n", "    ! cr\n", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpressionMatchGuard_ConsoleRead_IsCharged()
    {
        const string source = """
            module Guards
              func Pick public (int:n) → int
                !
                return match n
                  case 1 when Console.ReadLine() == "y" → 1
                  case _ → 0
            """;
        AssertUses(source, "Pick", "cr");
    }

    [Fact]
    public void StatementMatchGuard_CallingAMutFunction_IsCharged()
    {
        var source = $$"""
            module Guards
            {{GuardCallees}}
              func Pick public (int:n) → int
                !
                match n
                  case var k when Bump(k)
                    return 1
                  case _
                    return 0
            """;
        AssertUses(source, "Pick", "mut");
    }

    [Fact]
    public void ExpressionMatchGuard_CallingAMutFunction_IsCharged()
    {
        var source = $$"""
            module Guards
            {{GuardCallees}}
              func Pick public (int:n) → int
                !
                return match n
                  case var k when Bump(k) → 1
                  case _ → 0
            """;
        AssertUses(source, "Pick", "mut");
        AssertClean(source.Replace("    !\n    return match", "    ! mut\n    return match",
            StringComparison.Ordinal));
    }

    [Fact]
    public void ExpressionMatchCaseBody_IsCharged()
    {
        const string source = """
            module Guards
              func Pick public (int:n) → int
                !
                return match n
                  case 1 → 1
                  case _ → Console.Read()
            """;
        AssertUses(source, "Pick", "cr");
    }

    // ---- Property sub-patterns read members ----

    private static string Accounts(string row, string pattern) => $$"""
        module Patterns
          class Account
            field int:balance private
            field bool:Active public
            property int:Balance public
              get
                ! cw
                Console.WriteLine("read")
                return balance

          func Check public (object:value) → int
            {{row}}
            return match value
              case {{pattern}} → 1
              case _ → 0
        """;

    [Fact]
    public void PropertySubPattern_ChargesTheGetter()
    {
        AssertUses(Accounts("!", "Account { Balance: > 0 }"), "Check", "cw");
        AssertClean(Accounts("! cw", "Account { Balance: > 0 }"));
    }

    [Fact]
    public void PropertySubPattern_OnTheMatchedValuesOwnType_ChargesTheGetter()
    {
        const string source = """
            module Patterns
              class Account
                field int:balance private
                property int:Balance public
                  get
                    ! cw
                    Console.WriteLine("read")
                    return balance

              func Check public (Account:account) → int
                !
                return match account
                  case { Balance: 0 } → 1
                  case _ → 0
            """;
        AssertUses(source, "Check", "cw");
    }

    [Fact]
    public void FieldSubPattern_StaysPure()
        => AssertClean(Accounts("!", "Account { Active: true }"));

    [Fact]
    public void PureGuard_StaysPure()
    {
        const string source = """
            module Guards
              func Describe public (int:value) → string
                !
                return match value
                  case var n when n > 100 → "large"
                  case _ → "small"
            """;
        AssertClean(source);
    }
}
