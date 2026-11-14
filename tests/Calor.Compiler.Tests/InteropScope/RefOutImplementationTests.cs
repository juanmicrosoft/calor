using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Effects;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Verification.Obligations;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests.InteropScope;

/// <summary>
/// #943 (0.25 F1): ref/out/in arguments per the D1 contract (#1427). Language rows compile Calor
/// and check the refusal or acceptance (with a negative control for every refusal); conversion
/// rows convert every R0 F1 fixture, the F1 interaction fixtures and every D1 C# witness on the
/// library, CLI and MCP surfaces, run, and compare with the original C#. Native default output
/// must compile strictly; marked preservation is not a certificate. Lowering-shape comparisons
/// use an explicit noncertifying candidate, not a native certificate.
/// </summary>
public class RefOutImplementationTests
{
    public static TheoryData<string, string, string, Surface> GenericRefRows()
    {
        var data = new TheoryData<string, string, string, Surface>();
        var witnesses = new (string Members, string Body, string Expected)[]
        {
            ("static void Swap<T>(ref T a, ref T b) { T temp = a; a = b; b = temp; }",
                "int a = 1, b = 2; Swap<int>(ref a, ref b); return a + \":\" + b;", "2:1"),
            ("static void Swap<T>(ref T a, ref T b) { T temp = a; a = b; b = temp; }",
                "int a = 3; Swap(ref a, ref a); return a.ToString();", "3"),
            ("static T Write<T>(ref T a, ref T b, T value) { a = value; return b; }",
                "int a = 1; int seen = Write(ref a, ref a, 9); return seen + \":\" + a;", "9:9"),
            ("static T Clear<T>(ref T a, ref T b) { a = default(T); return b; }",
                "string a = \"first\"; string seen = Clear<string>(ref a, ref a); return (seen == null) + \":\" + (a == null);", "True:True"),
            ("static void Swap<T>(ref T a, ref T b) { T temp = a; a = b; b = temp; }",
                "string a = \"first\", b = \"second\"; Swap(ref a, ref b); return a + \":\" + b;", "second:first"),
            ("static T Write<T>(ref T a, ref T b, T value) { a = value; return b; }",
                "int[] a = new[] { 1, 2 }; int seen = Write(ref a[0], ref a[0], 9); return seen + \":\" + a[0];", "9:9"),
            ("static T Write<T>(ref T a, ref T b, T value) { a = value; return b; }",
                "int[] a = new[] { 1, 2 }; int seen = Write(ref a[0], ref a[1], 9); return seen + \":\" + a[0];", "2:9"),
            ("public struct Cell { public int N; } static T Write<T>(ref T a, ref T b, T value) { a = value; return b; }",
                "Cell a = new Cell { N = 1 }; Cell value = new Cell { N = 7 }; Cell seen = Write(ref a, ref a, value); return seen.N + \":\" + a.N;", "7:7"),
            ("static void Set<T>(ref T a, T value) { a = value; } static void Forward<T>(ref T a, T value) { Set(ref a, value); }",
                "int a = 1; Forward(ref a, 7); return a.ToString();", "7"),
            ("static void Swap<T>(ref T a, ref T b) { T temp = a; a = b; b = temp; }",
                "int a = 1, b = 2; Swap<int>(b: ref a, a: ref b); return a + \":\" + b;", "2:1"),
            ("static T Write<T>(ref T a, ref T b, T value) { a = value; return b; }",
                "int a = 1, b = 2; int seen = Write<int>(value: 9, b: ref a, a: ref b); return seen + \":\" + a + \":\" + b;", "1:1:9"),
        };
        foreach (var witness in witnesses)
            foreach (var surface in Enum.GetValues<Surface>())
                data.Add(witness.Members, witness.Body, witness.Expected, surface);
        return data;
    }

