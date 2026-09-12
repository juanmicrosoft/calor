using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

public class N5CorpusAudit
{
    [Fact]
    public void Capture()
    {
        Assert.Equal(Environment.GetEnvironmentVariable("N5_EXPECT_MEMBER_API") == "1",
            typeof(BoundFieldAccessExpression).GetProperty(
                "ResolvedMetadataMember", BindingFlags.NonPublic | BindingFlags.Instance) is not null);
        var root = Environment.GetEnvironmentVariable("N5_REPO")!;
        var scratch = Environment.GetEnvironmentVariable("N5_SCRATCH")!;
        var baseline = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(Path.Combine(scratch, "n5-base-coverage.json")))!;
        var candidate = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(Path.Combine(root, "bench/phase0-agent-native/binder-source-coverage.json")))!;
        Assert.Equal(baseline.Keys, candidate.Keys);
        var changed = baseline.Keys.Where(k => baseline[k].GetRawText() != candidate[k].GetRawText()).ToArray();
        var results = new List<object>();
        foreach (var key in changed)
        {
            var file = Path.Combine(root, "bench/corpus", key);
            var source = File.ReadAllText(file).Replace("\r\n", "\n");
            var conversion = new CSharpToCalorConverter(new ConversionOptions
            {
                Fidelity = ConversionFidelity.Lossy,
                PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
                ParseOptions = new CSharpParseOptions(
                    LanguageVersion.Preview, Microsoft.CodeAnalysis.DocumentationMode.Parse,
                    Microsoft.CodeAnalysis.SourceCodeKind.Regular, preprocessorSymbols: []),
                DefinedSymbols = [],
                ModuleName = "Leg2",
                GracefulFallback = true,
                AutoGenerateIds = true
            }).Convert(source, Path.GetFileName(file));
            var bag = new DiagnosticBag();
            Assert.NotNull(conversion.CalorSource);
            var text = conversion.CalorSource.Replace("\r\n", "\n");
            var ast = new Parser(new Lexer(text, bag).TokenizeAllForParser(), bag).Parse();
            Assert.False(bag.HasErrors);
            var diagnostics = new DiagnosticBag();
            var bound = new Binder(diagnostics).Bind(ast);
            var effectDiagnostics = new DiagnosticBag();
            if (key == "FluentValidation/src/FluentValidation/Results/ValidationResult.cs")
                new EffectEnforcementPass(effectDiagnostics, resolver: Calor0425CorpusLedgerTests.HermeticResolver()).Enforce(ast);
            var members = Nodes(bound).OfType<BoundFieldAccessExpression>().Select(m =>
            {
                var symbol = typeof(BoundFieldAccessExpression).GetProperty(
                    "ResolvedMetadataMember", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(m)
                    as Microsoft.CodeAnalysis.ISymbol;
                return new
                {
                    m.Span.Start, m.Span.Length, m.Span.Line, m.Span.Column,
                    Expression = text.Substring(m.Span.Start, m.Span.Length),
                    Kind = m.Type.GetType().Name,
                    Type = m.Type.DisplayString,
                    Annotation = (m.Type as NominalBoundType)?.NullableAnnotation.ToString(),
                    Member = symbol?.ToDisplayString(),
                    Assembly = symbol?.ContainingAssembly.Identity.ToString()
                };
            }).ToArray();
            results.Add(new
            {
                File = key,
                ConversionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),
                Diagnostics = diagnostics.Select(d => new
                {
                    d.Code, Severity = d.Severity.ToString(), d.Message,
                    d.Span.Start, d.Span.Length, d.Span.Line, d.Span.Column,
                    Expression = d.Span.Start + d.Span.Length <= text.Length
                        ? text.Substring(d.Span.Start, d.Span.Length) : "<outside source>",
                    Propagated = BindingDiagnosticPolicy.IsCompilationError(d)
                }).ToArray(),
                EffectDiagnostics = effectDiagnostics.Select(d => new { d.Code, d.Message, d.Span.Line, d.Span.Column }).ToArray(),
                Members = members
            });
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("N5_OUTPUT")!,
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<BoundNode> Nodes(BoundNode root)
    {
        var pending = new Stack<BoundNode>();
        var seen = new HashSet<BoundNode>(ReferenceEqualityComparer.Instance);
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            yield return node;
            foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0) continue;
                var value = property.GetValue(node);
                if (value is BoundNode child) pending.Push(child);
                else if (value is IEnumerable items and not string)
                    foreach (var item in items)
                        if (item is BoundNode nested) pending.Push(nested);
            }
        }
    }
}
