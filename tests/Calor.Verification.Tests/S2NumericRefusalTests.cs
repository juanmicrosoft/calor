using Calor.Compiler;
using Calor.Compiler.Migration;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// #1413 (0.24 S2, repair R-NUM): the frozen registration classifies NUM-NARROW-ARITH and
/// NUM-LITERAL-OVERSIZE as unsupported-refused (divergences D1 and D2) and NUM-OVERFLOW-CHECKED
/// as assumed (checked-arithmetic). S1 found Proven on all three (F-B1/N1-001..005, class
/// required-demotion-absent). The sources instantiate the registered templates T-NARROW-ARITH,
/// T-LIT-OVERSIZE, and T-OVF-CHECKED-2/-3. The tests need Z3 and fail, not skip, without it.
/// </summary>
public sealed class S2NumericRefusalTests
{
    private static string NativeArithmeticInput(string expression) => expression switch
    {
        "(< (- x INT:1) x)" => "x - 1 < x",
        "(== (- (+ x INT:1) INT:1) x)" => "x + 1 - 1 == x",
        "(> (+ x LONG:1) x)" => "x + 1L > x",
        "(>= (- x INT:-1) x)" => "x - (-1) >= x",
        "(* x y)" => "x * y",
        "(- x y)" => "x - y",
        _ => throw new ArgumentException("No native spelling registered for this arithmetic input.", nameof(expression))
    };

    private static string NativeIntegralInput(string literal)
    {
        var parts = literal.Split(':', 2);
        if (parts.Length == 2)
        {
            var style = System.Globalization.NumberStyles.Integer;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (parts[0] == "INT" && int.TryParse(parts[1], style, culture, out _))
                return parts[1];
            if (parts[0] == "LONG" && long.TryParse(parts[1], style, culture, out _))
                return parts[1] + "L";
            if (parts[0] == "UINT" && uint.TryParse(parts[1], style, culture, out _))
                return parts[1] + "U";
        }
        throw new ArgumentException("This input has no equivalent width-preserving native literal.", nameof(literal));
    }

