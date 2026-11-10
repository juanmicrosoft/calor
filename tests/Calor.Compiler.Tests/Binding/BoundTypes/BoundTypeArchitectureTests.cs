using System.Reflection;
using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
            (new BoundNullLiteral(span), "NULL"),
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
    /// <para>The frozen golden replays with its original compiler and inputs.
    /// Current native representations have separate, exact per-subject expectations;
    /// neither parse failures nor exceptions can remove a subject.</para>
    /// </summary>
    [Fact]
    public async Task BoundExpressionType_DisplayString_ByteIdenticalToTypeName_AcrossCalorSourceCorpus()
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

        var fixturePath = Path.Combine(repoRoot, "tests", "TestData", "BoundTypes",
            "displaystring-current-expectations.json");
        var expected = JsonSerializer.Deserialize<SortedDictionary<string, CorpusExpectation>>(
            File.ReadAllText(fixturePath))!;
        Assert.Equal(18, files.Length);
        Assert.Equal(expected.Keys, files.Select(file =>
            Path.GetRelativePath(repoRoot, file).Replace('\\', '/')).Order(StringComparer.Ordinal));
        var expressionsChecked = 0;

        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            Assert.Contains(relative, ActiveSyntaxInputs.OriginalPaths);
            var source = File.ReadAllText(ActiveSyntaxInputs.ForCurrentCompiler(repoRoot, file));
            var module = Parse(source);
            var generated = CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(module)).GetRoot();
            Assert.Equal(module.Name,
                Assert.Single(generated.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()).Name.ToString());
            Assert.Contains(generated.DescendantNodes().OfType<ClassDeclarationSyntax>(),
                declaration => declaration.Identifier.ValueText == module.Name + "Module");
            var boundModule = Bind(module);
            var histogram = Histogram(boundModule);
            expressionsChecked += histogram.Values.Sum();
            Assert.Equal(expected[relative].Functions,
                boundModule.Functions.Select(function => function.Symbol.Name));
            Assert.True(expected[relative].Histogram.OrderBy(pair => pair.Key, StringComparer.Ordinal).SequenceEqual(histogram),
                $"{relative}: bound-expression distribution changed.\n"
                + JsonSerializer.Serialize(histogram, new JsonSerializerOptions { WriteIndented = true }));
            var emitted = new CalorEmitter().Emit(module);
            Assert.DoesNotContain('§', emitted);
            var reparsed = Parse(emitted);
            Assert.Equal(histogram, Histogram(Bind(reparsed)));
            if (relative.StartsWith("samples/", StringComparison.Ordinal))
            {
                foreach (var candidate in new[] { source, emitted })
                {
                    var compiled = Program.Compile(candidate, relative);
                    Assert.False(compiled.HasErrors, $"{relative}:\n{string.Join("\n", compiled.Diagnostics)}");
                    LoadAssembly(compiled.GeneratedCode);
                }
            }
            if (relative == "samples/Contracts/contracts.calr")
                AssertContractMessageTransition(module);
            if (relative == "benchmarks/arithmetic/overflow.calr")
                AssertCheckedBoundary(source, module);
        }

        Assert.True(expressionsChecked > 400,
            $"Only {expressionsChecked} bound expressions checked; the corpus denominator "
            + "collapsed, so the golden comparison would be vacuous.");

        var historical = await ExecutionWorkspace.CaptureProcessAsync(
            "python3", [Path.Combine(repoRoot, "scripts", "registered_ledger_historical_replay.py"), "bound-types"],
            repoRoot, TimeSpan.FromMinutes(20), CancellationToken.None);
        Assert.True(historical.ExitCode == 0 && !historical.TimedOut,
            $"Historical DisplayString golden replay failed (exit {historical.ExitCode}).\n{historical.Output}");
    }

    private sealed record CorpusExpectation(
        [property: System.Text.Json.Serialization.JsonPropertyName("histogram")] SortedDictionary<string, int> Histogram,
        [property: System.Text.Json.Serialization.JsonPropertyName("functions")] string[] Functions);

    private static SortedDictionary<string, int> Histogram(BoundModule module)
        => new(EnumerateBoundExpressions(module)
            .GroupBy(expression => $"{expression.GetType().Name}|{expression.Type.DisplayString}",
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            StringComparer.Ordinal);

    [Fact]
    public void CorpusPatternBindings_PreserveTheIntegerScrutineeType()
    {
        var root = CliTestHarness.FindRepoRoot();
        var source = File.ReadAllText(ActiveSyntaxInputs.ForCurrentCompiler(root,
            Path.Combine(root, "samples", "PatternMatching", "matching.calr")));
        var references = BindAndCollect<BoundVariableExpression>(source)
            .Where(reference => reference.Variable.Name == "n").ToArray();

        Assert.Equal(3, references.Length);
        Assert.Equal(3, references.Select(reference => reference.SymbolId).Distinct().Count());
        Assert.All(references, reference =>
        {
            Assert.Equal("INT", reference.Type.DisplayString);
            Assert.False(reference.HasUnknownInferredType);
        });
    }

    [Theory]
    [InlineData("benchmarks/security/command-injection.calr", "Replace", 9, "string")]
    [InlineData("benchmarks/security/sql-injection.calr", "Replace", 1, "string")]
    [InlineData("benchmarks/null-safety/null-deref.calr", "GetHashCode", 2, "int")]
    public void CorpusScalarInstanceCalls_UseSelectedStringMetadata(
        string relativePath, string methodName, int expectedCount, string expectedDisplay)
    {
        var root = CliTestHarness.FindRepoRoot();
        var source = File.ReadAllText(ActiveSyntaxInputs.ForCurrentCompiler(root,
            Path.Combine(root, relativePath)));
        var calls = BindAndCollect<BoundCallExpression>(source)
            .Where(call => call.ResolvedMetadataMethod?.Name == methodName).ToArray();

        Assert.Equal(expectedCount, calls.Length);
        var oracleTree = CSharpSyntaxTree.ParseText(methodName == "Replace"
            ? "class Oracle { string Run(string receiver) => receiver.Replace(\"a\", \"b\"); }"
            : "class Oracle { int Run(string receiver) => receiver.GetHashCode(); }");
        var oracle = CSharpCompilation.Create("ScalarOracle", [oracleTree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(oracle.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        var oracleMethod = Assert.IsAssignableFrom<IMethodSymbol>(oracle.GetSemanticModel(oracleTree)
            .GetSymbolInfo(Assert.Single(oracleTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())).Symbol);
        Assert.All(calls, call =>
        {
            Assert.Equal("<expression-call>", call.Target);
            var receiver = Assert.IsType<BoundVariableExpression>(call.EvaluatedReceiver);
            Assert.Same(receiver, call.Receiver);
            Assert.Same(receiver.Variable, call.ReceiverSymbol);
            Assert.False(receiver.SymbolId.IsNone);
            Assert.Equal(receiver.Variable.Name, source.Substring(receiver.Span.Start, receiver.Span.Length));
            Assert.False(receiver.HasUnknownInferredType);
            var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.ResolvedMetadataMethod);
            Assert.Equal(SpecialType.System_String, selected.ContainingType.SpecialType);
            Assert.Equal(oracleMethod.ToDisplayString(), selected.ToDisplayString());
            Assert.Equal(methodName, selected.Name);
            Assert.False(selected.IsStatic);
            Assert.Empty(selected.DeclaringSyntaxReferences);
            Assert.Equal(expectedDisplay, call.Type.DisplayString);
            Assert.False(call.HasUnresolvedArguments);
        });
    }

    /// <summary>
    /// v0.15 E1 slice 2b — a lambda's bound type is a
    /// <see cref="FunctionBoundType"/> (kind 5), not a
    /// <see cref="NominalBoundType"/> whose name happens to spell a signature,
    /// AND its <c>DisplayString</c> retains the explicit
    /// <c>LAMBDA(...)-&gt;...</c> shape with current native type spellings.
    ///
    /// <para>Both halves matter. The kind is what lets the effect pass and
    /// <c>ExternalCallCollector</c> answer "is this a function type?"
    /// structurally. Inferred bindings also consume the initializer's display
    /// string; the current corpus fixture pins those downstream strings
    /// separately from the unchanged historical golden.</para>
    ///
    /// <para>Discriminates: drop <c>displayOverride</c> and the string becomes
    /// <c>(int) -&gt; INT</c>; revert to <c>NominalBoundType</c> and the kind
    /// assertion fails.</para>
    /// </summary>
    [Theory]
    [InlineData(
        "let f = (int:x) => x + 1",
        "LAMBDA(int)->INT")]
    [InlineData(
        "let g = () =>\n      return",
        "LAMBDA()->VOID")]
    public void LambdaExpression_BindsToFunctionBoundType_WithUnchangedDisplayString(
        string bindingLine,
        string expectedDisplay)
    {
        var source = string.Join("\n", [
            "module LambdaTypeProbe",
            "  func Main public () → void",
            "    !",
            $"    {bindingLine}",
            "",
        ]);

        var lambdas = BindAndCollect<BoundLambdaExpression>(source);
        var lambda = Assert.Single(lambdas);

        var functionType = Assert.IsType<FunctionBoundType>(lambda.Type);
        Assert.Equal(expectedDisplay, functionType.DisplayString);
        Assert.Equal(expectedDisplay == "LAMBDA()->VOID" ? Array.Empty<string>() : new[] { "int" },
            functionType.ParameterTypes.Select(type => type.DisplayString));
        Assert.Equal(expectedDisplay == "LAMBDA()->VOID" ? "VOID" : "INT",
            functionType.ReturnType.DisplayString);
        AssertNativeLambdaRoundTrip(source, expectedDisplay);
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
            "module LambdaShapeProbe",
            "  func Main public () → void",
            "    !",
            "    let f = (int:x, string:y) => x + 1",
            "",
        ]);

        var lambda = Assert.Single(BindAndCollect<BoundLambdaExpression>(source));
        var functionType = Assert.IsType<FunctionBoundType>(lambda.Type);

        Assert.Equal(
            "int, string",
            string.Join(", ", functionType.ParameterTypes.Select(t => t.DisplayString)));
        // The expression body's OWN bound type, handed over by the binder — not
        // a string re-parsed into a type.
        Assert.Equal("INT", functionType.ReturnType.DisplayString);
        AssertNativeLambdaRoundTrip(source, "LAMBDA(int,string)->INT");
    }

    /// <summary>
    /// v0.15 E1 slice 2b — the side channel the effect pass reads receivers
    /// through. <c>CallGraphAnalysis.BoundValueTypes(callerId)</c> hands over
    /// the bound <see cref="BoundType"/> of each call site's receiver, keyed by
    /// the structural receiver path, not the native call's placeholder target.
    ///
    /// <para>This is the structural pin for the slice: effect resolvers consult it before any
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
            "module ReceiverChannelProbe",
            "  using System.Text",
            "  func Go public (int:n) → void",
            "    !",
            "    let sb = new StringBuilder()",
            "    sb.AppendLine(\"x\")",
            "    let u = Whatever.Make()",
            "    u.Run()",
            "",
        ]);

        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(module);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UndefinedReference);
        var receiverCalls = Descendants(bound).OfType<BoundCallExpression>()
            .Where(call => call.Receiver is BoundVariableExpression).ToArray();
        var known = Assert.Single(receiverCalls, call => call.ResolvedMetadataMethod?.Name == "AppendLine");
        Assert.Equal("System.Text.StringBuilder", known.ResolvedMetadataMethod!.ContainingType.ToDisplayString());
        var unknown = Assert.Single(receiverCalls, call =>
            call.Receiver is BoundVariableExpression { Variable.Name: "u" });
        Assert.Null(unknown.ResolvedMetadataMethod);
        Assert.IsType<UnresolvedBoundType>(unknown.Receiver!.Type);
        var analysis = Calor.Compiler.Analysis.CallGraphAnalysis.Build(module);

        Assert.True(analysis.IsBoundResolutionComplete);
        var receivers = analysis.BoundValueTypes(Assert.Single(module.Functions).Id);

        // Anti-vacuity: the probe has two dotted call sites.
        Assert.True(
            receivers.Count >= 2,
            $"side channel carried {receivers.Count} receivers; the probe has two");

        // A receiver the binder typed: nominal, keyable as a manifest type.
        var stringBuilder = Assert.IsType<NominalBoundType>(receivers["sb"]);
        Assert.Equal("StringBuilder", stringBuilder.QualifiedName);
        Assert.Equal(known.Receiver!.Type, stringBuilder);

        // A receiver the binder could not type — an inferred §B whose
        // initializer it cannot resolve. This is the §D6 exit ramp, and it is
        // why ResolveLocalValueType returns null here instead of guessing.
        Assert.Equal(Assert.IsType<UnresolvedBoundType>(unknown.Receiver.Type),
            Assert.IsType<UnresolvedBoundType>(receivers["u"]));

        const string allowed = """
            module ReceiverEffects
              using System.Text
              func Go () → void
                ! alloc,mut
                let sb = new StringBuilder()
                sb.AppendLine("x")
            """;
        var permitted = Program.Compile(allowed);
        Assert.False(permitted.HasErrors, string.Join("\n", permitted.Diagnostics));
        var denied = Program.Compile(allowed.Replace("! alloc,mut", "!", StringComparison.Ordinal));
        Assert.True(denied.HasErrors);
        Assert.Contains(denied.Diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    private static void AssertContractMessageTransition(ModuleNode module)
    {
        var divide = Assert.Single(module.Functions, function => function.Name == "Divide");
        var precondition = Assert.Single(divide.Preconditions);
        Assert.Null(precondition.Message);
        var condition = Assert.IsType<BinaryOperationNode>(precondition.Condition);
        Assert.Equal(BinaryOperator.NotEqual, condition.Operator);
        Assert.Equal("b", Assert.IsType<ReferenceNode>(condition.Left).Name);
        Assert.Equal(0, Assert.IsType<IntLiteralNode>(condition.Right).Value);

        const string original = """
            public static class OriginalContract
            {
                public static int Divide(int a, int b)
                {
                    if (!(b != 0)) throw new Calor.Runtime.ContractViolationException(
                        "message= divisor must not be zero \"\"", "f_auto4",
                        Calor.Runtime.ContractKind.Requires, startOffset: 663, length: 50,
                        sourceFile: null, line: 21, column: 5, condition: "b != 0");
                    return a / b;
                }
            }
            """;
        var historical = LoadMethod(original, "Divide");
        var current = LoadMethod(new CSharpEmitter().Emit(module), "Divide");
        Assert.Equal(5, historical.Invoke(null, [10, 2]));
        Assert.Equal(5, current.Invoke(null, [10, 2]));
        var oldFailure = Assert.IsType<Calor.Runtime.ContractViolationException>(
            Assert.Throws<TargetInvocationException>(() => historical.Invoke(null, [10, 0])).InnerException);
        var newFailure = Assert.IsType<Calor.Runtime.ContractViolationException>(
            Assert.Throws<TargetInvocationException>(() => current.Invoke(null, [10, 0])).InnerException);
        Assert.Equal("message= divisor must not be zero \"\"", oldFailure.Message);
        Assert.Equal("Precondition failed: b != 0", newFailure.Message);
        Assert.Equal(Calor.Runtime.ContractKind.Requires, oldFailure.Kind);
        Assert.Equal(oldFailure.Kind, newFailure.Kind);
        Assert.Equal("b != 0", oldFailure.Condition);
        Assert.Equal(oldFailure.Condition, newFailure.Condition);
    }

    private static void AssertCheckedBoundary(string source, ModuleNode module)
    {
        const string originalExpression = """
            public static class OriginalChecked
            {
                public static int SafeCheckedAddition(int a, int b)
                {
                    int result = checked(a + b);
                    return result;
                }
            }
            """;
        const string assignment = """
            public static class CheckedAssignment
            {
                public static int SafeCheckedAddition(int a, int b)
                {
                    int result = 0;
                    checked { result = a + b; }
                    return result;
                }
            }
            """;
        var converted = new CSharpToCalorConverter().Convert(assignment);
        Assert.True(converted.Success, string.Join("\n", converted.Issues));
        var expectedFrame = Assert.IsType<RawCSharpFrame>(
            Assert.Single(RawCSharpFrame.ReadTokens(converted.CalorSource!),
                token => token.Value is RawCSharpFrame).Value);
        var actualFrame = Assert.IsType<RawCSharpFrame>(
            Assert.Single(RawCSharpFrame.ReadTokens(source),
                token => token.Value is RawCSharpFrame).Value);
        Assert.Equal(expectedFrame, actualFrame);
        Assert.Equal("checked-block", actualFrame.FeatureName);
        Assert.Equal("checked overflow block preserved verbatim", actualFrame.Reason);
        Assert.Equal("        checked { result = a + b; }\n", actualFrame.Code);
        foreach (var code in new[] { originalExpression, assignment, new CSharpEmitter().Emit(module) })
        {
            var method = LoadMethod(code, "SafeCheckedAddition");
            Assert.Equal(5, method.Invoke(null, [2, 3]));
            Assert.Equal(int.MaxValue, method.Invoke(null, [int.MaxValue, 0]));
            Assert.IsType<OverflowException>(
                Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [int.MaxValue, 1])).InnerException);
            Assert.IsType<OverflowException>(
                Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [int.MinValue, -1])).InnerException);
        }
    }

    private static MethodInfo LoadMethod(string source, string name)
        => Assert.Single(LoadAssembly(source).GetTypes().SelectMany(type =>
            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)),
            method => method.Name == name);

    private static Assembly LoadAssembly(string source)
    {
        var compilation = CSharpCompilation.Create("BoundTypeOracle_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static IReadOnlyList<T> BindAndCollect<T>(string source)
        where T : BoundNode
        => Descendants(Bind(Parse(source))).OfType<T>().ToArray();

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static BoundModule Bind(ModuleNode module)
    {
        var diagnostics = new DiagnosticBag();
        var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(module);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return bound;
    }

    private static void AssertNativeLambdaRoundTrip(string source, string display)
    {
        var emitted = new CalorEmitter().Emit(Parse(source));
        Assert.DoesNotContain('§', emitted);
        var lambda = Assert.Single(BindAndCollect<BoundLambdaExpression>(emitted));
        Assert.Equal(display, Assert.IsType<FunctionBoundType>(lambda.Type).DisplayString);
        foreach (var candidate in new[] { source, emitted })
        {
            var compiled = Program.Compile(candidate);
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        }
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