    [Theory]
    [MemberData(nameof(GenericRefRows))]
    public async Task GenericRefMethods_PreserveActualCallerStorage(
        string members, string body, string expected, Surface surface)
    {
        var source = "public static class Probe { " + members
            + " public static string Run() { " + body + " } }";
        var (calor, losses) = await ConvertAsync(source, surface);
        Assert.Empty(losses);
        Assert.DoesNotContain("§CS", calor);
        RawFrameAssertions.AssertFramed(calor, Calor.Compiler.Parsing.TokenKind.RawCSharp, present: false);
        Assert.Contains("ref T:a", calor);
        var parseDiagnostics = new Compiler.Diagnostics.DiagnosticBag();
        var module = new Compiler.Parsing.Parser(
            new Compiler.Parsing.Lexer(calor, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics).Parse();
        Assert.False(parseDiagnostics.HasErrors, string.Join("; ", parseDiagnostics.Errors));
        var generated = new CSharpEmitter().Emit(module);
        Assert.Contains("ref T a", generated);
        Assert.Equal(expected, Run(source));
        Assert.Equal(expected, Run(generated));
    }

    [Theory]
    [InlineData("Keep<string?>(ref b)", false)]
    [InlineData("let int:n = 1", true)]
    public void GenericRefCall_InvalidatesPotentiallyAliasedNarrowing(string step, bool keepsNarrowing)
    {
        var errors = Errors($$"""
              func Keep<T> public (ref T:value) → void
                ! mut
                set value value
              func Check public (ref string?:a, ref string?:b) → int
                ! mut
                if a != null
                  {{step}}
                  let string:z = a
                  return z.Length
                return 0
            """);
        Assert.Equal(keepsNarrowing ? [] : ["Calor0272"], errors);
    }

    [Theory]
    [InlineData("ref T?", "where T : class", "ref-parameter", "method")]
    [InlineData("scoped ref T", "", "scoped-parameter", "scoped-parameter")]
    [InlineData("ref readonly T", "", "ref-parameter", "method")]
    public void GenericRefShapesOutsideTheNativeSlice_KeepTheirDeclaration(
        string parameterType, string constraint, string feature, string loss)
    {
        var source = $"public static class Probe {{ static void Use<T>({parameterType} value) {constraint} {{ }} }}";
        var conversion = new CSharpToCalorConverter().Convert(source);
        RawFrameAssertions.AssertFramed(conversion.CalorSource);
        Assert.Contains(conversion.Issues, issue => issue.Feature == feature);
        Assert.Contains(conversion.Losses, item => item.Feature == loss);
    }

    private const string Prelude = """
        module P
          func Bump public (ref int:x) → void
            ! mut
            set x x + 1
          func SetOut public (out int:x) → void
            ! mut
            set x 7
          func SetNeg public (out int:x) → void
            ! mut
            set x -5
          func Twice public (in int:x) → int
            !
            return x * 2
          func Id public (int:x) → int
            !
            return x

        """;

    private static string[] Errors(string body, bool verify = false, CompilationOptions? options = null)
    {
        options ??= new CompilationOptions
        {
            VerifyContracts = verify, VerifyRefinements = verify,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var result = Program.Compile(Prelude + body, "refout.calr", options);
        return result.Diagnostics.Where(d => d.IsError).Select(d => d.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
    }

    // Each refusal (Calor0212) next to the closest accepted form: the operand rule, not the call, decides.
    [Theory]
    [InlineData("property",
        "  class K public\n    property int:N public\n      get\n      set\n    func Run public () → void\n      ! mut\n      Bump(ref N)",
        "  class K public\n    field int:N public\n    func Run public () → void\n      ! mut\n      Bump(ref N)")]
    [InlineData("static readonly field outside its static constructor",
        "  module K public\n    field int:n public readonly\n    func Run public () → void\n      ! mut\n      Bump(ref n)",
        "  module K public\n    field int:n public readonly\n    func Run public () → int\n      !\n      return Twice(in n)")]
    [InlineData("in parameter passed as ref",
        "  func Run public (in int:p) → void\n    ! mut\n    Bump(ref p)",
        "  func Run public (in int:p) → int\n    !\n    return Twice(in p)")]
    [InlineData("call result",
        "  func Run public () → void\n    ! mut\n    Bump(ref Twice(in 3))",
        "  func Run public () → void\n    ! mut\n    var int:v = Id(3)\n    Bump(ref v)")]
    [InlineData("list indexer element",
        "  func Run public (List<int>:xs) → void\n    ! mut\n    Bump(ref xs[0])",
        "  func Run public (int[]:xs) → void\n    ! mut\n    Bump(ref xs[0])")]
    [InlineData("field of an in struct parameter",
        "  class S public struct\n    field int:X public\n  func Run public (in S:s) → void\n    ! mut\n    Bump(ref s.X)",
        "  class S public struct\n    field int:X public\n  func Run public (ref S:s) → void\n    ! mut\n    Bump(ref s.X)")]
    [InlineData("non-generic indexer element",
        "  func Run public (System.Collections.ArrayList:xs) → void\n    ! mut\n    SetOut(out xs[0])",
        "  func Run public (int[]:xs) → void\n    ! mut\n    SetOut(out xs[0])")]
    [InlineData("scalar metadata property",
        "  func Run public (string:s) → void\n    ! mut\n    Bump(ref s.Length)",
        "  func Run public (string:s) → void\n    ! mut\n    var int:n = s.Length\n    Bump(ref n)")]
    [InlineData("readonly intermediate field of a struct path",
        "  class Inner public struct\n    field int:Y public\n  class Outer public struct\n    field Inner:X public readonly\n  func Run public (Outer[]:a) → void\n    ! mut\n    Bump(ref a[0].X.Y)",
        "  class Inner public struct\n    field int:Y public\n  class Outer public struct\n    field Inner:X public\n  func Run public (Outer[]:a) → void\n    ! mut\n    Bump(ref a[0].X.Y)")]
    [InlineData("readonly field of another instance in a constructor",
        "  class K public\n    field int:n public readonly\n    ctor public (K:other)\n      Bump(ref other.n)",
        "  class K public\n    field int:n public readonly\n    ctor public (K:other)\n      Bump(ref n)")]
    // Codex round 3: a readonly field reached through a struct path, a property at the end of
    // one, a metadata constant, and a two-index indexer.
    [InlineData("readonly field at the end of a dotted struct path",
        "  class Inner public struct\n    field int:N public readonly\n  class Outer public struct\n    field Inner:F public\n  func Run public (Outer:s) → void\n    ! mut\n    Bump(ref s.F.N)",
        "  class Inner public struct\n    field int:N public\n  class Outer public struct\n    field Inner:F public\n  func Run public (Outer:s) → void\n    ! mut\n    Bump(ref s.F.N)")]
    [InlineData("metadata constant",
        "  func Run public () → void\n    ! mut\n    Bump(ref Int32.MaxValue)",
        "  func Run public () → void\n    ! mut\n    var int:m = Int32.MaxValue\n    Bump(ref m)")]
    [InlineData("readonly reference-typed field in an in struct",
        "  class Box public\n    field int:N public readonly\n  class S public struct\n    field Box:B public\n  func Run public (in S:s) → void\n    ! mut\n    Bump(ref s.B.N)",
        "  class Box public\n    field int:N public\n  class S public struct\n    field Box:B public\n  func Run public (in S:s) → void\n    ! mut\n    Bump(ref s.B.N)")]
    [InlineData("two-index indexer element (Codex R3-5)",
        "  class Grid public\n    this[int:r, int:c] → int\n      get\n        return 1\n  func Run public (Grid:g) → void\n    ! mut\n    Bump(ref g[0, 0])",
        "  func Run public (int[,]:g) → void\n    ! mut\n    Bump(ref g[0, 0])")]
    [InlineData("discard bound to a field named _",
        "  class K public\n    field int:_ public\n    func Run public () → void\n      ! mut\n      SetOut(out _)",
        "  class K public\n    func Run public () → void\n      ! mut\n      SetOut(out _)")]
    public void InvalidOperand_IsRefusedBeforeCodegen_AndItsControlCompiles(string shape, string refused, string accepted)
    {
        Assert.True(Errors(refused).SequenceEqual(["Calor0212"]), $"{shape}: [{string.Join(", ", Errors(refused))}]");
        // The control may still hit an unrelated effect gap (struct members are unknown calls), never
        // a by-reference refusal or a generated-C# failure.
        Assert.True(!Errors(accepted).Any(code => code is "Calor0212" or "Calor1002" or "Calor0208"),
            $"{shape} control: [{string.Join(", ", Errors(accepted))}]");
        Assert.DoesNotContain(Errors(accepted), code =>
            code.StartsWith("Calor00", StringComparison.Ordinal)
            || code.StartsWith("Calor01", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Bump(ref k.n)", "!", "Calor0410")]
    [InlineData("SetOut(out k.n)", "!", "Calor0410")]
    [InlineData("Bump(ref k.n)", "! mut", null)]
    [InlineData("Twice(in k.n)", "!", null)]
    [InlineData("set k.n 3", "!", "Calor0410")]
    public void ReceiverFieldWrite_ChargesLikeAssign(string statement, string effects, string? error)
    {
        var errors = Errors($$"""
              class K public
                field int:n public
              func Run public (K:k) → void
                {{effects}}
                {{statement}}
            """);
        Assert.Equal(error is null ? [] : [error], errors);
    }

    // RO-NULL-2 controls: invalidation is limited to by-reference parameters that may alias.
    [Theory]
    [InlineData("(ref string?:a)", "let int:z = 1", true)]
    [InlineData("(string?:a)", "ClearSlot()", true)]
    [InlineData("(ref string?:a, string?:b)", "set b null", true)]
    [InlineData("(ref string?:a, ref string?:b)", "let int:z = 1", true)]
    [InlineData("(ref string?:a)", "ClearSlot()", false)]
    [InlineData("(ref string?:a, ref string?:b)", "set b null", false)]
    [InlineData("(in string?:a, ref string?:b)", "set b null", false)]
    [InlineData("(ref string?:a, Func<int>:f ! unknown)", "let int:z = f()", false)]   // expression-target call (Codex R2-1)
    public void AliasNarrowing_IsInvalidatedOnlyWhenStorageMayBeShared(string parameters, string step, bool keepsNarrowing)
    {
        var errors = Errors($$"""
              module K public
                field string?:slot public
                func ClearSlot public () → void
                  ! mut
                  set slot null
                func Check public {{parameters}} → int
                  ! mut
                  if a != null
                    {{step}}
                    let string:u = a
                    return u.Length
                  return 0
            """);
        Assert.Equal(keepsNarrowing ? [] : ["Calor0272"], errors);
    }

    // Codex round 1: a write later in a loop body reaches the next iteration's reads (RO-NULL-2).
    [Theory]
    [InlineData("(ref string?:a, ref string?:b)", "a", "set b null", false)]
    [InlineData("(ref string?:a, ref string?:b)", "a", "let int:z = 1", true)]
    [InlineData("(string?:a)", "a", "Clear(ref a)", false)]
    [InlineData("(string?:a)", "a", "Keep(a)", true)]
    [InlineData("(ref string?:a, string?:b)", "a", "let int:z = (b ?? throw null).Length", true, "! mut, throw")]
    [InlineData("(ref string?:a, string:b)", "a", "let int:z = b.Length", true)]
    [InlineData("(ref string?:a, string?:b)", "a", "let int:z = (Read(b) ?? throw null).Length", false, "! mut, throw")]
    public void LoopBackEdge_EndsNarrowingBeforeTheBody(string parameters, string name, string step, bool keepsNarrowing,
        string effects = "! mut")
    {
        var errors = Errors($$"""
              func Clear public (ref string?:s) → void
                ! mut
                set s null
              func Keep public (string?:s) → void
                !
                return
              func Read private (string?:s) → string?
                return s
              func Check public {{parameters}} → int
                {{effects}}
                if {{name}} != null
                  for i from 0 to 1 step 1
                    let string:u = {{name}}
                    {{step}}
                return 0
            """);
        Assert.Equal(keepsNarrowing ? [] : ["Calor0272"], errors);
    }

    [Fact]
    public void SourcePropertyNamedLength_StillEndsAliasedNarrowing()
    {
        var result = Program.Compile(Prelude + """
              class Box public
                field string?:Text public
                property int:Length private
                  get
                    set Text null
                    return 0
                func Check public (ref string?:a) → int
                  ! mut
                  if a != null
                    for i from 0 to 1 step 1
                      let string:u = a
                      let int:z = this.Length
                  return 0
            """, "source-length-alias.calr");
        Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code.StartsWith("Calor00", StringComparison.Ordinal)
            || diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal));
        var errors = result.Diagnostics.Errors.Select(diagnostic => diagnostic.Code).Distinct().ToArray();
        Assert.Equal(new[] { "Calor0272" }, errors);
    }

    // Codex round 3: an overloaded operator and a disposal can run code that writes aliased storage.
    [Theory]
    [InlineData("let Box:y = x + x", false)]
    [InlineData("let int:y = 1 + 2", true)]
    public void OverloadedOperator_EndsAliasedNarrowing(string step, bool keepsNarrowing)
    {
        var errors = Errors($$"""
              class Box public
                op + (Box:x, Box:y) → Box
                  !
                  return x
              module K public
                func Check public (ref string?:a, Box:x) → int
                  ! mut
                  if a != null
                    {{step}}
                    let string:u = a
                    return u.Length
                  return 0
            """);
        Assert.Equal(keepsNarrowing ? [] : ["Calor0272"], errors);
    }

    [Fact]
    public void UsingInALoop_EndsAliasedNarrowingAtTheBackEdge()
    {
        var errors = Errors("""
              class D public
                implements System.IDisposable
                func Dispose public () → void
                  !
                  return
              module K public
                func Check public (ref string?:a, D:d) → int
                  ! mut
                  if a != null
                    for i from 0 to 1 step 1
                      let string:u = a
                      using r = d
                        let int:z = 1
                  return 0
            """);
        Assert.Contains("Calor0272", errors);
    }

    // Soundness: no proof obligation is Discharged from a fact a by-reference write may have killed.
    [Theory]
    [InlineData("Bump(ref x)", "Unsupported")]
    [InlineData("SetNeg(out x)", "Unsupported")]
    [InlineData("if x > 0\n    SetNeg(out x)", "Unsupported")]
    [InlineData("let int:d = Twice(in x)", "Discharged")]
    [InlineData("let int:d = Id(x)", "Discharged")]
    public void ByReferenceWrite_NeverDischargesAStaleFact(string call, string expected)
    {
        Assert.True(Z3ContextFactory.IsAvailable, "proof cases need Z3");
        var options = new CompilationOptions
        {
            VerifyContracts = true, VerifyRefinements = true, ContractMode = ContractMode.Debug,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var errors = Errors($$"""
              func Run public (int:x) → int
                ! mut
                requires x > 0
                {{call}}
                proof x > 0
                return x
            """, options: options);
        Assert.Empty(errors);
        var proof = Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation);
        Assert.Equal(expected, proof.Status.ToString());
    }

    // D1-ANA-06 (RO-ANA-5): the division checker must not keep a ref operand's pre-call value,
    // in either direction: d == 1 is no longer "safe" and d == 0 no longer "guaranteed" after Zero(ref d).
    [Theory]
    [InlineData(1, "let int:z = 1", null)]
    [InlineData(1, "Zero(ref d)", false)]
    [InlineData(0, "let int:z = 1", true)]
    [InlineData(0, "Zero(ref d)", false)]
    public void DivisionByZeroChecker_DoesNotUseThePreCallValueOfARefOperand(int initial, string step, bool? guaranteed)
    {
        var result = Program.Compile(Prelude + $$"""
              func Zero public (ref int:x) → void
                ! mut
                set x 0
              func Run public () → int
                ! mut
                var int:d = {{initial}}
                {{step}}
                return 10 / d
            """, "ana06.calr", new CompilationOptions
            {
                EnableVerificationAnalyses = true,
                VerificationAnalysisOptions = new Calor.Compiler.Analysis.VerificationAnalysisOptions(),
            });
        Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code.StartsWith("Calor00", StringComparison.Ordinal)
            || diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal));
        var findings = result.Diagnostics.Where(d => d.Code == Calor.Compiler.Diagnostics.DiagnosticCode.DivisionByZero).ToList();
        if (guaranteed is null)
            Assert.Empty(findings);
        else
            Assert.Equal(guaranteed.Value, Assert.Single(findings).IsError);
    }

    // A module-level private helper retains its inferred row while a direct call
    // charges writes according to actual caller storage.
    [Theory]
    [InlineData("Inc(ref x)", "! alloc", "", "Unsupported")]
    [InlineData("Inc(ref (x + 1))", "! alloc", "Calor0212", null)]
    [InlineData("Inc(ref arr[0])", "! alloc", "Calor0410", null)]
    [InlineData("Inc(ref arr[0])", "! alloc, mut", "", "Discharged")]
    public void PrivateHelper_ByReferenceArguments_FollowTheContract(string call, string effects, string error, string? proof)
    {
        var options = new CompilationOptions
        {
            VerifyContracts = true, VerifyRefinements = true, ContractMode = ContractMode.Debug,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false },
        };
        var result = Program.Compile($$"""
            module P
              func Run public (int:x) → int
                {{effects}}
                requires x > 0
                let int[]:arr = new int[] {1}
                {{call}}
                proof x > 0
                return x
              func Inc private (ref int:v) → void
                set v v + 1
            """, "local.calr", options);
        var errors = result.Diagnostics.Where(d => d.IsError).Select(d => d.Code).Distinct().ToArray();
        Assert.Equal(error.Length == 0 ? [] : [error], errors);
        if (proof != null)
        {
            Assert.Contains("Inc(ref ", result.GeneratedCode);
            Assert.Equal(proof, Assert.Single(options.ObligationResults!.Obligations, o => o.Kind == ObligationKind.ProofObligation).Status.ToString());
        }
    }

    [Theory]
    [InlineData("int:x", "Inc(ref x)", "private", "set v v + 1", false)]
    [InlineData("int:x", "Inc(v: ref x)", "private", "set v v + 1", false)]
    [InlineData("ref int:x", "Inc(ref x)", "private", "set v v + 1", true)]
    [InlineData("int:x", "Inc(ref x)", "public", "! mut\nset v v + 1", true)]
    [InlineData("int:x", "Inc(ref x)", "private", "set v v + 1\nset State.Value v", true)]
    [InlineData("int:x", "Inc(ref x)", "private", "System.Threading.Interlocked.Increment(ref v)", true)]
    public void DirectPrivateByRefCall_OnlyInstantiatesProvedLocalStorage(
        string parameter, string call, string visibility, string body, bool hasForbiddenEffect)
    {
        var result = Program.Compile($$"""
            module P
              class State public
                field int:Value public static
              func Run public ({{parameter}}) → int
                !
                {{call}}
                return x
              func Inc {{visibility}} (ref int:v) → void
                {{body.Replace("\n", "\n    ")}}
            """, "owned-ref.calr", new CompilationOptions { StrictEffects = true });
        Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code.StartsWith("Calor00", StringComparison.Ordinal)
            || diagnostic.Code.StartsWith("Calor01", StringComparison.Ordinal));
        if (hasForbiddenEffect)
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0410");
        else
            Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
    }

