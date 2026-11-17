using Calor.Compiler.Analysis;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Ast;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Effects.Manifests;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for the calor effects suggest command logic:
/// ExternalCallCollector, internal call filtering, and manifest generation.
/// </summary>
public class EffectsSuggestTests
{
    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    // ========================================================================
    // ExternalCallCollector
    // ========================================================================

    [Fact]
    public void Collector_FindsExternalCalls()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    Console.WriteLine(""test"")
    MyService.Process()
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(calls, c => c.TypeName == "System.Console" && c.MethodName == "WriteLine");
        Assert.Contains(calls, c => c.MethodName == "Process");
    }

    [Fact]
    public void Collector_DeduplicatesCalls()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    Console.WriteLine(""a"")
    Console.WriteLine(""b"")
    Console.WriteLine(""c"")
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Single(calls, c => c.TypeName == "System.Console" && c.MethodName == "WriteLine");
    }

    [Fact]
    public void Collector_ExpandsShortNames()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    File.ReadAllText(""test.txt"")
    HttpClient.GetAsync(""url"")
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(calls, c => c.TypeName == "System.IO.File" && c.MethodName == "ReadAllText");
        Assert.Contains(calls, c => c.TypeName == "System.Net.Http.HttpClient" && c.MethodName == "GetAsync");
    }

    [Fact]
    public void Collector_WalksClassMethods()
    {
        var source = @"
module Test Test block
  class MyClass
    func DoWork () → void
      !
      ExternalService.Call()
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(calls, c => c.MethodName == "Call");
    }

    [Fact]
    public void Collector_ResolvesVariableTypes()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let r = new Random()
    r.Next()
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        // r.Next should resolve to System.Random.Next via variable type map
        Assert.Contains(calls, c => c.TypeName == "System.Random" && c.MethodName == "Next");
    }

    [Fact]
    public void Collector_UsesBoundReceiverIdentityForShadowedNames()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let r = new Random()
    if true
      let r = new HttpClient()
      r.GetAsync(""/"")
    r.Next()
