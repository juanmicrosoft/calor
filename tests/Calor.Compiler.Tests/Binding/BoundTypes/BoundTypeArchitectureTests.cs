using System.Reflection;
using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// v0.14 §F-3 ratchet + §S2 architecture pins for the BoundType hierarchy.
/// </summary>
public class BoundTypeArchitectureTests
{
    /// <summary>
    /// F-3 ratchet — the count of <c>TypeName ==</c>/<c>TypeName !=</c>
    /// occurrences in <c>src/</c> is strictly monotonically decreasing.
    /// Baseline is committed at <c>bench/phase0-agent-native/typename-equality-baseline.json</c>
    /// and lowered as S3–S6 migrate call sites off string equality; the
    /// property itself is deleted at S7 (F-5).
    /// </summary>
    [Fact]
    public void TypeName_StringEquality_NotAddedAfterFreeze()
    {
        var repoRoot = CliTestHarness.FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcRoot),
            $"src/ not found from working directory. Run from a Calor checkout. srcRoot={srcRoot}");

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Contains("TypeName ==") || line.Contains("TypeName !="))
                {
                    count++;
                }
            }
        }

        var baselinePath = Path.Combine(repoRoot,
            "bench", "phase0-agent-native", "typename-equality-baseline.json");
        Assert.True(File.Exists(baselinePath),
            $"F-3 baseline not found: {baselinePath}. This file freezes at S2's merge and must exist.");

        using var stream = File.OpenRead(baselinePath);
        var baseline = JsonSerializer.Deserialize<JsonElement>(stream);
        var baselineCount = baseline.GetProperty("baselineCount").GetInt32();

        Assert.True(count <= baselineCount,
            $"F-3 ratchet violated: found {count} TypeName ==/!= occurrences in src/, " +
            $"baseline is {baselineCount}. New string-equality sites are forbidden — " +
            "use .Type instead of .TypeName. To lower the baseline (permitted), " +
            "migrate call sites and update the baseline JSON in the same PR.");
    }

    /// <summary>
    /// v0.14 §D1 pin — no <c>BoundType</c> subclass may provide a
    /// <c>ParseFromString</c>-shaped method. The reverse of the S2 shim is
    /// prohibited: structural information lives on the symbol; the string is
    /// a leaf artifact for diagnostics only.
    /// </summary>
    [Fact]
    public void BoundType_HasNo_ParseFromString_Method()
    {
        var boundTypeAssembly = typeof(BoundType).Assembly;
        var boundTypeTypes = boundTypeAssembly.GetTypes()
            .Where(t => typeof(BoundType).IsAssignableFrom(t))
            .ToList();

        foreach (var t in boundTypeTypes)
        {
            var offending = t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
                .Where(m => m.Name.Contains("Parse", StringComparison.OrdinalIgnoreCase)
                            && m.ReturnType == typeof(BoundType)
                            && m.GetParameters().Length == 1
                            && m.GetParameters()[0].ParameterType == typeof(string))
                .ToList();

            Assert.Empty(offending);
        }
    }

    /// <summary>
    /// v0.14 F-5 exit criterion — every concrete <see cref="BoundExpression"/>
    /// subclass declares its own <c>Type</c> property override. Post-F-5 the
    /// base class no longer stores type as a string (the <c>abstract string
    /// TypeName</c> shim was deleted); each subclass provides its Type as a
    /// <see cref="BoundType"/>. Reflection is used because the check is
    /// declarative — we care that every subclass owns its Type source of
    /// truth, not that any particular instance can be constructed.
    /// </summary>
    [Fact]
    public void EveryConcreteBoundExpression_DeclaresTypeOverride_PostF5()
    {
        var assembly = typeof(BoundExpression).Assembly;
        var concreteBoundExpressionTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(BoundExpression).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(concreteBoundExpressionTypes);

        foreach (var t in concreteBoundExpressionTypes)
        {
            var typeProperty = t.GetProperty(nameof(BoundExpression.Type),
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(typeProperty);
            Assert.NotNull(typeProperty!.GetMethod);
            Assert.False(typeProperty.GetMethod!.IsStatic);

            // F-5 pin: Type is abstract on BoundExpression, so every concrete
            // subclass must provide its own override. The getter's declaring
            // type must be the subclass itself (or an intermediate abstract
            // base within the BoundExpression hierarchy that provides Type).
            var declaring = typeProperty.GetMethod.DeclaringType;
            Assert.NotSame(typeof(BoundExpression), declaring);
            Assert.True(
                typeof(BoundExpression).IsAssignableFrom(declaring),
                $"Type getter for {t.Name} has declaringType={declaring?.Name}, " +
                "which is not in the BoundExpression hierarchy.");
        }
    }

    /// <summary>
    /// Cache-invariance pin — <c>Type.DisplayString</c> is the string that
    /// verifier caches and effect-resolver dictionaries key on, so it must not
    /// drift silently. Each sampler entry names the exact expected string.
    ///
    /// <para>Until v0.15 E1 slice 2a this compared <c>DisplayString</c> to
    /// itself: the assertion was written against the removed <c>TypeName</c>
    /// shim, and the F-5 shim deletion rewrote both sides of the comparison
    /// into the same expression, leaving a tautology that could not fail.
    /// The expected strings below are the committed golden that replaces it.</para>
    /// </summary>
    [Fact]
    public void BoundExpressionType_DisplayString_ByteIdenticalToTypeName_OnCommonSampler()
    {
        var span = new TextSpan(0, 0, 0, 0);
        var sampler = new (BoundExpression Expression, string ExpectedDisplayString)[]
        {
            (new BoundBoolLiteral(span, true), "BOOL"),
            (new BoundIntLiteral(span, 42), "INT"),
            (new BoundStringLiteral(span, "hello", isMultiline: false, isUtf8: false), "STRING"),
            // UTF-8 branch: the load-bearing non-primitive case — generic-angle
            // brackets that a naive normalization might trim.
            (new BoundStringLiteral(span, "hello", isMultiline: false, isUtf8: true),
                "ReadOnlySpan<BYTE>"),
            (new BoundDecimalLiteral(span, 1.5m), "DECIMAL"),
            (new BoundNoneLiteral(span), "NONE"),
        };

        foreach (var (expression, expected) in sampler)
        {
            Assert.Equal(expected, expression.Type.DisplayString);
        }
    }

    /// <summary>
    /// V-1 gap #25 — corpus cache-invariance. Binds every Calor source file
    /// under <c>samples/</c> and <c>benchmarks/</c> and compares the resulting
    /// <c>DisplayString</c> distribution — every distinct
    /// (bound-node type, DisplayString) pair with its count — against a
    /// committed golden. If any node's <c>Type</c> ever starts producing a
    /// different string, the verifier cache and the effect resolver's
    /// dictionaries silently invalidate; this is what catches that first.
    ///
    /// <para>Until v0.15 E1 slice 2a this asserted
    /// <c>DisplayString == DisplayString</c>. It was written against the
    /// <c>TypeName</c> shim, and the F-5 shim deletion rewrote both sides into
    /// the same expression — leaving a corpus walk that bound hundreds of
    /// modules and could not fail. v0.15 E1 slice 2a's scope limitation
    /// (<c>UnresolvedBoundType</c> confined to receiver positions, so no
    /// existing expression's <c>DisplayString</c> moves) rests on this pin, so
    /// the pin had to become real before the limitation could be claimed.</para>
    ///
    /// <para>Regenerate: <c>CALOR_REGENERATE_DISPLAYSTRING_GOLDEN=1 dotnet test
    /// --filter AcrossCalorSourceCorpus</c>. A moved row is a decision — record
    /// what moved and why in the PR that regenerates it.</para>
    /// </summary>
    [Fact]
    public void BoundExpressionType_DisplayString_ByteIdenticalToTypeName_AcrossCalorSourceCorpus()
    {
        var repoRoot = CliTestHarness.FindRepoRoot();
        var corpusRoots = new[]
        {
            Path.Combine(repoRoot, "samples"),
            Path.Combine(repoRoot, "benchmarks"),
        };

        var files = corpusRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.calr", SearchOption.AllDirectories))
            .ToArray();

        Assert.NotEmpty(files);

        var histogram = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var modulesBound = 0;
        var expressionsChecked = 0;

        foreach (var file in files)
        {
            try
            {
                var source = File.ReadAllText(file);
                var lexDiagnostics = new DiagnosticBag();
                var lexer = new Lexer(source, lexDiagnostics);
                var tokens = lexer.TokenizeAllForParser();
                var parseDiagnostics = new DiagnosticBag();
                var parser = new Parser(tokens, parseDiagnostics);
                var module = parser.Parse();

                var bindDiagnostics = new DiagnosticBag();
                var binder = new Calor.Compiler.Binding.Binder(bindDiagnostics);
                var boundModule = binder.Bind(module);
                modulesBound++;

                foreach (var boundExpr in EnumerateBoundExpressions(boundModule))
                {
                    expressionsChecked++;
                    var key = $"{boundExpr.GetType().Name}|{boundExpr.Type.DisplayString}";
                    histogram[key] = histogram.GetValueOrDefault(key) + 1;
                }
            }
            catch (Exception)
            {
                // Some samples may fail to bind cleanly — skip them. The point
                // is to catch TypeName-vs-DisplayString drift on modules that
                // DO bind; a corpus-file bind failure is not what this pin is
                // about.
            }
        }

        // Anti-vacuity: without a real denominator every comparison below passes.
        Assert.True(modulesBound > 0,
            $"No .calr modules bound cleanly. Corpus roots: {string.Join(", ", corpusRoots)}");
        Assert.True(expressionsChecked > 400,
            $"Only {expressionsChecked} bound expressions checked; the corpus denominator "
            + "collapsed, so the golden comparison would be vacuous.");

        var goldenPath = Path.Combine(
            repoRoot, "tests", "TestData", "BoundTypes", "displaystring-corpus-golden.json");
        Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);

        if (string.Equals(
                Environment.GetEnvironmentVariable("CALOR_REGENERATE_DISPLAYSTRING_GOLDEN"),
                "1", StringComparison.Ordinal))
        {
            File.WriteAllText(goldenPath, System.Text.Json.JsonSerializer.Serialize(
                histogram, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine($"DisplayString corpus golden regenerated: {goldenPath}");
            return;
        }

        // A missing golden is a failure, never a silent regeneration (R2-A: the
        // BinderIncompleteRatchetTests pattern). Regenerate only via the env var.
        Assert.True(File.Exists(goldenPath),
            $"DisplayString corpus golden missing at {goldenPath} — run once with CALOR_REGENERATE_DISPLAYSTRING_GOLDEN=1");
        var golden = System.Text.Json.JsonSerializer
            .Deserialize<SortedDictionary<string, int>>(File.ReadAllText(goldenPath))!;

        var drifted = new List<string>();
        foreach (var key in golden.Keys.Union(histogram.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            var expected = golden.GetValueOrDefault(key);
            var actual = histogram.GetValueOrDefault(key);
            if (expected != actual)
                drifted.Add($"{key}: golden {expected}, measured {actual}");
        }

        Assert.True(drifted.Count == 0,
            "Bound-expression DisplayString distribution drifted over the committed corpus — "
            + "the verifier cache and the effect resolver key on these exact strings. "
            + "Regenerate with CALOR_REGENERATE_DISPLAYSTRING_GOLDEN=1 in the PR that moves them, "
            + "naming what moved:\n  " + string.Join("\n  ", drifted.Take(25)));
    }

    /// <summary>
    /// v0.15 E1 slice 2b — a lambda's bound type is a
    /// <see cref="FunctionBoundType"/> (kind 5), not a
    /// <see cref="NominalBoundType"/> whose name happens to spell a signature,
    /// AND its <c>DisplayString</c> is byte-identical to the pre-slice
    /// <c>LAMBDA(...)-&gt;...</c> string.
    ///
    /// <para>Both halves matter. The kind is what lets the effect pass and
    /// <c>ExternalCallCollector</c> answer "is this a function type?"
    /// structurally. The string has to stay because <c>Binder.cs:1320</c>
    /// infers an untyped <c>§B</c>'s <c>TypeName</c> from the initializer's
    /// <c>DisplayString</c>, so a change here would move OTHER expressions'
    /// display strings — the thing the corpus golden above pins.</para>
    ///
    /// <para>Discriminates: drop <c>displayOverride</c> and the string becomes
    /// <c>(i32) -&gt; INT</c>; revert to <c>NominalBoundType</c> and the kind
    /// assertion fails.</para>
    /// </summary>
    [Theory]
    [InlineData(
        "§B{f} §LAM{l1:x:i32} (+ x INT:1)",
        "LAMBDA(i32)->INT")]
    [InlineData(
        "§B{g} §LAM{l2}",
        "LAMBDA()->VOID")]
    public void LambdaExpression_BindsToFunctionBoundType_WithUnchangedDisplayString(
        string bindingLine,
        string expectedDisplay)
    {
        var source = string.Join("\n", [
            "§M{m1:LambdaTypeProbe}",
            "  §F{f1:Main:pub} () -> void",
            "    §E{}",
            $"    {bindingLine}",
            "",
        ]);

        var lambdas = BindAndCollect<BoundLambdaExpression>(source);
        var lambda = Assert.Single(lambdas);

        var functionType = Assert.IsType<FunctionBoundType>(lambda.Type);
        Assert.Equal(expectedDisplay, functionType.DisplayString);
    }

    /// <summary>
    /// The structural half of the item above, stated as the property the
    /// consumers depend on: the parameter and return slots carry real
    /// <see cref="BoundType"/>s, so a consumer never has to parse the display
    /// string back into a type (the registered anti-pattern on
    /// <see cref="BoundType"/>).
    /// </summary>
    [Fact]
    public void LambdaFunctionBoundType_CarriesParameterAndReturnTypes()
    {
        var source = string.Join("\n", [
            "§M{m1:LambdaShapeProbe}",
            "  §F{f1:Main:pub} () -> void",
            "    §E{}",
            "    §B{f} §LAM{l1:x:i32:y:str} (+ x INT:1)",
            "",
        ]);

        var lambda = Assert.Single(BindAndCollect<BoundLambdaExpression>(source));
        var functionType = Assert.IsType<FunctionBoundType>(lambda.Type);

        Assert.Equal(
            "i32, str",
            string.Join(", ", functionType.ParameterTypes.Select(t => t.DisplayString)));
        // The expression body's OWN bound type, handed over by the binder — not
        // a string re-parsed into a type.
        Assert.Equal("INT", functionType.ReturnType.DisplayString);
    }

    /// <summary>
    /// v0.15 E1 slice 2b — the side channel the effect pass reads receivers
    /// through. <c>CallGraphAnalysis.BoundValueTypes(callerId)</c> hands over
    /// the bound <see cref="BoundType"/> of each call site's receiver, keyed by
    /// the receiver path as the target spells it.
    ///
    /// <para>This is the structural pin for the slice: the API does not exist
    /// on <c>main</c>, and the effect pass's resolvers now consult it before any
    /// AST type string. Delete the <c>RecordReceiver</c> call in
    /// <c>ResolveBoundCallSites</c> and the assertions below go empty.</para>
    ///
    /// <para>Also pins the two invariants the resolvers depend on: a receiver
    /// the binder typed arrives NOMINAL (so it can be keyed as a manifest
    /// type), and a receiver it could not type arrives
    /// <see cref="UnresolvedBoundType"/> (so the resolver can fail closed
    /// instead of guessing).</para>
    /// </summary>
    [Fact]
    public void CallGraphSideChannel_CarriesBoundReceiverTypesPerCallSite()
    {
        var source = string.Join("\n", [
            "§M{m001:ReceiverChannelProbe}",
            "  §F{f001:Go:pub}",
            "      §I{i32:n}",
            "      §O{void}",
            "      §E{}",
            "      §B{sb} §NEW{StringBuilder}",
            "      §C{sb.AppendLine} §A STR:\"x\" §/C",
            "      §B{u} §C{Whatever.Make} §/C",
            "      §C{u.Run} §/C",
            "",
        ]);

        var lexDiagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, lexDiagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, new DiagnosticBag()).Parse();
        var analysis = Calor.Compiler.Analysis.CallGraphAnalysis.Build(module);

        Assert.True(analysis.IsBoundResolutionComplete);
        var receivers = analysis.BoundValueTypes("f001");

        // Anti-vacuity: the probe has two dotted call sites.
        Assert.True(
            receivers.Count >= 2,
            $"side channel carried {receivers.Count} receivers; the probe has two");

        // A receiver the binder typed: nominal, keyable as a manifest type.
        var stringBuilder = Assert.IsType<NominalBoundType>(receivers["sb"]);
        Assert.Equal("StringBuilder", stringBuilder.QualifiedName);

        // A receiver the binder could not type — an inferred §B whose
        // initializer it cannot resolve. This is the §D6 exit ramp, and it is
        // why ResolveLocalValueType returns null here instead of guessing.
        Assert.IsType<UnresolvedBoundType>(receivers["u"]);
    }

    private static IReadOnlyList<T> BindAndCollect<T>(string source)
        where T : BoundNode
    {
        var lexDiagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, lexDiagnostics).TokenizeAllForParser();
        var parseDiagnostics = new DiagnosticBag();
        var module = new Parser(tokens, parseDiagnostics).Parse();
        var bound = new Calor.Compiler.Binding.Binder(new DiagnosticBag()).Bind(module);
        return Descendants(bound).OfType<T>().ToArray();
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

    private static IEnumerable<BoundExpression> EnumerateBoundExpressions(BoundNode node)
    {
        if (node is BoundExpression expr)
        {
            yield return expr;
        }
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in EnumerateBoundExpressions(child))
            {
                yield return descendant;
            }
        }
    }
}
