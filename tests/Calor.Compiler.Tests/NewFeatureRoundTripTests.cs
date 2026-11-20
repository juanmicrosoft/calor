using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Round-trip tests for newer Calor language features.
/// Each test: Parse source -> Format with CalorFormatter -> Re-parse formatted output -> Assert no errors.
/// This ensures that the formatter produces syntactically valid output for all language constructs.
/// </summary>
public class NewFeatureRoundTripTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    /// <summary>
    /// Parse -> CalorFormatter.Format -> Re-parse, asserting no errors at each step.
    /// </summary>
    private static (ModuleNode original, string formatted, ModuleNode reparsed) FormatAndReparse(string source)
    {
        // Step 1: Parse original
        var original = Parse(source, out var diagnostics1);
        Assert.False(diagnostics1.HasErrors,
            $"Original source should parse without errors.\nErrors: {string.Join("\n", diagnostics1.Select(d => d.Message))}");

        // Step 2: Format
        var formatter = new CalorFormatter();
        var formatted = formatter.Format(original);

        // Step 3: Re-parse formatted output
        var reparsed = Parse(formatted, out var diagnostics2);
        Assert.False(diagnostics2.HasErrors,
            $"Formatted output should re-parse without errors.\nFormatted:\n{formatted}\nErrors: {string.Join("\n", diagnostics2.Select(d => d.Message))}");

        return (original, formatted, reparsed);
    }

    #region Type Operations (is, as, cast)

    [Fact]
    public void RoundTrip_CastExpression_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func CastTest (object:x) → int
    !
    return ((int)x)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("(int)x", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_IsExpression_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func IsTest (object:x) → bool
    !
    return (x is string)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("is", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_AsExpression_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func AsTest (object:x) → string
    !
    return (x as string)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("as", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_NestedCast_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func NestedCast (object:x) → int
    !
    return ((int)((double)x))
";
        var (_, formatted, _) = FormatAndReparse(source);
        Assert.Contains("((int)((double)x))", formatted);
    }

    #endregion

    #region Async/Await

    [Fact]
    public void RoundTrip_AsyncFunction_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func GetDataAsync async () → string
    !
    return ""data""
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.NotEmpty(formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Pattern Matching / Switch Expression

    [Fact]
    public void RoundTrip_MatchStatement_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func MatchTest (int:x) → string
    !
    match x
      case 1
        return ""one""
      case 2
        return ""two""
      case _
        return ""other""
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("match ", formatted);
        Assert.Contains("case ", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_MatchWithNullablePatterns_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func OptMatch (int:x) → int
    !
    match x
      case null
        return 1
      case _
        return 0
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("null", formatted);
        Assert.Contains("case _", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Contracts (Preconditions / Postconditions)

    [Fact]
    public void RoundTrip_PreconditionAndPostcondition_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b | (# != 0)) → int
    !
    => (result >= 0)
    return (a / b)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("requires ", formatted);
        Assert.Contains("=> ", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Loops

    [Fact]
    public void RoundTrip_ForLoop_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func LoopTest () → void
    ! cw
    for i from 0 to 10 step 1
      Console.WriteLine(i)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("for i from ", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_WhileLoop_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func WhileTest (bool:running) → void
    ! cw
    while running
      Console.WriteLine(""working"")
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("while ", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_NestedLoops_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func NestedLoop () → void
    ! cw
    for i from 0 to 5 step 1
      for j from 0 to 5 step 1
        Console.WriteLine((i + j))
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Control Flow

    [Fact]
    public void RoundTrip_NestedIfElse_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func NestedIf (bool:a, bool:b) → int
    !
    if a
      return (b ? 2 : 1)
    else
      return 0
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("if ", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Effects and Calls

    [Fact]
    public void RoundTrip_FunctionWithEffects_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func PrintAll (string:msg) → void
    ! cw
    Console.WriteLine(msg)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("! cw", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Nullable Values and Exceptions

    [Fact]
    public void RoundTrip_NullableExpressions_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func SomeTest () → int?
    !
    return 42

  func NoneTest () → int?
    !
    return null
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("42", formatted);
        Assert.Contains("null", formatted);
        Assert.Equal(2, reparsed.Functions.Count);
    }

    [Fact]
    public void RoundTrip_ReturnAndThrow_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func OkTest () → int
    !
    return 42

  func ErrTest () → string
    !
    throw new Exception(""error"")
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("42", formatted);
        Assert.Contains("throw ", formatted);
        Assert.Equal(2, reparsed.Functions.Count);
    }

    #endregion

    #region Literals

    [Fact]
    public void RoundTrip_LiteralInReturn_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func IntLiteral () → int
    !
    return 42

  func BoolLiteral () → bool
    !
    return true

  func StringLiteral () → string
    !
    return ""hello""
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("42", formatted);
        Assert.Contains("true", formatted);
        Assert.Contains("hello", formatted);
        Assert.Equal(3, reparsed.Functions.Count);
    }

    #endregion

    #region Multiple Functions

    [Fact]
    public void RoundTrip_MultipleFunctions_FormatsAndReparses()
    {
        var source = @"
module MathLib MathLib block
  func Add (int:a, int:b) → int
    !
    return (a + b)

  func Subtract (int:a, int:b) → int
    !
    return (a - b)

  func Multiply private (int:a, int:b) → int
    return (a * b)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("Add", formatted);
        Assert.Contains("Subtract", formatted);
        Assert.Contains("Multiply", formatted);
        Assert.Equal(3, reparsed.Functions.Count);
    }

    #endregion

    #region Complex Combinations

    [Fact]
    public void RoundTrip_FunctionWithContractsEffectsAndControlFlow_FormatsAndReparses()
    {
        var source = @"
module ComplexTest ComplexTest block
  func Process (int:input | (# > 0)) → int
    ! cw
    => (result >= 0)
    if (input > 10)
      Console.WriteLine(""big"")
      return (input * 2)
    else
      Console.WriteLine(""small"")
      return input
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("requires ", formatted);
        Assert.Contains("=> ", formatted);
        Assert.Contains("if ", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Bind Statements

    [Fact]
    public void RoundTrip_BindStatements_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func BindTest (int:a, int:b) → int
    !
    let sum = (a + b)
    return sum
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("let sum", formatted);
        Assert.DoesNotContain("§LET{", formatted);
        Assert.DoesNotContain("§MUT{", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_TypedBindStatements_FormatsAndReparses()
    {
        // Typed immutable: {type:name}, typed mutable: {~name:type}
        var source = @"
module Test Test block
  func TypedBindTest (int:x) → int
    !
    let int:count = 0
    var int:total = 0
    return count
";
        var (original, formatted, reparsed) = FormatAndReparse(source);

        // Immutable typed bind should format as {type:name}
        Assert.Contains("let int:count", formatted);
        // Mutable typed bind should format as {~name:type}
        Assert.Contains("var int:total", formatted);

        // Round-trip preserves semantics
        var func = Assert.Single(reparsed.Functions);
        Assert.True(func.Body.Count >= 2);

        var immutableBind = (BindStatementNode)func.Body[0];
        Assert.Equal("count", immutableBind.Name);
        Assert.False(immutableBind.IsMutable);
        Assert.NotNull(immutableBind.TypeName);

        var mutableBind = (BindStatementNode)func.Body[1];
        Assert.Equal("total", mutableBind.Name);
        Assert.True(mutableBind.IsMutable);
        Assert.NotNull(mutableBind.TypeName);
    }

    [Fact]
    public void RoundTrip_LegacyConstBind_ParsesAsImmutable()
    {
        // Legacy CalorEmitter format: {name:const} should parse as immutable, no type
        var source = @"
module Test Test block
  func LegacyTest () → int
    !
    let count = 42
    return count
";
        var parsed = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Legacy :const format should parse without errors.\nErrors: {string.Join("\n", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(parsed.Functions);
        var bind = (BindStatementNode)func.Body[0];
        Assert.Equal("count", bind.Name);
        Assert.False(bind.IsMutable);
        Assert.Null(bind.TypeName);
    }

    [Fact]
    public void RoundTrip_LegacyTypedConstBind_ParsesAsImmutableWithType()
    {
        // Legacy format: {type:name:const} should parse as immutable with type
        var source = @"
module Test Test block
  func LegacyTypedTest () → int
    !
    let int:count = 42
    return count
";
        var parsed = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            $"Legacy :const format with type should parse without errors.\nErrors: {string.Join("\n", diagnostics.Select(d => d.Message))}");

        var func = Assert.Single(parsed.Functions);
        var bind = (BindStatementNode)func.Body[0];
        Assert.Equal("count", bind.Name);
        Assert.False(bind.IsMutable);
        Assert.NotNull(bind.TypeName);
    }

    #endregion

    #region Arrays

    [Fact]
    public void RoundTrip_ArrayInitialized_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func Main () → void
    !
    let int[]:nums = new int[] { 1, 2, 3 }
";
        var (_, formatted, _) = FormatAndReparse(source);
        Assert.Contains("new int[] { 1, 2, 3 }", formatted);
        // Phase 4b: indent form — array literal blocks end by dedent, not §/ARR.
    }

    [Fact]
    public void RoundTrip_ArraySized_FormatsAndReparses()
    {
        var source = @"
module Test Test block
  func Main () → void
    !
    let int[]:arr1 = new arr1[10]
";
        var (_, formatted, _) = FormatAndReparse(source);
        Assert.Contains("new arr1[10]", formatted);
    }

    #endregion

    #region Effect rows (v0.15 E2 slice a — design-doc pin P4)

    /// <summary>
    /// Parse → <c>CalorEmitter</c> → parse → emit, asserting the emitter is a fixed
    /// point: <c>emit(parse(s))</c> equals <c>emit(parse(emit(parse(s))))</c>, byte
    /// for byte. That is the achievable form of P4 — the emitter has always
    /// normalised source (it reorders effect codes, drops optional whitespace), so
    /// "identical to the original source" was never the property, and claiming it
    /// would be claiming something the emitter does not do for any construct.
    /// </summary>
    /// <remarks>
    /// <b>This half is the weaker one.</b> An emitter that dropped every row would
    /// still be a fixed point — it would emit the same row-less text twice. What
    /// discriminates "the row survived" is the <c>Assert.Contains</c> in the caller,
    /// which is why each case's expected string carries its POSITION (the <c>§I</c>
    /// tag, the <c>-&gt;</c>, the <c>§FLD</c> or <c>§B</c> header, or the surrounding
    /// newlines for a row that must stand on its own line) rather than a bare
    /// <c>§E{cw}</c> — which any other position's row in the same output would satisfy.
    /// </remarks>
    private static void AssertEmitterIsAFixedPoint(string source)
    {
        var first = Parse(source, out var d1);
        Assert.False(d1.HasErrors,
            $"Source should parse cleanly.\n{string.Join("\n", d1.Select(d => d.Message))}");
        var once = new CalorFormatter().Format(first);

        var second = Parse(once, out var d2);
        Assert.False(d2.HasErrors,
            $"Emitted Calor should re-parse cleanly.\nEmitted:\n{once}\n"
            + string.Join("\n", d2.Select(d => d.Message)));
        var twice = new CalorFormatter().Format(second);

        Assert.Equal(once, twice);
    }

    [Theory]
    // position 1 — the declaration's own row
    [InlineData("""
        module R R block
          func F () → void
            ! cw
            Console.WriteLine("x")
        """, "\n    ! cw\n")]
    // position 2 — lambda literal. The emitter did not write this tag back before
    // this slice, so the row was silently dropped on every round trip.
    [InlineData("""
        module R R block
          func Main () → void
            !
            let f = (int:x) ! cw => (x + 1)
        """, "(int:x) ! cw =>")]
    // position 3 — delegate declaration. Emitted as `§E{[console, write]}` before
    // this slice: a stringified KeyValuePair that did not re-parse.
    [InlineData("""
        module R R block
          delegate Handler (int:x) → void
            ! cw

          func Main () → void
            !
            return
        """, "\n    ! cw\n")]
    // position 4 — parameter, tag form
    [InlineData("""
        module R R block
          func Apply (Func<int, int>:transform ! cw, int:value = 1) → int
            ! cw
            return 0
        """, "Func<int, int>:transform ! cw")]
    // position 5 — parameter, inline form
    [InlineData("""
        module R R block
          func Apply (Func<int, int>:transform ! cw, int:value) → int
            ! cw
            return 0
        """, "Func<int, int>:transform ! cw")]
    // position 6 — return, arrow spelling
    [InlineData("""
        module R R block
          func Make () → Func<int> ! cw
            ! cw
            return () => 0
        """, "→ Func<int> ! cw")]
    // position 7 — binding
    [InlineData("""
        module R R block
          func Main () → void
            !
            let Func<int, int>:f ! cw = (int:x) => (x + 1)
        """, "let Func<int, int>:f ! cw")]
    // position 8 — field
    [InlineData("""
        module R R block
          class C
            field Action<int>:onChange private ! cw
            func M () → void
              !
              return
        """, "field Action<int>:onChange private ! cw")]
    // an empty row is not the same as no row (§3.5) and must survive
    [InlineData("""
        module R R block
          func Apply (Func<int, int>:a !, Func<int, int>:b) → int
            !
            return 0
        """, "Func<int, int>:a !,")]
    // the `eff` binder, written last in the type-parameter list
    [InlineData("""
        module R R block
          func Map <T,! e> (Func<int, int>:f ! e) → int
            ! e
            return 0
        """, "<T,! e>")]
    // …and written FIRST, which is why EffectParameterInfo carries an Ordinal
    [InlineData("""
        module R R block
          func Map <! e,T> (Func<int, int>:f ! e) → int
            ! e
            return 0
        """, "<! e,T>")]
    public void RowRoundTrips_ParseEmitParse(string source, string expectedInEmitted)
    {
        var module = Parse(source, out var diagnostics);
        Assert.False(diagnostics.HasErrors,
            string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitted = new CalorFormatter().Format(module);
        Assert.Contains(expectedInEmitted, emitted);

        AssertEmitterIsAFixedPoint(source);
    }

    #endregion
}