    private static Calor.Compiler.Verification.Z3.ContractVerificationResult VerifySinglePostcondition(string source)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "these witnesses need Z3");
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var result = Program.Compile(source, "case.calr", options);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        var function = Assert.Single(options.VerificationResults!.Functions);
        return Assert.Single(function.PostconditionResults);
    }

    [Theory]
    [InlineData("i8", "i16", "+", ">=", "INT:-70000")]   // T-NARROW-ARITH (R1-NUM-NARROW-ARITH-005 shape)
    [InlineData("u8", "u8", "*", "<=", "INT:70000")]
    [InlineData("u16", "i8", "-", ">", "INT:-70000")]
    public void NarrowArithmetic_IsRefused(string t1, string t2, string op, string relation, string bound)
    {
        var source = $$"""
            module R1Case
              func Probe public ({{TypeMapper.ToSurfaceType(t1)}}:x, {{TypeMapper.ToSurfaceType(t2)}}:y) → int
                !
                => x {{op}} y {{relation}} {{NativeIntegralInput(bound)}}
                return 0
            """;
        Assert.Equal(ContractVerificationStatus.Unsupported, VerifySinglePostcondition(source).Status);
    }

    [Fact]
    public void Control_NarrowComparison_StaysProven()
    {
        const string source = """
            module R1Case
              func Probe public (sbyte:x) → int
                !
                => x <= 127
                return 0
            """;
        Assert.Equal(ContractVerificationStatus.Proven, VerifySinglePostcondition(source).Status);
    }

    [Fact]
    public void Control_SameValueSpelledLong_StaysProven()
    {
        const string source = """
            module R1Case
              func Probe public (long:x) → int
                !
                => x <= 9223372036854775807L
                return 0
            """;
        Assert.Equal(ContractVerificationStatus.Proven, VerifySinglePostcondition(source).Status);
    }

    [Theory]
    [InlineData("u32", "UINT:1702287129", "(< (- x INT:1) x)")]          // T-OVF-CHECKED-2 (R1-NUM-OVERFLOW-CHECKED-007 shape)
    [InlineData("i64", "LONG:1", "(< (- x INT:1) x)")]
    [InlineData("i32", "INT:0", "(== (- (+ x INT:1) INT:1) x)")]        // T-OVF-CHECKED-3
    public void OverflowSensitiveCheckedArithmetic_IsAssumed(string type, string lower, string claim)
    {
        var source = $$"""
            module R1Case
              func Probe public ({{TypeMapper.ToSurfaceType(type)}}:x) → int
                !
                requires x >= {{NativeIntegralInput(lower)}}
                => {{NativeArithmeticInput(claim)}}
                return 0
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.Equal(ProofStatus.Assumed, outcome.Status);
        Assert.Equal([Z3Verifier.CheckedArithmeticAssumption], outcome.Assumptions);
    }

    [Theory]
    [InlineData("i32", "(> (+ x LONG:1) x)")]     // promoted to 64-bit: no i32 input overflows
    [InlineData("u32", "(>= (- x INT:-1) x)")]    // u32 - int promotes to long: no overflow
    public void Control_ArithmeticThatCannotOverflow_StaysProven(string type, string claim)
    {
        var source = $$"""
            module R1Case
              func Probe public ({{TypeMapper.ToSurfaceType(type)}}:x) → int
                !
                => {{NativeArithmeticInput(claim)}}
                return 0
            """;
        Assert.Equal(ContractVerificationStatus.Proven, VerifySinglePostcondition(source).Status);
    }

    [Fact]
    public void OversizeLiteralBuiltWithoutTheLexer_IsRefused()
    {
        // Review round 1: an SDK-built 32-bit literal holding a 64-bit value is the same D2 form.
        Assert.True(Z3ContextFactory.IsAvailable, "this witness needs Z3");
        var span = Calor.Compiler.Parsing.TextSpan.Empty;
        var literal = new Calor.Compiler.Ast.IntLiteralNode(span, long.MaxValue) { IsLong = false };
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var result = verifier.VerifyPostcondition(
            [("x", "i64")], "i32", [],
            new Calor.Compiler.Ast.EnsuresNode(span,
                new Calor.Compiler.Ast.BinaryOperationNode(span, Calor.Compiler.Ast.BinaryOperator.LessOrEqual,
                    new Calor.Compiler.Ast.ReferenceNode(span, "x"), literal),
                null, new Calor.Compiler.Ast.AttributeCollection()));
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [Theory]
    [InlineData("i32", "u32", "(* x y)", true)]      // linux-arm64 case: 64-bit product, decided by widths
    [InlineData("u32", "i32", "(- x y)", true)]      // promoted to 64 bits: cannot overflow
    [InlineData("i32", "i32", "(* x y)", false)]     // control: 32-bit product can overflow
    public void OverflowSafety_IsDecidedFromOperandWidths(string leftType, string rightType, string arithmetic, bool staticallySafe)
    {
        // #1413 (S2 R-NUM, amendment 1.3.1): when the promoted result type always holds the
        // result, no solver query decides it, so the verdict cannot depend on solver time.
        var source = $$"""
            module R1Case
              func Probe public ({{TypeMapper.ToSurfaceType(leftType)}}:x, {{TypeMapper.ToSurfaceType(rightType)}}:y) → int
                !
                => ({{NativeArithmeticInput(arithmetic)}}) == ({{NativeArithmeticInput(arithmetic)}})
                return 0
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        if (staticallySafe)
            Assert.Equal(ProofStatus.Proven, outcome.Status);
        else
            Assert.Equal([Z3Verifier.CheckedArithmeticAssumption], outcome.Assumptions);
    }

    [Fact]
    public void OversizeLiteralRemovedBySimplification_IsStillRefused()
    {
        // Review round 2: the simplifier folded the conditional to 0 == 0 before the translator
        // saw the refused int-typed literal. Native source can no longer spell an int-typed
        // literal outside int32, so the D2 form is built at AST level (as an SDK caller can, see
        // OversizeLiteralBuiltWithoutTheLexer_IsRefused) and run through the pipeline's simplifier.
        Assert.True(Z3ContextFactory.IsAvailable, "this witness needs Z3");
        var span = Calor.Compiler.Parsing.TextSpan.Empty;
        var conditional = new Calor.Compiler.Ast.ConditionalExpressionNode(span,
            new Calor.Compiler.Ast.BoolLiteralNode(span, true),
            new Calor.Compiler.Ast.IntLiteralNode(span, 0),
            new Calor.Compiler.Ast.IntLiteralNode(span, 3000000000L) { IsLong = false });
        var condition = new Calor.Compiler.Ast.BinaryOperationNode(span, Calor.Compiler.Ast.BinaryOperator.Equal,
            conditional, new Calor.Compiler.Ast.IntLiteralNode(span, 0));
        var simplified = new ExpressionSimplifier().Simplify(condition);
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var result = verifier.VerifyPostcondition(
            [], "i32", [],
            new Calor.Compiler.Ast.EnsuresNode(span, simplified, null, new Calor.Compiler.Ast.AttributeCollection()));
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [Fact]
    public void OversizeLiteralInABodyBinding_IsRefusedNotThrown()
    {
        // Review round 2: the binding path passed a refused literal into an equality.
        Assert.True(Z3ContextFactory.IsAvailable, "this witness needs Z3");
        var span = Calor.Compiler.Parsing.TextSpan.Empty;
        var attributes = new Calor.Compiler.Ast.AttributeCollection();
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var result = verifier.VerifyPostcondition(
            [], "i32", [],
            new Calor.Compiler.Ast.EnsuresNode(span,
                new Calor.Compiler.Ast.BinaryOperationNode(span, Calor.Compiler.Ast.BinaryOperator.Equal,
                    new Calor.Compiler.Ast.ReferenceNode(span, "result"), new Calor.Compiler.Ast.IntLiteralNode(span, 0)),
                null, attributes),
            [
                new Calor.Compiler.Ast.BindStatementNode(span, "n", "i32", false,
                    new Calor.Compiler.Ast.IntLiteralNode(span, long.MaxValue) { IsLong = false }, attributes),
                new Calor.Compiler.Ast.ReturnStatementNode(span, new Calor.Compiler.Ast.IntLiteralNode(span, 0)),
            ]);
        Assert.NotEqual(ContractVerificationStatus.Proven, result.Status);
    }

    [Theory]
    [InlineData(null)]      // control: the current format is served
    [InlineData("1.20")]
    [InlineData("1.21")]
    public void ForgedProvenEntriesFromOlderFormats_AreNotServed(string? stamp)
    {
        // Review round 3: an entry written before R-NUM (format 1.20 or 1.21) may hold Proven for an
        // overflow-sensitive shape that is now Assumed.
        const string source = """
            module R1Case
              func Probe public (uint:x) → int
                !
                requires x >= 1702287129U
                => x - 1 < x
                return 0
            """;
        var directory = Path.Combine(Path.GetTempPath(), "calor-s2-num-forge-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            ProofStatus Verify()
            {
                var options = new CompilationOptions
                {
                    VerifyContracts = true,
                    VerificationCacheOptions = new VerificationCacheOptions { Enabled = true, CacheDirectory = directory },
                };
                Program.Compile(source, "case.calr", options);
                return Assert.Single(Assert.Single(options.VerificationResults!.Functions).PostconditionResults).EffectiveOutcome.Status;
            }
            Assert.Equal(ProofStatus.Assumed, Verify());
            var entries = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
            Assert.NotEmpty(entries);
            foreach (var file in entries)
            {
                var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
                if (json["proofStatus"]?.GetValue<string>() != "assumed")
                    continue;
                json["status"] = (int)ContractVerificationStatus.Proven;
                json["proofStatus"] = "proven";
                json["assumptions"] = null;
                if (stamp != null)
                    json["version"] = stamp;
                File.WriteAllText(file, json.ToJsonString());
            }
            Assert.Equal(stamp == null ? ProofStatus.Proven : ProofStatus.Assumed, Verify());
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void WrappedMagnitudeLiteral_IsRefused()
    {
        // Review round 3: a positive 32-bit signed literal with magnitude ulong.MaxValue wraps its
        // Value to -1; the refusal checks sign and magnitude.
        Assert.True(Z3ContextFactory.IsAvailable, "this witness needs Z3");
        var span = Calor.Compiler.Parsing.TextSpan.Empty;
        var literal = new Calor.Compiler.Ast.IntLiteralNode(span, ulong.MaxValue,
            Calor.Compiler.Parsing.IntegerLiteralSign.Positive, Calor.Compiler.Parsing.IntegerLiteralBase.Decimal,
            Calor.Compiler.Parsing.IntegerLiteralWidth.Bits32, Calor.Compiler.Parsing.IntegerLiteralSignedness.Signed);
        using var ctx = Z3ContextFactory.Create();
        using var verifier = new Z3Verifier(ctx);
        var result = verifier.VerifyPostcondition(
            [], "i32", [],
            new Calor.Compiler.Ast.EnsuresNode(span,
                new Calor.Compiler.Ast.BinaryOperationNode(span, Calor.Compiler.Ast.BinaryOperator.LessThan,
                    literal, new Calor.Compiler.Ast.IntLiteralNode(span, 0)),
                null, new Calor.Compiler.Ast.AttributeCollection()));
        Assert.Equal(ContractVerificationStatus.Unsupported, result.Status);
    }

    [Fact]
    public void InferredWidthLiteral_HasItsOwnCacheKey_AndTheFormatEvictsOlderEntries()
    {
        var span = Calor.Compiler.Parsing.TextSpan.Empty;
        var hasher = new ContractHasher();
        var inferred = hasher.GetCanonicalExpression(new Calor.Compiler.Ast.IntLiteralNode(span, 3_000_000_000) { WidthInferred = true });
        var spelled = hasher.GetCanonicalExpression(new Calor.Compiler.Ast.IntLiteralNode(span, 3_000_000_000));
        Assert.NotEqual(inferred, spelled);
        // Entries of 1.21 and earlier may hold Proven for the forms R-NUM refuses or demotes.
        Assert.True(int.Parse(VerificationCacheEntry.CurrentFormatVersion.Split('.')[1]) >= 22,
            VerificationCacheEntry.CurrentFormatVersion);
    }

    [Fact]
    public void EntailedCheckedArithmetic_IsAssumedWithTheCheckedArithmeticAssumption()
    {
        const string source = """
            module R1Case
              func Probe public (uint:x) → int
                !
                requires x >= 1702287129U
                => x - 1 < x
                return 0
            """;
        var outcome = VerifySinglePostcondition(source).EffectiveOutcome;
        Assert.Equal(ProofStatus.Assumed, outcome.Status);
        Assert.Equal([Z3Verifier.CheckedArithmeticAssumption], outcome.Assumptions);
    }
}