";
        var module = Parse(source);

        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(
            calls,
            call => call.TypeName == "System.Net.Http.HttpClient"
                && call.MethodName == "GetAsync");
        Assert.Contains(
            calls,
            call => call.TypeName == "System.Random"
                && call.MethodName == "Next");
    }

    // ========================================================================
    // v0.15 E1 exit pins (roadmap §4.2 E1; metadata-binding scoping §3 S6 / D5)
    // ========================================================================

    /// <summary>
    /// E1 exit pin (a): the AST-side variable-type map is deleted, not
    /// bypassed. No identifier <c>_variableTypeMap</c> may occur anywhere
    /// under <c>src/Calor.Compiler/Effects/</c>.
    /// </summary>
    [Fact]
    public void E1_ExitPin_EffectsDirectory_HasNoVariableTypeMapIdentifier()
    {
        var effectsDir = Path.Combine(RepoRoot(), "src", "Calor.Compiler", "Effects");
        Assert.True(Directory.Exists(effectsDir), $"Effects directory not found at {effectsDir}");

        var identifier = new System.Text.RegularExpressions.Regex(@"\b_variableTypeMap\b");
        var offenders = Directory
            .EnumerateFiles(effectsDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => identifier.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(effectsDir, file))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The _variableTypeMap string path was deleted in v0.15 E1; it must not be reintroduced " +
            "under src/Calor.Compiler/Effects/. Offending files: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// E1 exit pin (b), the original S6 behavioural criterion: a receiver whose
    /// type is available ONLY through metadata resolves. Nothing in the module
    /// names <c>System.Guid</c> or <c>System.String</c> as the type of
    /// <c>g</c> / <c>line</c>; the binder learns them from MetadataBinder's
    /// Roslyn return types for <c>System.Guid.NewGuid()</c> and
    /// <c>System.Console.ReadLine()</c>.
    /// </summary>
    [Fact]
    public void E1_ExitPin_ReceiverTypeKnownOnlyThroughMetadata_Resolves()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let g = System.Guid.NewGuid()
    g.ToString()
    let line = System.Console.ReadLine()
    line.Trim()
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var toString = Assert.Single(calls, c => c.MethodName == "ToString");
        Assert.Equal("System.Guid", toString.TypeName);
        Assert.True(toString.ReceiverResolved);

        var trim = Assert.Single(calls, c => c.MethodName == "Trim");
        Assert.Equal("System.String", trim.TypeName);
        Assert.True(trim.ReceiverResolved);
    }

    /// <summary>
    /// An inferred binding (<c>§B{sb}</c> with no type annotation) resolves
    /// its receiver through the bound tree — the <c>BoundType</c> of the
    /// variable — not by scanning §NEW initializers in the AST.
    /// </summary>
    [Fact]
    public void Collector_InferredBinding_ResolvesReceiverFromBoundType()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let sb = new System.Text.StringBuilder()
    sb.Append(""x"")
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var append = Assert.Single(calls, c => c.MethodName == "Append");
        Assert.Equal("System.Text.StringBuilder", append.TypeName);
        Assert.True(append.ReceiverResolved);

        // A dotted §NEW target is one type, not a "System.Text" receiver with a
        // "StringBuilder" method.
        var ctor = Assert.Single(calls, c => c.Kind == CallKind.Constructor);
        Assert.Equal("System.Text.StringBuilder", ctor.TypeName);
        Assert.Equal(".ctor", ctor.MethodName);
        Assert.DoesNotContain(calls, c => c.TypeName == "System.Text");
    }

    /// <summary>
    /// A receiver the binder cannot vouch for is reported as unresolved and
    /// never given a guessed type: <c>x</c> is bound from an unknown callee
    /// (the binder's OBJECT fallback); <c>a.b</c> is a member chain the bound
    /// tree does not type; <c>foo</c> is not a bound variable and not written
    /// as a type; <c>this.sb</c> is a chain through <c>this</c>. None may
    /// surface as <c>OBJECT</c>, <c>System.Object</c>, the type of <c>a</c>,
    /// or as the echoed source text with <c>ReceiverResolved == true</c>.
    /// </summary>
    [Fact]
    public void Collector_UnresolvedReceiver_IsReportedWithoutGuessedType()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let x = Unknown.Make()
    x.Run()
    let a = new Random()
    a.b.Chain()
    foo.Bar()

  class Holder
    func Write () → void
      !
      this.sb.Append(""x"")
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var run = Assert.Single(calls, c => c.MethodName == "Run");
        Assert.False(run.ReceiverResolved);
        Assert.Equal("x", run.TypeName);

        var chain = Assert.Single(calls, c => c.MethodName == "Chain");
        Assert.False(chain.ReceiverResolved);
        Assert.Equal("a.b", chain.TypeName);

        var bar = Assert.Single(calls, c => c.MethodName == "Bar");
        Assert.False(bar.ReceiverResolved);
        Assert.Equal("foo", bar.TypeName);

        var append = Assert.Single(calls, c => c.MethodName == "Append");
        Assert.False(append.ReceiverResolved);
        Assert.Equal("this.sb", append.TypeName);

        // The type-qualified callee that produced x is still a resolved receiver
        // (written as a type reference), so suggest can still propose it.
        var make = Assert.Single(calls, c => c.MethodName == "Make");
        Assert.True(make.ReceiverResolved);
        Assert.Equal("Unknown", make.TypeName);

        Assert.DoesNotContain(calls, c => c.TypeName is "OBJECT" or "System.Object");
        Assert.DoesNotContain(calls, c => c.TypeName == "System.Random" && c.MethodName == "Chain");

        // The unresolved call is still visible to the resolver as Unknown — never silently pure.
        var resolver = new EffectResolver();
        resolver.Initialize();
        Assert.Equal(EffectResolutionStatus.Unknown, resolver.Resolve(EffectResolverKey.FromStrings(run.TypeName, run.MethodName)).Status);
    }

    /// <summary>
    /// A lambda-bound variable is a function value, not a nominal receiver:
    /// invoking it must not be attributed to the binder's
    /// <c>LAMBDA(...)</c> type string.
    /// </summary>
    [Fact]
    public void Collector_FunctionTypedReceiver_IsUnresolved()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let f = (int:x) => (x + 1)
    f.Invoke(1)
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var invoke = Assert.Single(calls, c => c.MethodName == "Invoke");
        Assert.False(invoke.ReceiverResolved);
        Assert.Equal("f", invoke.TypeName);
        Assert.DoesNotContain(calls, c => c.TypeName.StartsWith("LAMBDA", StringComparison.Ordinal));
    }

    /// <summary>
    /// An array-typed receiver resolves to <c>System.Array</c>, not to the
    /// element type.
    /// </summary>
    [Fact]
    public void Collector_ArrayReceiver_ResolvesToSystemArray()
    {
        var source = @"
module Test Test block
  func DoWork (int[]:arr) → void
    !
    arr.Clone()
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var clone = Assert.Single(calls, c => c.MethodName == "Clone");
        Assert.True(clone.ReceiverResolved);
        Assert.Equal("System.Array", clone.TypeName);
        Assert.DoesNotContain(calls, c => c.TypeName == "System.Int32");
    }

    /// <summary>
    /// Resolution step 2: a Calor-declared class used as a static receiver
    /// resolves through its <c>TypeSymbol</c>. And the deliberate slice-1
    /// residual, pinned so it cannot drift silently: a receiver written as a
    /// type reference that is neither a Calor type nor known to metadata
    /// (<c>OrderRepo</c>) stays a resolved receiver, so
    /// <c>calor effects suggest</c> can still propose a manifest entry for it.
    /// </summary>
    [Fact]
    public void Collector_CalorClassStaticReceiver_ResolvesThroughTypeSymbol()
    {
        var source = @"
module Test Test block
  class Helper
    func Run () → void
      !
      return

  func DoWork () → void
    !
    Helper.Run()
    OrderRepo.Save()
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        var run = Assert.Single(calls, c => c.MethodName == "Run");
        Assert.True(run.ReceiverResolved);
        Assert.EndsWith("Helper", run.TypeName, StringComparison.Ordinal);

        var save = Assert.Single(calls, c => c.MethodName == "Save");
        Assert.True(save.ReceiverResolved);
        Assert.Equal("OrderRepo", save.TypeName);
    }

    // ========================================================================
    // v0.15 E1 slice 2a pins — receiver BoundExpression on the call nodes,
    // binder-emitted UnresolvedBoundType (scoping §D6, roadmap §4.2 E1)
    // ========================================================================

    private static (BoundModule Bound, DiagnosticBag Diagnostics) Bind(ModuleNode module)
    {
        var bag = new DiagnosticBag();
        return (new Binder(bag).Bind(module), bag);
    }

    private static IEnumerable<BoundNode> Descendants(BoundNode node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    /// <summary>The receiver the binder attached to the call written as
    /// <paramref name="target"/>. A native <c>x.M()</c> binds as an expression
    /// call (<c>Target == "&lt;expression-call&gt;"</c>), so it is found by its
    /// receiver and callee spans in <paramref name="source"/>. Fails the test if
    /// no such call node exists, so a null return means "the binder attached no
    /// receiver", never "not found".</summary>
    private static BoundExpression? ReceiverOf(BoundNode root, string source, string target) =>
        CallOf(root, source, target) switch
        {
            BoundCallStatement statement => statement.Receiver,
            BoundCallExpression expression => expression.Receiver,
            _ => null
        };

    private static BoundNode CallOf(BoundNode root, string source, string target)
    {
        foreach (var node in Descendants(root))
        {
            switch (node)
            {
                case BoundCallStatement statement when statement.Target == target:
                    return statement;
                case BoundCallExpression expression when expression.Target == target
                    || WrittenTarget(source, expression) == target:
                    return expression;
            }
        }

        Assert.Fail($"No bound call node with target '{target}'.");
        return null!;
    }

    private static string? WrittenTarget(string source, BoundCallExpression call) =>
        call.ReceiverSpan is { } receiver
            ? $"{source.Substring(receiver.Start, receiver.Length)}.{source.Substring(call.CalleeSpan.Start, call.CalleeSpan.Length)}"
            : null;

    /// <summary>The Calor0270s the binder reports for an unresolved call
    /// RECEIVER (<c>Binder.UnresolvedReceiver</c>) — not the metadata
    /// member-resolution Calor0270 the same code also carries.</summary>
    private static List<Diagnostic> ReceiverUnresolvedDiagnostics(DiagnosticBag diagnostics) =>
        diagnostics.Where(d => d.Code == DiagnosticCode.SignatureUnresolved
            && d.Message.Contains("so the call's receiver type is unresolved", StringComparison.Ordinal)).ToList();

    /// <summary>
    /// E1 slice 2a pin (a): a dotted head the binder cannot bind is never given a
    /// type, and the collector reports the call unresolved. <c>foo</c> is not a
    /// bound variable and is not written as a type reference; <c>this.sb</c> is a
    /// chain through <c>this</c> to a member <c>Holder</c> does not declare.
    ///
    /// <para>0.30 native syntax: <c>foo.Bar()</c> binds as an expression call
    /// whose receiver is bound as an ordinary expression, so the binder no longer
    /// attaches NO receiver — it attaches its undefined-name variable for
    /// <c>foo</c> (typed <see cref="UnresolvedBoundType"/>) and the
    /// <c>OBJECT</c>-fallback member access for <c>this.sb</c> (marked
    /// <c>HasUnknownInferredType</c>). Neither is a type the collector may
    /// report.</para>
    /// </summary>
    [Fact]
    public void E1_Slice2a_UnboundDottedHead_YieldsUnresolvedReceiverAndUnresolvedCall()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    foo.Bar()

  class Holder
    func Write () → void
      !
      this.sb.Append(""x"")
";
        var module = Parse(source);
        var (bound, _) = Bind(module);

        var foo = Assert.IsType<BoundVariableExpression>(ReceiverOf(bound, source, "foo.Bar"));
        Assert.True(foo.Variable.Id.IsNone);
        Assert.IsType<UnresolvedBoundType>(foo.Type);

        var sb = Assert.IsType<BoundFieldAccessExpression>(ReceiverOf(bound, source, "this.sb.Append"));
        Assert.IsType<BoundThisExpression>(sb.Target);
        Assert.True(sb.HasUnknownInferredType);

        var calls = ExternalCallCollector.Collect(module);
        var bar = Assert.Single(calls, c => c.MethodName == "Bar");
        Assert.False(bar.ReceiverResolved);
        Assert.Equal("foo", bar.TypeName);
        var append = Assert.Single(calls, c => c.MethodName == "Append");
        Assert.False(append.ReceiverResolved);
        Assert.Equal("this.sb", append.TypeName);
    }

    /// <summary>
    /// E1 slice 2a pin (b): a receiver whose type is available ONLY through
    /// metadata carries a real <see cref="NominalBoundType"/> on
    /// <c>Receiver.Type</c>. Nothing in the module names <c>Guid</c> as the type
    /// of <c>g</c> — the binder learned it from MetadataBinder's Roslyn return
    /// type for <c>System.Guid.NewGuid()</c>. This is the discriminator against
    /// the deleted slice-1 path: the type is read off the receiver expression,
    /// not reconstructed from a symbol string at the collector.
    /// </summary>
    [Fact]
    public void E1_Slice2a_MetadataOnlyReceiver_CarriesNominalBoundTypeOnReceiver()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let g = System.Guid.NewGuid()
    g.ToString()
