using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using System.Text.Json.Nodes;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1413 (0.24 S2) regression witnesses for the S1 sweep row CACHE-LITERAL-WIDTH
/// (findings F-B1/N1-031..034). The verification cache keyed integer literals by value
/// only, so `x + INT:1` and `x + LONG:1` shared one entry: compiling the LONG text first
/// made a warm compile of the INT text report the LONG text's Proven (a false
/// unconditional proof that elides the guard: under unchecked overflow, x = int.MaxValue
/// wraps to a negative sum), and the reverse order served a stale Refuted.
/// The cases are the registered S1 sources, verbatim. The tests need Z3 and fail (not
/// skip) without it: a skipped soundness witness would read as a pass.
/// </summary>
public sealed class S2CacheLiteralWidthTests : IDisposable
{
    private const string IntSource = """
        §M{m1:R1Case:overflow=unchecked}
          §F{f1:Probe:pub} (i32:x) -> i32
            §E{}
            §Q (== x INT:2147483647)
            §S (> (+ x INT:1) INT:0)
            §R INT:0
        """;

    private const string LongSource = """
        §M{m1:R1Case:overflow=unchecked}
          §F{f1:Probe:pub} (i32:x) -> i32
            §E{}
            §Q (== x INT:2147483647)
            §S (> (+ x LONG:1) INT:0)
            §R INT:0
        """;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "calor-s2-cache-width", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ProofStatus Verify(string source, string cacheDirectory, bool cached)
    {
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            ContractMode = ContractMode.Debug,
            VerificationCacheOptions = new VerificationCacheOptions
            {
                Enabled = cached,
                CacheDirectory = cacheDirectory,
            },
        };
        var result = Program.Compile(source, "case.calr", options);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        var function = Assert.Single(options.VerificationResults!.Functions);
        return Assert.Single(function.PostconditionResults).EffectiveOutcome.Status;
    }

    [Fact]
    public void ColdVerdicts_DifferByLiteralWidth()
    {
        // Independent oracle (S1 R1-O1, reproduced in C#): int.MaxValue + 1 wraps to
        // int.MinValue (property false); int.MaxValue + 1L is 2^31 (property true).
        Assert.True(unchecked(int.MaxValue + 1) <= 0);
        Assert.True(int.MaxValue + 1L > 0);

        Assert.Equal(ProofStatus.Refuted, Verify(IntSource, Path.Combine(_root, "cold-int"), cached: false));
        Assert.Equal(ProofStatus.Proven, Verify(LongSource, Path.Combine(_root, "cold-long"), cached: false));
    }

    private static string[] Entries(string dir)
        => Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
            : [];

    [Fact]
    public void WarmCache_PrimedWithLongLiteral_DoesNotProveIntLiteral()
    {
        // R1-CACHE-LITERAL-WIDTH-001: prime = LONG text, final = INT text.
        var dir = Path.Combine(_root, "warm-001");

        Assert.Equal(ProofStatus.Proven, Verify(LongSource, dir, cached: true));
        var primed = Entries(dir).Length;
        Assert.True(primed > 0, "the prime compile wrote no cache entry; the warm case was not exercised");

        Assert.Equal(ProofStatus.Refuted, Verify(IntSource, dir, cached: true));
        // The INT text missed the LONG entry and wrote its own: the keys differ.
        Assert.True(Entries(dir).Length > primed, "the INT text was served from the LONG text's key");
    }

    [Fact]
    public void WarmCache_PrimedWithIntLiteral_DoesNotRefuteLongLiteral()
    {
        // R1-CACHE-LITERAL-WIDTH-002: prime = INT text, final = LONG text.
        var dir = Path.Combine(_root, "warm-002");

        Assert.Equal(ProofStatus.Refuted, Verify(IntSource, dir, cached: true));
        var primed = Entries(dir).Length;
        Assert.True(primed > 0, "the prime compile wrote no cache entry; the warm case was not exercised");

        Assert.Equal(ProofStatus.Proven, Verify(LongSource, dir, cached: true));
        Assert.True(Entries(dir).Length > primed, "the LONG text was served from the INT text's key");
    }

    /// <summary>
    /// Rewrites every entry for the INT text to claim Proven. With the current format the
    /// forged entry is served (control: proves the warm path is read); stamped 1.19 (the previous format) it is
    /// rejected and the INT text is re-verified as Refuted.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreBumpEntries_AreNotServed(bool stampOldFormat)
    {
        var dir = Path.Combine(_root, stampOldFormat ? "old-format" : "current-format");
        Assert.Equal(ProofStatus.Refuted, Verify(IntSource, dir, cached: true));
        var entries = Entries(dir);
        Assert.NotEmpty(entries);
        foreach (var file in entries)
        {
            var json = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            if (json["proofStatus"]?.GetValue<string>() != "refuted")
                continue;
            json["status"] = (int)ContractVerificationStatus.Proven;
            json["proofStatus"] = "proven";
            json["counterexampleDescription"] = null;
            json["counterexampleBindings"] = null;
            if (stampOldFormat)
                json["version"] = "1.19";
            File.WriteAllText(file, json.ToJsonString());
        }

        var expected = stampOldFormat ? ProofStatus.Refuted : ProofStatus.Proven;
        Assert.Equal(expected, Verify(IntSource, dir, cached: true));
    }

    [Fact]
    public void PostconditionKey_OutputTypeCannotForgeDelimiters()
    {
        // Review witness (SDK-built AST): an output type containing ":PRECS:" text made
        // two different (output, precondition) pairs serialize identically.
        var span = new TextSpan(0, 0, 1, 1);
        var attributes = new AttributeCollection();
        var hasher = new ContractHasher();
        var parameters = new List<(string Name, string TypeName)> { ("a:PRECS:BOOL:true", "bool") };
        var p = new ReferenceNode(span, "a:PRECS:BOOL:true");
        var post = new EnsuresNode(span, p, null, attributes);
        var body = new List<StatementNode> { new ReturnStatementNode(span, new IntLiteralNode(span, 0)) };

        var prime = hasher.HashPostcondition(
            parameters, "i32", [new RequiresNode(span, p, null, attributes)], post, body);
        var forged = hasher.HashPostcondition(
            parameters, "i32:PRECS:REF:17#a",
            [new RequiresNode(span, new BoolLiteralNode(span, true), null, attributes)], post, body);
        Assert.NotEqual(prime, forged);
    }

    [Fact]
    public void CacheKey_UnpairedSurrogateDoesNotCollideWithReplacementCharacter()
    {
        // Review round 2 witness (SDK-built AST): UTF-8 hashing mapped "p\uFFFD" and
        // "p\uD800" to the same bytes, so a warm cache primed with the first name's Proven
        // served it to the second (an unknown variable, Unsupported when verified cold).
        var span = new TextSpan(0, 0, 1, 1);
        var attributes = new AttributeCollection();
        const string replacement = "p\uFFFD";
        const string surrogate = "p\uD800";
        var parameters = new List<(string Name, string TypeName)> { (replacement, "bool") };
        var pre = new List<RequiresNode> { new(span, new ReferenceNode(span, replacement), null, attributes) };
        var body = new List<StatementNode> { new ReturnStatementNode(span, new IntLiteralNode(span, 0)) };
        var primePost = new EnsuresNode(span, new ReferenceNode(span, replacement), null, attributes);
        var finalPost = new EnsuresNode(span, new ReferenceNode(span, surrogate), null, attributes);

        var hasher = new ContractHasher();
        Assert.NotEqual(
            hasher.HashPostcondition(parameters, "i32", pre, primePost, body),
            hasher.HashPostcondition(parameters, "i32", pre, finalPost, body));

        var options = new VerificationCacheOptions
        {
            Enabled = true,
            CacheDirectory = Path.Combine(_root, "surrogate"),
        };
        using (var cache = new VerificationCache(options, keyScope: "scope\uD800"))
        {
            cache.CachePostconditionResult(parameters, "i32", pre, primePost, body,
                new Calor.Compiler.Verification.Z3.ContractVerificationResult(ContractVerificationStatus.Proven));
            Assert.True(cache.TryGetPostconditionResult(parameters, "i32", pre, primePost, body, out _));
            Assert.False(cache.TryGetPostconditionResult(parameters, "i32", pre, finalPost, body, out _));
        }
        using (var cache = new VerificationCache(options, keyScope: "scope\uFFFD"))
        {
            // The key scope is hashed losslessly too.
            Assert.False(cache.TryGetPostconditionResult(parameters, "i32", pre, primePost, body, out _));
        }
    }

    [Fact]
    public void PostconditionKey_InferredBindingTypeDiffersFromQuestionMarkSpelling()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var attributes = new AttributeCollection();
        var hasher = new ContractHasher();
        var post = new EnsuresNode(span, new BoolLiteralNode(span, true), null, attributes);
        List<StatementNode> Body(string? type) =>
        [
            new BindStatementNode(span, "t", type, false, new IntLiteralNode(span, 1), attributes),
            new ReturnStatementNode(span, new ReferenceNode(span, "t")),
        ];

        Assert.NotEqual(
            hasher.HashPostcondition([], "i32", [], post, Body(null)),
            hasher.HashPostcondition([], "i32", [], post, Body("?")));
    }

    private static ExpressionNode ParsePostcondition(string literal)
    {
        var source = $$"""
            §M{m1:M}
              §F{f1:P:pub} (i64:x) -> i32
                §E{}
                §S (> x {{literal}})
                §R INT:0
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));
        return Assert.Single(Assert.Single(module.Functions).Postconditions).Condition;
    }

    [Theory]
    [InlineData("INT:1", "LONG:1")]
    [InlineData("INT:1", "UINT:1")]
    [InlineData("LONG:1", "ULONG:1")]
    [InlineData("UINT:1", "ULONG:1")]
    [InlineData("ULONG:18446744073709551615", "LONG:-1")]
    public void CanonicalKey_DistinguishesLiteralTyping(string left, string right)
    {
        var hasher = new ContractHasher();
        var a = hasher.GetCanonicalExpression(ParsePostcondition(left));
        var b = hasher.GetCanonicalExpression(ParsePostcondition(right));
        Assert.NotEqual(a, b);
        Assert.False(hasher.SawUnhashedKind);
    }

    [Fact]
    public void CanonicalKey_DistinguishesRealLiteralTyping()
    {
        var span = new TextSpan(0, 0, 1, 1);
        var hasher = new ContractHasher();
        var asDouble = hasher.GetCanonicalExpression(new FloatLiteralNode(span, 0.5));
        var asSingle = hasher.GetCanonicalExpression(new FloatLiteralNode(span, 0.5) { IsSingle = true });
        var asDecimal = hasher.GetCanonicalExpression(new FloatLiteralNode(span, 0.5, isDecimal: true));
        Assert.Equal(3, new[] { asDouble, asSingle, asDecimal }.Distinct().Count());
    }

    [Fact]
    public void FormatVersion_EvictsPreFixEntries()
    {
        // Entries of 1.19 and earlier were written under value-only literal keys. Later repairs
        // (#1413 S2) bump the format again, so the pin is a lower bound.
        var minor = int.Parse(VerificationCacheEntry.CurrentFormatVersion.Split('.')[1]);
        Assert.StartsWith("1.", VerificationCacheEntry.CurrentFormatVersion);
        Assert.True(minor >= 20, VerificationCacheEntry.CurrentFormatVersion);
    }
}