    [Fact]
    public void DirectPrivateByRefCall_DoesNotDowngradeTheCalleeRow()
    {
        const string source = """
            module P
              func Run public (int:x) → int
                !
                Inc(ref x)
                return x
              func Inc private (ref int:v) → void
                set v v + 1
            """;
        var diagnostics = new Compiler.Diagnostics.DiagnosticBag();
        var module = new Compiler.Parsing.Parser(
            new Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var effects = new Compiler.Effects.EffectEnforcementPass(diagnostics, strictEffects: true);
        effects.Enforce(module);
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics.Errors));
        var callee = Assert.Single(effects.DeclarationFacts.Values, fact => fact.Name == "Inc");
        Assert.Equal("mut", callee.InferredRow.ToCompactDisplayString());
        Assert.Equal("mut", callee.DeclaredRow.ToCompactDisplayString());
    }

    [Fact]
    public void DirectPrivateOutCall_InstantiatesOwnedReferenceStorage()
    {
        var result = Program.Compile("""
            module P
              func Run public () → string?
                !
                var string?:value = null
                Clear(out value)
                return value
              func Clear private (out string?:target) → void
                set target "done"
            """, "owned-out.calr", new CompilationOptions { StrictEffects = true });
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
    }

    // ---- Conversion: convert, compile, run, compare (the R0/D1 oracle on every surface). ----

    public enum Surface { Library, CliDefault, CliPassthrough, McpDefault, McpPassthroughOnError }

    public static TheoryData<string, Surface> ConversionRows()
    {
        var data = new TheoryData<string, Surface>();
        foreach (var (id, _, _) in Rows())
            foreach (var surface in Enum.GetValues<Surface>())
                data.Add(id, surface);
        return data;
    }

    [Theory]
    [MemberData(nameof(ConversionRows))]
    public async Task Conversion_KeepsByReferenceIdentity_OrPreservesTheShape(string id, Surface surface)
    {
        var (_, source, allowed) = Rows().Single(row => row.Id == id);
        var original = Run(source);
        var (calor, losses) = await ConvertForValueComparisonAsync(source, surface, original,
            includeLoweringCandidate: allowed.Split("-or-").Contains("native"));
        var interop = calor.Contains("§CSHARP{", StringComparison.Ordinal) || calor.Contains("§CS{", StringComparison.Ordinal)
            || calor.Contains("§RAW", StringComparison.Ordinal) || losses.Count > 0;
        var strict = Program.Compile(calor, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        var relaxed = strict.HasErrors && strict.Diagnostics.Errors.All(e => e.Code.StartsWith("Calor04", StringComparison.Ordinal))
            ? Program.Compile(calor, id + ".calr", new CompilationOptions { StatusWriter = TextWriter.Null, EnforceEffects = false })
            : strict;
        string outcome;
        if (relaxed.HasErrors)
            outcome = "rejected";
        else
        {
            Assert.Equal(original, Run(relaxed.GeneratedCode)); // never a silent copy or reorder
            outcome = interop ? "preserved" : "native";
        }
        Assert.True(allowed.Split("-or-").Contains(outcome),
            $"{id} on {surface}: {outcome}, allowed {allowed}: {string.Join("; ", relaxed.Diagnostics.Errors)}\n{calor}");
        // The F1-REFOUT-02 shape: an out declaration is never kept as inline §CS{} with assumed effects.
        Assert.DoesNotMatch(@"§CS\{[^}]*out var", calor);
    }

    [Fact]
    public void OutVar_IsLoweredBeforeItsStatement()
    {
        var calor = new CSharpToCalorConverter().Convert(R0Fixture("F1-REFOUT-02")).CalorSource!;
        Assert.Contains("§B{~n:int} `default`\n        §R (? §C{int.TryParse} §A text §A{out} n §/C n (- 1))", calor.Replace("\r\n", "\n"));
        Assert.Contains("§A{out} _ §/C", calor);
    }

    [Theory]
    [InlineData("while (int.TryParse(s, out var n)) { s = \"\"; }", false)]          // loop header
    [InlineData("System.Func<bool> f = () => int.TryParse(s, out var n);", false)]     // lambda
    [InlineData("if (int.TryParse(s, out var Field)) { }", false)]                     // collides with a field
    [InlineData("{ int m = 0; } if (int.TryParse(s, out var m)) { }", false)]          // declared elsewhere in scope
    [InlineData("int k = 1; if (int.TryParse(s, out var m) && m > k) { }", true)]      // control: inside the bound
    public void OutVar_OutsideTheBound_IsPreserved(string statement, bool native)
    {
        var result = new CSharpToCalorConverter().Convert($$"""
            public class C
            {
                private int Field;
                public void M(string s)
                {
                    {{statement}}
                }
            }
            """);
        Assert.Equal(native, !result.CalorSource!.Contains("§CSHARP{", StringComparison.Ordinal) && result.Losses.Count == 0);
    }

    [Fact]
    public void RefArgumentToAnInParameter_ConvertsToIn()
    {
        var calor = new CSharpToCalorConverter().Convert("""
            public static class Probe
            {
                static int Twice(in int x) => x * 2;
                public static string Run() { int v = 4; return Twice(ref v).ToString(); }
            }
            """).CalorSource!;
        Assert.Contains("§A{in} v", calor);
        Assert.DoesNotContain("§A{ref}", calor);
    }

    private static IEnumerable<(string Id, string Source, string Allowed)> Rows()
    {
        var root = CliTestHarness.FindRepoRoot();
        var r0 = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "tests/TestData/InteropScope/R0/cases.json")))!;
        foreach (var c in r0["cases"]!.AsArray().Where(c => c!["id"]!.GetValue<string>().StartsWith("F1-REFOUT-0", StringComparison.Ordinal)))
            yield return (c!["id"]!.GetValue<string>(), R0Fixture(c["id"]!.GetValue<string>()), c["expected"]!.GetValue<string>());
        // Codex review round 1 reproducers: a nested value-type field path, a by-reference call in
        // a decomposed chain with a by-value overload, a by-reference element before a hoisted
        // argument that throws, and an out declaration in a statement that is preserved.
        foreach (var (id, source) in ReviewWitnesses)
            yield return (id, source, "native-or-preserved");
        foreach (var (id, source, allowed) in Pr2Witnesses)
            yield return (id, source, allowed);
        foreach (var id in new[] { "X-INT-05", "X-INT-08" })
            yield return (id, File.ReadAllText(Path.Combine(root, "tests/TestData/InteropScope/F1/fixtures", id + ".cs.txt")), "native-or-preserved");
        foreach (var c in RefOutContractCaseTests.Contract()["cases"]!.AsArray().Where(c => c!["kind"]!.GetValue<string>() == "csharp"))
        {
            var src = c!["source"]!;
            // D1-CS-16 is native for its by-reference calls; its unrelated `[1] = "one"` index
            // initializer is preserved inline (dictionary-initializer), so the row allows preserved.
            var allowed = c["id"]!.GetValue<string>() == "D1-CS-16" ? "native-or-preserved" : c["conversion"]!.GetValue<string>();
            yield return (c["id"]!.GetValue<string>(), src is JsonArray a ? string.Join("\n", a.Select(l => l!.GetValue<string>())) : src.GetValue<string>(), allowed);
        }
    }

    private static readonly (string Id, string Source)[] ReviewWitnesses =
    [
        ("R1-nested-struct-path", "public struct Inner { public int Y; } public struct Outer { public Inner X; } public static class Probe { static void Bump(ref int x) { x = 7; } public static string Run() { var a = new Outer[] { new Outer() }; Bump(ref a[0].X.Y); return a[0].X.Y.ToString(); } }"),
        ("R1-chain-overload", "public class Box { public Box Next() => this; public int Use(ref int x) { x = 7; return 1; } public int Use(int x) => 2; } public static class Probe { public static string Run() { var b = new Box(); int x = 0; int k = b.Next().Use(ref x); return (x * 10 + k).ToString(); } }"),
        ("R1-element-before-hoist", "public class Bomb { public static string Log = \"\"; public Bomb() { Log += \"b\"; throw new System.InvalidOperationException(); } } public static class Probe { static void Use(ref int x, Bomb b) { } public static string Run() { int[] a = new int[0]; try { Use(ref a[0], new Bomb()); } catch (System.Exception e) { return e.GetType().Name + Bomb.Log; } return \"none\"; } }"),
        ("R1-out-var-preserved-statement", "public static class Probe { static string Seen = \"\"; static void Use(bool b, in int x) { Seen = b + \":\" + x; } public static string Run() { Use(int.TryParse(\"1\", out var n), 2); return Seen + \"|\" + n; } }"),
        // Codex review round 2: a by-reference call at the root of a chain, a field through a
        // ref-returning call, a receiver call after an earlier argument, and a block lambda.
        ("R2-chain-root", "public class Box { public int Use() => 1; } public class Probe { int Use() => 2; static Box Make(ref int x) { x = 7; return new Box(); } public string Go() { int x = 0; int z = Make(ref x).Use(); return x + \":\" + z; } public static string Run() => new Probe().Go(); }"),
        ("R2-ref-return-receiver", "public struct S { public int X; } public static class Probe { static S slot; static ref S Get() { return ref slot; } static void Use(ref int x) { x = 7; } public static string Run() { Use(ref Get().X); return slot.X.ToString(); } }"),
        ("R2-receiver-call-order", "public class Box { public int N; } public static class Probe { static Box b = new Box(); static string log = \"\"; static Box Get() { log += \"g\"; return b; } static int Before() { log += \"b\"; return 0; } static void Use(int i, ref int x) { x = 7; } public static string Run() { Use(Before(), ref Get().N); return log + \":\" + b.N; } }"),
        ("R2-block-lambda", "using System; public static class Probe { public static string Run() { Func<bool> f = () => { return int.TryParse(\"1\", out var n); }; return f().ToString(); } }"),
        // Codex review round 3: the receiver of a by-reference call is captured before a later
        // argument runs, and a write through a reference-typed field of an `in` struct is valid.
        ("R3-receiver-before-hoist", "public class Box { public int N; public Box(int n) { N = n; } public void Use(ref int x, Bomb b) { x = N; } } public class Bomb { public Bomb() { Probe.b = Probe.second; } } public static class Probe { public static Box b = new Box(1), second = new Box(2); public static string Run() { int x = 0; b.Use(ref x, new Bomb()); return x.ToString(); } }"),
        ("R3-reference-field-of-in-struct", "public class Box { public int N; } public struct S { public Box B; } public static class Probe { static void Bump(ref int n) { n = 7; } static void Use(in S s) { Bump(ref s.B.N); } public static string Run() { var s = new S { B = new Box() }; Use(in s); return s.B.N.ToString(); } }"),
    ];

    // F1 PR 2 (W1 findings). A bare §B{~n:i32} took the next sibling statement as its initializer
    // (a call statement, an §ARR declaration), so a lowered binding starts at `default`, as the
    // converter already writes `int x;`. That `default` would read a symbol named `default`, so
    // such code is preserved (Codex PR 2 round 1). A chained assignment evaluates its right side
    // once per target when converted, so it is preserved too (round 2). The parser is unchanged
    // (round 3: a next-line initializer is valid Calor). An expression-bodied ref return is
    // preserved like a block-bodied one.
    private static readonly (string Id, string Source, string Allowed)[] Pr2Witnesses =
    [
        ("P2-out-var-call-statement", "public static class Probe { public static string Run() { int.TryParse(\"5\", out var n); return n.ToString(); } }", "native"),
        ("P2-out-var-statement-then-call", "public static class Probe { static string Log = \"\"; static void Use(int x) { Log += x; } public static string Run() { int.TryParse(\"5\", out var n); Use(n); int.TryParse(\"x\", out int m); Use(m); return Log; } }", "native"),
        ("P2-out-var-string-statement", "using System.Collections.Generic; public static class Probe { public static string Run() { var d = new Dictionary<int, string>(); d.Add(1, \"one\"); d.TryGetValue(1, out var v); return v ?? \"null\"; } }", "native"),
        ("P2-out-var-array-declaration", "public static class Probe { static int Set(out int[] n) { n = new[] { 7 }; return 1; } public static string Run() { int[] a = new[] { Set(out var n) }; return n[0] + \":\" + a[0]; } }", "native"),
        ("P2-out-var-chained-assignment", "public static class Probe { static int calls; static bool Set(out int n) { n = ++calls; return true; } public static string Run() { bool x = false, y = false; x = y = Set(out var n); return calls + \":\" + n + \":\" + x; } }", "preserved"),
        ("P2-out-var-assignment-statement", "public static class Probe { public static string Run() { bool ok; ok = int.TryParse(\"6\", out var n); return ok + \":\" + n; } }", "native"),
        ("P2-out-var-local-declaration", "public static class Probe { public static string Run() { bool ok = int.TryParse(\"7\", out var n); return ok + \":\" + n; } }", "native"),
        ("P2-out-var-return", "public static class Probe { static int Get(string s) { return int.TryParse(s, out var n) ? n : -1; } public static string Run() => Get(\"8\") + \"|\" + Get(\"x\"); }", "native"),
        ("P2-out-var-if", "public static class Probe { public static string Run() { if (int.TryParse(\"9\", out var n)) { return n.ToString(); } return \"none\"; } }", "native"),
        ("P2-out-var-member-named-default", "public static class Probe { public static int reads; public static int @default { get { reads++; return 0; } } public static string Run() { bool ok = int.TryParse(\"5\", out var n); return reads + \":\" + n + \":\" + ok; } }", "preserved"),
        ("P2-ref-return-expression", "public static class Probe { static ref int First(int[] a) => ref a[0]; public static string Run() { var a = new[] { 1, 2 }; First(a) = 5; return a[0].ToString(); } }", "preserved"),
        ("P2-ref-return-property", "public static class Probe { static int slot = 3; static ref int Slot => ref slot; public static string Run() { Slot = 4; return slot.ToString(); } }", "preserved"),
    ];

    // The retired bare, next-line initializer now uses the canonical explicit assignment.
    [Fact]
    public void HandWrittenInitializer_UsesCurrentSyntaxAndKeepsItsMeaning()
    {
        var result = Program.Compile("""
            module P P
              module PModule
                func Run public () → int
                  !
                  let int:n = System.Math.Abs(-7)
                  return n
            """,
            "next.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Errors));
        Assert.Equal("7", Run(result.GeneratedCode + "\npublic static class Probe { public static string Run() => P.PModule.Run().ToString(); }"));
    }

    // The lowered binding carries an explicit default, so the next statement is never its initializer.
    [Fact]
    public void OutDeclaration_IsLoweredWithAnExplicitDefault()
    {
        var calor = new CSharpToCalorConverter().Convert("public static class Probe { static bool Set(out string value) { value = \"ok\"; return true; } public static string Run() { Set(out string value); return value; } }").CalorSource!;
        Assert.True(calor.Replace("\r\n", "\n").Contains(
            "var string:value = default(string)\n      Set(out @value)", StringComparison.Ordinal), calor);
    }

    [Theory]
    [InlineData("static void Use(ref int x) { x++; } static void M(int[] a) { Use(ref a[Next()]); }", "ref-parameter")]
    [InlineData("static void Use<T>(ref T x) { } static void M(int[] a) { Use<int>(ref a[Next()]); }", "ref-parameter")]
    [InlineData("static void Use(out int x) { x = 1; } static void M(int[] a) { Use(out a[Next()]); }", "out-parameter")]
    [InlineData("static void Use(ref readonly int x) { } static void M(int[] a) { Use(in a[Next()]); }", "ref-parameter")]
    [InlineData("static void Use<T>(in T x) { } static void M() { int x = 0; Use<int>(in x); }", "in-parameter")]
    [InlineData("static void Use(in int x) { } static void M(int[] a) { Use(in a[Next()]); }", "in-parameter")] // control
    [InlineData("static void Use(in int x) { } static void M(int v) { Use(v); }", "in-parameter")]                  // control
    public void ByReferenceStatementLoss_IsReportedUnderTheParametersFeature(string members, string feature)
    {
        var result = new CSharpToCalorConverter().Convert("public static class C { static int Next() => 0; " + members + " }");
        var features = result.Losses.Select(loss => loss.Feature).Concat(result.Issues.Where(i => i.Feature?.EndsWith("-parameter") == true && i.Message.Contains("Use", StringComparison.Ordinal))
            .Select(i => i.Feature!)).Distinct().ToArray();
        Assert.Contains(feature, features); // a `ref readonly` parameter also preserves its declaring member (`method`)
        Assert.All(features, f => Assert.True(f == feature || f == "method", f));
    }

    [Fact]
    public void ExpressionBodiedRefReturn_IsPreserved_NotStripped()
    {
        var result = new CSharpToCalorConverter().Convert("public static class C { public static ref int First(int[] a) => ref a[0]; }");
        RawFrameAssertions.AssertFramed(result.CalorSource);
        Assert.DoesNotContain("-> ref", result.CalorSource);
        var block = new CSharpToCalorConverter().Convert("public static class C { public static ref int First(int[] a) { return ref a[0]; } }");
        Assert.Equal(block.Losses.Select(loss => loss.Feature), result.Losses.Select(loss => loss.Feature)); // the same report
        Assert.Contains(result.Issues, issue => issue.Feature == "ref-parameter");
    }

    // FeatureSupport states what converts (W1 finding 4); no level is promoted (D-S1.5).
    [Theory]
    [InlineData("ref-expression", "stripped", SupportLevel.Full)]
    [InlineData("multidim-array", "fully supported", SupportLevel.Full)]
    [InlineData("out-parameter", "[NotNullWhen] are preserved", SupportLevel.Partial)]
    [InlineData("in-parameter", "pass a value to an in parameter", SupportLevel.Full)]
    [InlineData("out-var", "Chained assignments", SupportLevel.Partial)]
    public void FeatureSupportText_MatchesTheConverter(string feature, string phrase, SupportLevel level)
    {
        var info = FeatureSupport.GetFeatureInfo(feature)!;
        Assert.Equal(level, info.Support);
        Assert.Equal(feature == "out-var", info.Description!.Contains(phrase, StringComparison.Ordinal));
        Assert.Contains("C#", info.Description);
    }

    private static string R0Fixture(string id) => File.ReadAllText(Path.Combine(
        CliTestHarness.FindRepoRoot(), "tests/TestData/InteropScope/R0/fixtures", id + ".cs.txt"));

    public static TheoryData<Surface> DefaultSurfaceRows()
    {
        var data = new TheoryData<Surface>();
        foreach (var surface in Enum.GetValues<Surface>())
            data.Add(surface);
        return data;
    }

    [Theory]
    [MemberData(nameof(DefaultSurfaceRows))]
    public async Task DefaultSurfaces_KeepProvedPureCodeNative(Surface surface)
    {
        const string source = "public static class Probe { public static string Run() => \"safe\"; }";
        var (calor, losses) = await ConvertAsync(source, surface);
        Assert.Empty(losses);
        RawFrameAssertions.AssertFramed(calor, Compiler.Parsing.TokenKind.CSharpInterop, present: false);
        RawFrameAssertions.AssertFramed(calor, Compiler.Parsing.TokenKind.RawCSharp, present: false);
        var compiled = Program.Compile(calor, "default-pure.calr",
            new CompilationOptions { StrictEffects = true, StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.Equal("safe", Run(compiled.GeneratedCode));
    }

    [Theory]
    [MemberData(nameof(DefaultSurfaceRows))]
    public async Task DefaultSurfaces_DoNotCertifyOpenObjectDispatch(Surface surface)
    {
        const string source = "public static class Probe { public static bool Compare(object a, object b) => object.Equals(a, b); }";
        var (calor, losses) = await ConvertAsync(source, surface);
        Assert.NotEmpty(losses);
        RawFrameAssertions.AssertFramed(calor, Compiler.Parsing.TokenKind.CSharpInterop);
        var candidate = ConvertValueCandidate(source);
        Assert.True(candidate.Success, string.Join("; ", candidate.Issues.Select(issue => issue.Message)));
        Assert.Empty(candidate.Losses);
        var strict = Program.Compile(candidate.CalorSource!, "uncertified-object.calr",
            new CompilationOptions { StrictEffects = true, StatusWriter = TextWriter.Null });
        Assert.True(strict.HasErrors);
        Assert.Contains(strict.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor0411");
    }

    [Fact]
    public void ValueCandidate_RetainsObliviousDeclarationsAndAssignedLocalTrace()
    {
        const string source = """
            public static class Probe
            {
                public static string Run()
                {
                    bool flag = true;
                    string result;
                    if (flag) result = "safe";
                    else result = "other";
                    return result;
                }
            }
            """;
        var candidate = ConvertValueCandidate(source);
        Assert.True(candidate.Success, string.Join("; ", candidate.Issues.Select(issue => issue.Message)));
        Assert.Empty(candidate.Losses);
        Assert.Contains("nullable oblivious", candidate.CalorSource);
        var compiled = Program.Compile(candidate.CalorSource!, "oblivious-candidate.calr",
            new CompilationOptions { StrictEffects = true, StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors));
        Assert.Equal("safe", Run(compiled.GeneratedCode));

        var originalTree = CSharpSyntaxTree.ParseText(source);
        var original = CSharpCompilation.Create("ObliviousCandidateMetadata",
            [originalTree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Disable));
        var generated = original.ReplaceSyntaxTree(originalTree, CSharpSyntaxTree.ParseText(compiled.GeneratedCode));
        var before = original.GetTypeByMetadataName("Probe")!.GetMembers("Run").OfType<IMethodSymbol>().Single();
        var after = generated.GetTypeByMetadataName("Probe")!.GetMembers("Run").OfType<IMethodSymbol>().Single();
        Assert.Equal(NullableAnnotation.None, before.ReturnNullableAnnotation);
        Assert.Equal(before.ReturnNullableAnnotation, after.ReturnNullableAnnotation);
        Assert.Equal(before.ReturnType.ToDisplayString(), after.ReturnType.ToDisplayString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObliviousValueCandidate_PreservesPropertyReceiverEvaluationAndNullFailure(bool nullReceiver)
    {
        var source = $$"""
            public static class Probe
            {
                static int calls;
                static string Next() { calls++; return {{(nullReceiver ? "null" : "\"abc\"")}}; }
                public static string Run()
                {
                    try
                    {
                        int length = Next().Length;
                        return calls == 1 && length == 3 ? "safe" : "wrong";
                    }
                    catch (System.NullReferenceException) { return calls == 1 ? "null" : "wrong"; }
                }
            }
            """;
        var candidate = ConvertValueCandidate(source);
        Assert.True(candidate.Success, string.Join("; ", candidate.Issues.Select(issue => issue.Message)));
        Assert.Empty(candidate.Losses);
        var compiled = Program.Compile(candidate.CalorSource!, "oblivious-property-candidate.calr",
            new CompilationOptions { StrictEffects = true, StatusWriter = TextWriter.Null });
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Errors) + "\n" + candidate.CalorSource);
        var expected = nullReceiver ? "null" : "safe";
        Assert.Equal(expected, Run(source));
        Assert.Equal(expected, Run(compiled.GeneratedCode));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValueCandidate_DoesNotDisableExplicitNullableChecking(bool projectOption)
    {
        var source = (projectOption ? "" : "#nullable enable\n") + """
            public static class Probe
            {
                public static string Run(string? value) => value;
            }
            """;
        var candidate = ConvertValueCandidate(source,
            projectOption ? NullableContextOptions.Enable : null);
        Assert.True(candidate.Success, string.Join("; ", candidate.Issues.Select(issue => issue.Message)));
        Assert.Empty(candidate.Losses);
        Assert.DoesNotContain("nullable oblivious", candidate.CalorSource);
        var compiled = Program.Compile(candidate.CalorSource!, "annotated-candidate.calr",
            new CompilationOptions { StrictEffects = true, StatusWriter = TextWriter.Null });
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == "Calor0273" && diagnostic.Message.Contains("Annotated", StringComparison.Ordinal));
        Assert.DoesNotContain(compiled.Diagnostics.Errors, diagnostic => diagnostic.Code is "Calor0410" or "Calor0411");
    }

    // Candidate "native" means syntax-shaped lowering only. Actual default surfaces retain
    // their lossless contract; neither preservation nor this opt-in is an effect certificate.
    internal static async Task<(string Calor, List<string> Losses)> ConvertForValueComparisonAsync(
        string source, Surface surface, string original, bool includeLoweringCandidate = true)
    {
        var (delivered, deliveredLosses) = await ConvertAsync(source, surface);
        var strict = Program.Compile(delivered, "default-value-comparison.calr",
            new CompilationOptions { StrictEffects = deliveredLosses.Count == 0, StatusWriter = TextWriter.Null });
        Assert.False(strict.HasErrors, string.Join("; ", strict.Diagnostics.Errors));
        var executed = Task.Run(() => Run(strict.GeneratedCode));
        Assert.True(await Task.WhenAny(executed, Task.Delay(TimeSpan.FromSeconds(30))) == executed,
            $"Default {surface} output did not finish\n{delivered}");
        Assert.Equal(original, await executed);

        // Preservation-only contract rows test delivery, not an optional native-shaped candidate.
        if (deliveredLosses.Count == 0 || !includeLoweringCandidate)
            return (delivered, deliveredLosses);

        var candidate = ConvertValueCandidate(source);
        Assert.True(candidate.Success, string.Join("; ", candidate.Issues.Select(issue => issue.Message)));
        Assert.DoesNotContain(candidate.Losses, loss => loss.IsSemanticLoss);
        Assert.NotNull(candidate.CalorSource);
        return (candidate.CalorSource, candidate.Losses.Select(loss => loss.Feature).ToList());
    }

    private static ConversionResult ConvertValueCandidate(string source,
        NullableContextOptions? nullableContextOptions = null) =>
        new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            Mode = ConversionMode.Interop,
            NullableContextOptions = nullableContextOptions,
            UserManifestRoot = string.Empty
        }).Convert(source);

    internal static async Task<(string Calor, List<string> Losses)> ConvertAsync(string source, Surface surface)
    {
        if (surface is Surface.McpDefault or Surface.McpPassthroughOnError)
        {
            var args = new Dictionary<string, object> { ["source"] = source };
            if (surface == Surface.McpPassthroughOnError)
                args["passthroughOnError"] = true;
            var tool = await new ConvertTool().ExecuteAsync(JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement);
            using var payload = JsonDocument.Parse(tool.Content[0].Text!);
            var root = payload.RootElement;
            var losses = root.TryGetProperty("lossSummary", out var summary)
                ? summary.GetProperty("locations").EnumerateArray().Select(l => l.GetProperty("feature").GetString()!).ToList()
                : [];
            return (root.GetProperty("calorSource").GetString() ?? "", losses);
        }
        var options = surface == Surface.Library ? new ConversionOptions()
            : ConvertCommand.BuildCSharpToCalorOptions(benchmark: false, verbose: false, explain: false, noFallback: false,
                passthrough: surface == Surface.CliPassthrough, explicitCallClosers: false);
        var result = new CSharpToCalorConverter(options).Convert(source);
        return (result.CalorSource ?? "", result.Losses.Select(loss => loss.Feature).ToList());
    }

    internal static string Run(string csharp, string probeName = "Probe")
    {
        var compilation = CSharpCompilation.Create("F1_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(csharp, new CSharpParseOptions(LanguageVersion.Preview))],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) + "\n" + csharp);
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var run = context.LoadFromStream(stream).GetTypes().Single(t => t.Name == probeName).GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            try { return run.Invoke(null, null)?.ToString() ?? "<null>"; }
            catch (TargetInvocationException e) { return "!" + e.InnerException!.GetType().Name; }
        }
        finally
        {
            context.Unload();
        }
    }
}