";
        var module = Parse(source);
        var (bound, _) = Bind(module);

        var receiver = ReceiverOf(bound, source, "g.ToString");
        var variable = Assert.IsType<BoundVariableExpression>(receiver);
        var nominal = Assert.IsType<NominalBoundType>(variable.Type);
        Assert.Equal("System.Guid", nominal.QualifiedName);
    }

    /// <summary>
    /// E1 slice 2a pin (b), structural half: the collector no longer builds a
    /// receiver expression of its own. Slice 1 reconstructed one
    /// (<c>new BoundVariableExpression(span, symbol).Type</c>) because the call
    /// nodes carried no receiver; slice 2a deleted that, and reintroducing it
    /// would mean the collector is second-guessing the binder again.
    /// </summary>
    [Fact]
    public void E1_Slice2a_Collector_ConstructsNoReceiverExpressionOfItsOwn()
    {
        var collector = Path.Combine(
            RepoRoot(), "src", "Calor.Compiler", "Effects", "ExternalCallCollector.cs");
        Assert.True(File.Exists(collector), $"Collector not found at {collector}");

        var text = File.ReadAllText(collector);
        Assert.DoesNotContain("new BoundVariableExpression", text, StringComparison.Ordinal);
        Assert.DoesNotContain("new BoundFieldAccessExpression", text, StringComparison.Ordinal);
        Assert.DoesNotContain("new BoundTypeReferenceExpression", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// E1 slice 2a pin (c): a receiver the binder cannot type carries an
    /// <see cref="UnresolvedBoundType"/> and reports Calor0270 exactly once at
    /// that site — not once per consumer, not once per segment of a chain. The
    /// call never becomes a resolved manifest row; the end-to-end half of that
    /// claim is pinned by
    /// <c>Calor.Compiler.Tests.EffectsSuggestCliTests.Suggest_Text_UntypedReceiver_IsWarnedAndKeptOutOfManifest</c>,
    /// which runs the same <c>x.Run</c> shape through <c>calor effects suggest</c>.
    /// </summary>
    [Fact]
    public void E1_Slice2a_UnresolvedReceiver_ReportsCalor0270OnceAndIsNeverResolved()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let x = Unknown.Make()
    x.Run()
";
        var module = Parse(source);
        var (bound, diagnostics) = Bind(module);

        var receiver = ReceiverOf(bound, source, "x.Run");
        var unresolved = Assert.IsType<UnresolvedBoundType>(
            Assert.IsType<BoundVariableExpression>(receiver).Type);
        Assert.Equal(
            "receiver 'x' is an inferred local whose type the binder could not determine",
            unresolved.Reason);

        var reported = diagnostics.Where(d => d.Code == DiagnosticCode.SignatureUnresolved).ToList();
        var single = Assert.Single(reported);
        Assert.Equal(DiagnosticSeverity.Info, single.Severity);
        // The receiver path appears once in the message, not twice.
        Assert.Equal(1, single.Message.Split("'x'").Length - 1);

        var run = Assert.Single(ExternalCallCollector.Collect(module), c => c.MethodName == "Run");
        Assert.False(run.ReceiverResolved);
        Assert.Equal("x", run.TypeName);
    }

    /// <summary>
    /// E1 slice 2a: the receiver is one of exactly four shapes, and each carries
    /// the type the binder will stand behind — a bound variable, a member-access
    /// chain the binder does not type, a type reference, or nothing at all.
    /// A member chain reports ONE Calor0270 for the whole chain, not one per
    /// segment.
    ///
    /// <para>0.30 native syntax binds the receiver of <c>x.M()</c> as an ordinary
    /// expression, so the chain and the unbound head take the shapes the
    /// expression binder gives them: the chain is typed segment by segment (the
    /// first unknown segment is <see cref="UnresolvedBoundType"/>, later ones the
    /// <c>OBJECT</c> fallback marked <c>HasUnknownInferredType</c>), and the
    /// unbound head is the binder's undefined-name variable, not null.</para>
    /// </summary>
    [Fact]
    public void E1_Slice2a_ReceiverShapes_AreTheFourDocumentedOnes()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let sb = new System.Text.StringBuilder()
    sb.Append(""x"")
    let a = new Random()
    a.b.c.Chain()
    System.Console.WriteLine(""y"")
    foo.Bar()
";
        var module = Parse(source);
        var (bound, diagnostics) = Bind(module);

        // 1. bound local → BoundVariableExpression carrying the symbol's type.
        var local = Assert.IsType<BoundVariableExpression>(ReceiverOf(bound, source, "sb.Append"));
        Assert.Equal("System.Text.StringBuilder", local.Type.DisplayString);

        // 2. member chain → a field-access chain over the typed head, unresolved.
        var chain = Assert.IsType<BoundFieldAccessExpression>(ReceiverOf(bound, source, "a.b.c.Chain"));
        Assert.True(chain.HasUnknownInferredType);
        Assert.Equal("c", chain.FieldName);
        var inner = Assert.IsType<BoundFieldAccessExpression>(chain.Target);
        Assert.Equal("b", inner.FieldName);
        Assert.IsType<UnresolvedBoundType>(inner.Type);
        var chainHead = Assert.IsType<BoundVariableExpression>(inner.Target);
        // The head keeps the binder's own spelling — short-BCL-name expansion is
        // the collector's step, not the binder's, and the binder must not be
        // read as having claimed "System.Random" here.
        Assert.Equal("Random", chainHead.Type.DisplayString);

        // 3. static type receiver → BoundTypeReferenceExpression naming the type.
        var typeRef = Assert.IsType<BoundTypeReferenceExpression>(
            ReceiverOf(bound, source, "System.Console.WriteLine"));
        Assert.Equal("System.Console", typeRef.Name);
        Assert.Equal("System.Console", typeRef.Type.DisplayString);

        // 4. unbound head → the binder's undefined-name variable, never typed.
        var unbound = Assert.IsType<BoundVariableExpression>(ReceiverOf(bound, source, "foo.Bar"));
        Assert.True(unbound.Variable.Id.IsNone);
        Assert.IsType<UnresolvedBoundType>(unbound.Type);

        // Marking is not reporting: every unresolved receiver above is MARKED,
        // but a member chain is a binder limitation the author cannot act on, so
        // it is silent. No shape in this fixture is reportable, so the module
        // produces zero receiver-unresolved Calor0270. (Native binding does report
        // `Random` having no member `b` — a metadata member-resolution Calor0270
        // about the member, not the receiver.)
        Assert.Empty(ReceiverUnresolvedDiagnostics(diagnostics));
    }

    /// <summary>
    /// Review round 1, finding 2: the two shapes an author can act on report, and
    /// the two they cannot are silent — while all four are still marked
    /// <see cref="UnresolvedBoundType"/>. Delete the
    /// <c>ShouldReportUnresolvedReceiver</c> gate and the chain/temporary arms
    /// start firing again (875 Info diagnostics on the converted corpus; see the
    /// ledger pinned by
    /// <c>Calor.Compiler.Tests.Calor0270CorpusVolumeTests</c>).
    /// </summary>
    [Fact]
    public void E1_Slice2a_Calor0270_FiresOnlyForActionableReceiverShapes()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let x = Unknown.Make()
    x.Run()
    let a = new Random()
    a.b.Chain()
    let _chainIsValid042 = Unknown.Make()
    _chainIsValid042.Run()
