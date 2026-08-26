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
§M{m001:Test}
  §F{f001:CastTest:pub}
    §I{object:x}
    §O{i32}
    §R (cast i32 x)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("cast", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_IsExpression_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:IsTest:pub}
    §I{object:x}
    §O{bool}
    §R (is x str)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("is", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_AsExpression_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:AsTest:pub}
    §I{object:x}
    §O{str}
    §R (as x str)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("as", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_NestedCast_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:NestedCast:pub}
    §I{object:x}
    §O{i32}
    §R (cast i32 (cast f64 x))
";
        var (_, formatted, _) = FormatAndReparse(source);
        Assert.Contains("cast", formatted);
    }

    #endregion

    #region Async/Await

    [Fact]
    public void RoundTrip_AsyncFunction_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §AF{f001:GetDataAsync:pub}
    §O{str}
    §R ""data""
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
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§W{", formatted);
        Assert.Contains("§K", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_MatchWithOptionPatterns_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:OptMatch:pub}
    §I{i32:x}
    §O{i32}
    §W{m1} x
    §K §SM _
      §R 1
    §K §NN
      §R 0
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§SM", formatted);
        Assert.Contains("§NN", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Contracts (Preconditions / Postconditions)

    [Fact]
    public void RoundTrip_PreconditionAndPostcondition_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:Divide:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §Q (!= b 0)
    §S (>= result 0)
    §R (/ a b)
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§Q", formatted);
        Assert.Contains("§S", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Loops

    [Fact]
    public void RoundTrip_ForLoop_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:LoopTest:pub}
    §O{void}
    §E{cw}
    §L{l1:i:0:10:1}
      §C{Console.WriteLine} i
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§L{", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_WhileLoop_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:WhileTest:pub}
    §I{bool:running}
    §O{void}
    §E{cw}
    §WH{w1} running
      §P ""working""
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§WH", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_NestedLoops_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:NestedLoop:pub}
    §O{void}
    §E{cw}
    §L{l1:i:0:5:1}
      §L{l2:j:0:5:1}
        §C{Console.WriteLine} (+ i j)
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
§M{m001:Test}
  §F{f001:NestedIf:pub}
    §I{bool:a}
    §I{bool:b}
    §O{i32}
    §IF{if1} a
      §IF{if2} b
        §R 2
      §EL
        §R 1
    §EL
      §R 0
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§IF{", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Effects and Calls

    [Fact]
    public void RoundTrip_FunctionWithEffects_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:PrintAll:pub}
    §I{str:msg}
    §O{void}
    §E{cw}
    §C{Console.WriteLine} msg
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§E{", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Option and Result Types

    [Fact]
    public void RoundTrip_OptionExpressions_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:SomeTest:pub}
    §O{i32}
    §R §SM 42
  §F{f002:NoneTest:pub}
    §O{i32}
    §R §NN{i32}
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§SM", formatted);
        Assert.Contains("§NN", formatted);
        Assert.Equal(2, reparsed.Functions.Count);
    }

    [Fact]
    public void RoundTrip_ResultExpressions_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:OkTest:pub}
    §O{i32}
    §R §OK 42
  §F{f002:ErrTest:pub}
    §O{str}
    §R §ERR ""error""
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("OK", formatted);
        Assert.Contains("ERR", formatted);
        Assert.Equal(2, reparsed.Functions.Count);
    }

    #endregion

    #region Literals

    [Fact]
    public void RoundTrip_LiteralInReturn_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:IntLiteral:pub}
    §O{i32}
    §R 42
  §F{f002:BoolLiteral:pub}
    §O{bool}
    §R true
  §F{f003:StringLiteral:pub}
    §O{str}
    §R ""hello""
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
§M{m001:MathLib}
  §F{f001:Add:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §R (+ a b)
  §F{f002:Subtract:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §R (- a b)
  §F{f003:Multiply:prv}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §R (* a b)
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
§M{m001:ComplexTest}
  §F{f001:Process:pub}
    §I{i32:input}
    §O{i32}
    §E{cw}
    §Q (> input 0)
    §S (>= result 0)
    §IF{if1} (> input 10)
      §C{Console.WriteLine} ""big""
      §R (* input 2)
    §EL
      §C{Console.WriteLine} ""small""
      §R input
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§Q", formatted);
        Assert.Contains("§S", formatted);
        Assert.Contains("§IF{", formatted);
        Assert.Single(reparsed.Functions);
    }

    #endregion

    #region Bind Statements

    [Fact]
    public void RoundTrip_BindStatements_FormatsAndReparses()
    {
        var source = @"
§M{m001:Test}
  §F{f001:BindTest:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §B{sum} (+ a b)
    §R sum
";
        var (_, formatted, reparsed) = FormatAndReparse(source);
        Assert.Contains("§B{", formatted);
        Assert.DoesNotContain("§LET{", formatted);
        Assert.DoesNotContain("§MUT{", formatted);
        Assert.Single(reparsed.Functions);
    }

    [Fact]
    public void RoundTrip_TypedBindStatements_FormatsAndReparses()
    {
        // Typed immutable: {type:name}, typed mutable: {~name:type}
        var source = @"
§M{m001:Test}
  §F{f001:TypedBindTest:pub}
    §I{i32:x}
    §O{i32}
    §B{i32:count} 0
    §B{~total:i32} 0
    §R count
";
        var (original, formatted, reparsed) = FormatAndReparse(source);

        // Immutable typed bind should format as {type:name}
        Assert.Contains("§B{i32:count}", formatted);
        // Mutable typed bind should format as {~name:type}
        Assert.Contains("§B{~total:i32}", formatted);

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
§M{m001:Test}
  §F{f001:LegacyTest:pub}
    §O{i32}
    §B{count:const} 42
    §R count
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
§M{m001:Test}
  §F{f001:LegacyTypedTest:pub}
    §O{i32}
    §B{i32:count:const} 42
    §R count
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
§M{m1:Test}
  §F{f001:Main:pub}
      §O{void}
      §ARR{nums:i32} 1 2 3 §/ARR{nums}
";
        var (_, formatted, _) = FormatAndReparse(source);
        Assert.Contains("§ARR", formatted);
        // Phase 4b: indent form — array literal blocks end by dedent, not §/ARR.
    }

    [Fact]
    public void RoundTrip_ArraySized_FormatsAndReparses()
    {
        var source = @"
§M{m1:Test}
  §F{f001:Main:pub}
      §O{void}
      §B{[i32]:arr1} §ARR{i32:arr1:10}
";
        var (_, formatted, _) = FormatAndReparse(source);
        Assert.Contains("§ARR", formatted);
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
        §M{m001:R}
          §F{f001:F:pub} () -> void
            §E{cw}
            §P "x"
        """, "§E{cw}")]
    // position 2 — lambda literal. The emitter did not write this tag back before
    // this slice, so the row was silently dropped on every round trip.
    [InlineData("""
        §M{m001:R}
          §F{f001:Main:pub} () -> void
            §E{}
            §B{f} §LAM{lam1:x:i32} §E{cw} (+ x INT:1) §/LAM{lam1}
        """, "§LAM{lam1:x:i32} §E{cw}")]
    // position 3 — delegate declaration. Emitted as `§E{[console, write]}` before
    // this slice: a stringified KeyValuePair that did not re-parse.
    [InlineData("""
        §M{m001:R}
          §DEL{d001:Handler}
            §I{i32:x}
            §O{void}
            §E{cw}
          §F{f001:Main:pub} () -> void
            §E{}
        """, "§E{cw}")]
    // position 4 — parameter, tag form
    [InlineData("""
        §M{m001:R}
          §F{f001:Apply:pub}
            §I{Func<i32,i32>:transform} §E{cw}
            §I{i32:value} = INT:1
            §O{i32}
            §E{cw}
            §R INT:0
        """, "§I{Func<i32, i32>:transform} §E{cw}")]
    // position 5 — parameter, inline form
    [InlineData("""
        §M{m001:R}
          §F{f001:Apply:pub} (Func<i32,i32>:transform §E{cw}, i32:value) -> i32
            §E{cw}
            §R INT:0
        """, "Func<i32, i32>:transform §E{cw}")]
    // position 6 — return, §O spelling
    [InlineData("""
        §M{m001:R}
          §F{f001:Make:pub}
            §O{Func<i32>} §E{cw}
            §E{cw}
            §R §LAM{lam1} INT:0 §/LAM{lam1}
        """, "-> Func<i32> §E{cw}")]
    // position 6 — return, arrow spelling
    [InlineData("""
        §M{m001:R}
          §F{f001:Make:pub} () -> Func<i32> §E{cw}
            §E{cw}
            §R §LAM{lam1} INT:0 §/LAM{lam1}
        """, "-> Func<i32> §E{cw}")]
    // position 7 — binding
    [InlineData("""
        §M{m001:R}
          §F{f001:Main:pub} () -> void
            §E{}
            §B{f:Func<i32,i32>} §E{cw} §LAM{lam1:x:i32} (+ x INT:1) §/LAM{lam1}
        """, "§E{cw}")]
    // position 8 — field
    [InlineData("""
        §M{m001:R}
          §CL{c001:C:pub}
            §FLD{Action<i32>:onChange:pri} §E{cw}
            §MT{mt001:M:pub} () -> void
              §E{}
        """, "§FLD{Action<i32>:onChange:priv} §E{cw}")]
    // an empty row is not the same as no row (§3.5) and must survive
    [InlineData("""
        §M{m001:R}
          §F{f001:Apply:pub} (Func<i32,i32>:a §E{}, Func<i32,i32>:b) -> i32
            §E{}
            §R INT:0
        """, "Func<i32, i32>:a §E{}")]
    // the `eff` binder, written last in the type-parameter list
    [InlineData("""
        §M{m001:R}
          §F{f001:Map:pub}<T, eff e> (Func<i32,i32>:f §E{e}) -> i32
            §E{e}
            §R INT:0
        """, "<T,eff e>")]
    // …and written FIRST, which is why EffectParameterInfo carries an Ordinal
    [InlineData("""
        §M{m001:R}
          §F{f001:Map:pub}<eff e, T> (Func<i32,i32>:f §E{e}) -> i32
            §E{e}
            §R INT:0
        """, "<eff e,T>")]
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
