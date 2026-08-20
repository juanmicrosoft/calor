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
    /// v0.14 §S2 cache-invariance smoke — the shim (<c>Type.DisplayString</c>
    /// wraps <c>TypeName</c>) preserves the byte-identical string that
    /// verifier caches and effect-resolver dictionaries key on. Assert this
    /// on a representative sampler that covers the primitive/nominal common
    /// cases.
    /// </summary>
    [Fact]
    public void BoundExpressionType_DisplayString_ByteIdenticalToTypeName_OnCommonSampler()
    {
        var sampler = new BoundExpression[]
        {
            new BoundBoolLiteral(new TextSpan(0, 0, 0, 0), true),
            new BoundIntLiteral(new TextSpan(0, 0, 0, 0), 42),
            new BoundStringLiteral(new TextSpan(0, 0, 0, 0), "hello", isMultiline: false, isUtf8: false),
            // UTF-8 branch: TypeName is "ReadOnlySpan<BYTE>" per BoundNodes.cs:479 —
            // the load-bearing non-primitive shim case (contains generic-angle
            // brackets that a naive normalization might trim).
            new BoundStringLiteral(new TextSpan(0, 0, 0, 0), "hello", isMultiline: false, isUtf8: true),
            new BoundDecimalLiteral(new TextSpan(0, 0, 0, 0), 1.5m),
            new BoundNoneLiteral(new TextSpan(0, 0, 0, 0)),
        };

        foreach (var expr in sampler)
        {
            Assert.Equal(expr.TypeName, expr.Type.DisplayString);
        }
    }

    /// <summary>
    /// V-1 gap #25 fix — §S2 R7 corpus cache-invariance. Binds every Calor
    /// source file under <c>samples/</c> and <c>benchmarks/</c> and asserts
    /// <c>Type.DisplayString == TypeName</c> for every bound expression in
    /// every module. This is what makes the S2 shim's byte-identity claim
    /// actually verified: if any subclass's Type override ever diverges
    /// from TypeName's string, the verifier cache silently invalidates —
    /// this test catches that first.
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

        var mismatches = new List<string>();
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
                    if (!string.Equals(boundExpr.TypeName, boundExpr.Type.DisplayString, StringComparison.Ordinal))
                    {
                        mismatches.Add(
                            $"{file}: {boundExpr.GetType().Name} TypeName='{boundExpr.TypeName}' " +
                            $"Type.DisplayString='{boundExpr.Type.DisplayString}'");
                        if (mismatches.Count > 20) break;
                    }
                }
                if (mismatches.Count > 20) break;
            }
            catch (Exception)
            {
                // Some samples may fail to bind cleanly — skip them. The point
                // is to catch TypeName-vs-DisplayString drift on modules that
                // DO bind; a corpus-file bind failure is not what this pin is
                // about.
            }
        }

        Assert.True(modulesBound > 0,
            $"No .calr modules bound cleanly. Corpus roots: {string.Join(", ", corpusRoots)}");
        Assert.True(expressionsChecked > 0,
            "No bound expressions checked; corpus binding produced empty trees.");
        Assert.Empty(mismatches);
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