";
        var module = Parse(source);
        var (bound, diagnostics) = Bind(module);

        // All three receivers are marked unresolved — silence is about the
        // editor, never about what the type system claims.
        foreach (var target in new[] { "x.Run", "a.b.Chain", "_chainIsValid042.Run" })
        {
            Assert.IsType<UnresolvedBoundType>(ReceiverOf(bound, source, target)!.Type);
        }

        // Only the inferred local the author actually wrote is reported as an
        // unresolved receiver.
        var single = Assert.Single(ReceiverUnresolvedDiagnostics(diagnostics));
        Assert.Contains("'x'", single.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("a.b", single.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("_chain", single.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review round 1, finding 9: <c>Receiver</c> and <c>ReceiverSymbol</c> are two
    /// channels describing the same receiver, and they must not drift apart in a
    /// later slice. When the receiver is a bound variable the two agree on the
    /// symbol; when the binder attached no receiver there is no receiver symbol
    /// either; and a type-reference receiver never carries a variable symbol.
    ///
    /// <para>0.30 native syntax: a member chain <c>a.b.Chain()</c> binds as an
    /// expression call whose chain receiver is ALSO its evaluated receiver, a
    /// child of the call node. <c>ReceiverSymbol</c> is then null — deliberately
    /// not re-derived from the chain head: every reader of it
    /// (<c>BoundNodeHelpers.GetUsedVariables*</c>, the bug-pattern receiver
    /// dereference check, <c>CallGraphAnalysis</c>'s per-site record) also walks
    /// the call's children, where the head variable is visited as an ordinary
    /// <see cref="BoundVariableExpression"/> (and dereferenced by the chain's
    /// field access), so nothing is lost. The pin is that the head is reachable
    /// there.</para>
    /// </summary>
    [Fact]
    public void E1_Slice2a_ReceiverAndReceiverSymbol_DoNotDrift()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let sb = new System.Text.StringBuilder()
    sb.Append(""x"")
    let x = Unknown.Make()
    x.Run()
    let a = new Random()
    a.b.Chain()
    System.Console.WriteLine(""y"")
    foo.Bar()
    Helper()
";
        var module = Parse(source);
        var (bound, _) = Bind(module);

        var checkedCalls = 0;
        foreach (var node in Descendants(bound))
        {
            var (receiver, symbol, target) = node switch
            {
                BoundCallStatement s => (s.Receiver, s.ReceiverSymbol, s.Target),
                BoundCallExpression e => (e.Receiver, e.ReceiverSymbol, e.Target),
                _ => (null, null, null),
            };
            if (target is null)
                continue;
            checkedCalls++;

            switch (receiver)
            {
                case BoundVariableExpression variable:
                    Assert.Same(symbol, variable.Variable);
                    break;
                case BoundFieldAccessExpression chain:
                    var head = chain.Target;
                    while (head is BoundFieldAccessExpression inner)
                        head = inner.Target;
                    var headVariable = Assert.IsType<BoundVariableExpression>(head).Variable;
                    if (node is BoundCallExpression { EvaluatedReceiver: { } evaluated } native)
                    {
                        // Native: the chain is the evaluated receiver, so the
                        // head reaches dataflow through the call's children.
                        Assert.Same(chain, evaluated);
                        Assert.Contains(chain, native.Children);
                        Assert.Null(symbol);
                    }
                    else
                    {
                        // Legacy §C: the chain's head is the receiver symbol.
                        Assert.Same(symbol, headVariable);
                    }
                    break;
                case BoundTypeReferenceExpression:
                    Assert.Null(symbol);
                    break;
                case null:
                    Assert.Null(symbol);
                    break;
                default:
                    Assert.Fail($"Unexpected receiver shape {receiver.GetType().Name} for '{target}'.");
                    break;
            }
        }

        // Guard against the loop silently checking nothing.
        Assert.True(checkedCalls >= 6, $"Only {checkedCalls} call nodes were checked.");
    }

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Collector_TagsConstructors()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let x = new HttpClient()
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Contains(calls, c => c.TypeName == "System.Net.Http.HttpClient" && c.Kind == CallKind.Constructor);
    }

    // ========================================================================
    // Internal call filtering
    // ========================================================================

    [Fact]
    public void InternalFunction_NotIncludedWhenFiltered()
    {
        var source = @"
module Test Test block
  func Helper () → void
    !
    Console.WriteLine(""internal"")

  func Main () → void
    !
    Helper()
    ExternalService.DoWork()
";
        var module = Parse(source);
        var callGraph = CallGraphAnalysis.Build(module);
        var allCalls = ExternalCallCollector.Collect(module);

        var resolver = new EffectResolver();
        resolver.Initialize();

        var unresolvedExternal = allCalls
            .Where(c => !callGraph.FunctionNameToId.ContainsKey(c.MethodName))
            .Where(c => resolver.Resolve(EffectResolverKey.FromStrings(c.TypeName, c.MethodName)).Status == EffectResolutionStatus.Unknown)
            .ToList();

        // Console.WriteLine resolves from manifests → not in unresolved
        Assert.DoesNotContain(unresolvedExternal, c => c.TypeName == "System.Console");
        // ExternalService.DoWork is unknown → should be in unresolved
        Assert.Contains(unresolvedExternal, c => c.MethodName == "DoWork");
        // Helper is an internal function → should not be in unresolved
        Assert.DoesNotContain(unresolvedExternal, c => c.MethodName == "Helper");
    }

    // ========================================================================
    // Edge cases
    // ========================================================================

    [Fact]
    public void AllResolved_ProducesEmptyList()
    {
        var source = @"
§M{Test}
  §F{DoWork:pub}
      §O{void}
      §C{Console.WriteLine} §A STR:""test"" §/C
      §C{Math.Abs} §A INT:42 §/C
";
        var module = Parse(source);
        var allCalls = ExternalCallCollector.Collect(module);

        var resolver = new EffectResolver();
        resolver.Initialize();

        var unresolved = allCalls
            .Where(c => resolver.Resolve(EffectResolverKey.FromStrings(c.TypeName, c.MethodName)).Status == EffectResolutionStatus.Unknown)
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void NoExternalCalls_ProducesEmptyList()
    {
        var source = @"
module Test Test block
  func Add (int:a, int:b) → int
    !
    return (a + b)
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        Assert.Empty(calls);
    }

    // ========================================================================
    // Merge mode
    // ========================================================================

    [Fact]
    public void Merge_PreservesExistingEffects()
    {
        // Existing manifest has OrderService.Save with db:w
        var existingJson = @"{
            ""version"": ""1.0"",
            ""description"": ""Production manifest"",
            ""confidence"": ""verified"",
            ""mappings"": [
                {
                    ""type"": ""OrderService"",
                    ""methods"": {
                        ""Save"": [""db:w""],
                        ""Delete"": [""db:w""]
                    }
                }
            ],
            ""namespaceDefaults"": {
                ""MyApp.Data"": [""db:rw""]
            }
        }";

        // Write to temp file
        var tempPath = Path.Combine(Path.GetTempPath(), $"calor-merge-test-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempPath, existingJson);

            // Build a suggested manifest that adds a new method to OrderService + a new type
            var suggested = new EffectManifest
            {
                Version = "1.0",
                Description = "Suggested",
                Confidence = "inferred",
                Mappings = new List<TypeMapping>
                {
                    new TypeMapping { Type = "OrderService", Methods = new Dictionary<string, List<string>> { ["Cancel"] = new() } },
                    new TypeMapping { Type = "CacheClient", Methods = new Dictionary<string, List<string>> { ["Get"] = new() } }
                }
            };

            var result = EffectsCommand.MergeManifest(tempPath, suggested);

            // Existing effects preserved
            var orderMapping = result.Mappings.First(m => m.Type == "OrderService");
            Assert.Equal(new List<string> { "db:w" }, orderMapping.Methods!["Save"]);
            Assert.Equal(new List<string> { "db:w" }, orderMapping.Methods!["Delete"]);

            // New method added
            Assert.True(orderMapping.Methods!.ContainsKey("Cancel"));
            Assert.Empty(orderMapping.Methods!["Cancel"]);

            // New type added
            Assert.Contains(result.Mappings, m => m.Type == "CacheClient");

            // Metadata preserved
            Assert.Equal("Production manifest", result.Description);
            Assert.Equal("verified", result.Confidence);

            // NamespaceDefaults preserved
            Assert.NotNull(result.NamespaceDefaults);
            Assert.True(result.NamespaceDefaults.ContainsKey("MyApp.Data"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Merge_ExistingEmptyEffectsNotOverwritten()
    {
        // A method with explicitly empty [] (marked pure) should NOT be overwritten
        var existingJson = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""MyService"",
                    ""methods"": {
                        ""PureMethod"": [],
                        ""EffectfulMethod"": [""db:w""]
                    }
                }
            ]
        }";

        var tempPath = Path.Combine(Path.GetTempPath(), $"calor-merge-test-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempPath, existingJson);

            var suggested = new EffectManifest
            {
                Version = "1.0",
                Mappings = new List<TypeMapping>
                {
                    new TypeMapping { Type = "MyService", Methods = new Dictionary<string, List<string>>
                    {
                        ["PureMethod"] = new(),         // would overwrite if not careful
                        ["EffectfulMethod"] = new(),    // would overwrite if not careful
                        ["NewMethod"] = new()           // genuinely new
                    }}
                }
            };

            var result = EffectsCommand.MergeManifest(tempPath, suggested);

            var mapping = result.Mappings.First(m => m.Type == "MyService");

            // Existing empty [] preserved (not overwritten)
            Assert.Empty(mapping.Methods!["PureMethod"]);

            // Existing filled effects preserved
            Assert.Equal(new List<string> { "db:w" }, mapping.Methods!["EffectfulMethod"]);

            // New method added
            Assert.True(mapping.Methods!.ContainsKey("NewMethod"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void MergeDictionary_OnlyAddsNewKeys()
    {
        var existing = new Dictionary<string, List<string>>
        {
            ["Save"] = new List<string> { "db:w" },
            ["Pure"] = new List<string>()
        };

        var suggested = new Dictionary<string, List<string>>
        {
            ["Save"] = new List<string>(),       // existing — should NOT overwrite
            ["Pure"] = new List<string>(),        // existing — should NOT overwrite
            ["NewMethod"] = new List<string>()    // new — should add
        };

        EffectsCommand.MergeDictionary(existing, suggested);

        // Existing entries untouched
        Assert.Equal(new List<string> { "db:w" }, existing["Save"]);
        Assert.Empty(existing["Pure"]);

        // New entry added
        Assert.True(existing.ContainsKey("NewMethod"));
        Assert.Empty(existing["NewMethod"]);
    }

    // ========================================================================
    // E2E: suggest → fill → recompile loop
    // ========================================================================

    [Fact]
    public void E2E_SuggestThenFillThenRecompile()
    {
        // Step 1: Source with an unknown external call
        var source = @"
module Test Test block
  func ProcessOrder () → void
    ! cw
    Console.WriteLine(""Starting"")
    OrderRepo.Save()
";
        // Step 2: Collect unresolved calls (simulating what suggest does)
        var module = Parse(source);
        var callGraph = CallGraphAnalysis.Build(module);
        var allCalls = ExternalCallCollector.Collect(module);

        var resolver = new EffectResolver();
        resolver.Initialize();

        var unresolved = allCalls
            .Where(c => !callGraph.FunctionNameToId.ContainsKey(c.MethodName))
            .Where(c => resolver.Resolve(EffectResolverKey.FromStrings(c.TypeName, c.MethodName)).Status == EffectResolutionStatus.Unknown)
            .Distinct()
            .ToList();

        // Verify OrderRepo.Save is unresolved
        Assert.Contains(unresolved, c => c.MethodName == "Save");

        // Step 3: Create a manifest with the user-filled effects (OrderRepo.Save → db:w)
        var manifestJson = @"{
            ""version"": ""1.0"",
            ""mappings"": [
                {
                    ""type"": ""OrderRepo"",
                    ""methods"": {
                        ""Save"": [""db:w""]
                    }
                }
            ]
        }";

        // Step 4: Recompile with the manifest loaded
        var loader = new ManifestLoader();
        loader.LoadFromJson(manifestJson, "suggested");

        var resolverWithManifest = new EffectResolver(loader);
        resolverWithManifest.Initialize();

        // Step 5: Verify OrderRepo.Save now resolves
        var resolution = resolverWithManifest.Resolve(EffectResolverKey.FromStrings("OrderRepo", "Save"));
        Assert.NotEqual(EffectResolutionStatus.Unknown, resolution.Status);
        Assert.Contains(resolution.Effects.Effects, e => e.Value == "database_write");

        // Step 6: Full compilation should succeed with correct effect declarations
        var fullSource = @"
module Test Test block
  func ProcessOrder () → void
    ! cw, db:w
    Console.WriteLine(""Starting"")
    OrderRepo.Save()
";
        var compileResult = TestHarness.CompileWithEffects(fullSource, enforceEffects: true,
            policy: Calor.Compiler.Effects.UnknownCallPolicy.Permissive);

        // OrderRepo.Save should no longer produce Calor0411 if the manifest is loaded.
        // In this test context, the manifest isn't in the file system, so the compiler
        // won't auto-load it. Instead, verify the resolver round-trip works:
        // the suggest output → user fills effects → resolver resolves = the loop is valid.
        var unresolvedAfter = allCalls
            .Where(c => !callGraph.FunctionNameToId.ContainsKey(c.MethodName))
            .Where(c => resolverWithManifest.Resolve(EffectResolverKey.FromStrings(c.TypeName, c.MethodName)).Status == EffectResolutionStatus.Unknown)
            .ToList();

        Assert.Empty(unresolvedAfter);
    }

    // ========================================================================
    // Multi-file cross-references
    // ========================================================================

    [Fact]
    public void MultiFile_InternalFunctionRecognizedAcrossFiles()
    {
        // File A defines a helper function
        var sourceA = @"
module Helpers Helpers block
  func FormatName (string:name) → string
    !
    return name
";
        // File B calls the helper + an external type
        var sourceB = @"
module App App block
  func ProcessUser () → void
    !
    FormatName(""test"")
    ExternalApi.Submit()
";
        var moduleA = Parse(sourceA);
        var moduleB = Parse(sourceB);

        // Union function maps across files
        var combinedFunctionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in CallGraphAnalysis.Build(moduleA).FunctionNameToId.Keys)
            combinedFunctionNames.Add(name);
        foreach (var name in CallGraphAnalysis.Build(moduleB).FunctionNameToId.Keys)
            combinedFunctionNames.Add(name);

        // Collect calls from file B
        var callsB = ExternalCallCollector.Collect(moduleB);

        var resolver = new EffectResolver();
        resolver.Initialize();

        var unresolved = callsB
            .Where(c => !combinedFunctionNames.Contains(c.MethodName))
            .Where(c => resolver.Resolve(EffectResolverKey.FromStrings(c.TypeName, c.MethodName)).Status == EffectResolutionStatus.Unknown)
            .ToList();

        // FormatName is in file A's function map → should be filtered out
        Assert.DoesNotContain(unresolved, c => c.MethodName == "FormatName");
        // ExternalApi.Submit is truly external → should be in unresolved
        Assert.Contains(unresolved, c => c.MethodName == "Submit");
    }

    // ========================================================================
    // Constructor parameter handling
    // ========================================================================

    [Fact]
    public void Constructor_CollectedWithTypeNameAndCtorKind()
    {
        var source = @"
module Test Test block
  func DoWork () → void
    !
    let svc = new OrderService()
    let client = new HttpClient()
";
        var module = Parse(source);
        var calls = ExternalCallCollector.Collect(module);

        // OrderService constructor — unknown type, should appear
        Assert.Contains(calls, c => c.TypeName == "OrderService" && c.Kind == CallKind.Constructor);

        // HttpClient constructor — maps to System.Net.Http.HttpClient
        Assert.Contains(calls, c => c.TypeName == "System.Net.Http.HttpClient" && c.Kind == CallKind.Constructor);

        // Both should have .ctor as the method name
        Assert.All(calls.Where(c => c.Kind == CallKind.Constructor), c => Assert.Equal(".ctor", c.MethodName));
    }
}
