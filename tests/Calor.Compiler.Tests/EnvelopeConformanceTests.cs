using System.Reflection;
using System.Text.Json;
using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Calor.Compiler.Verification.Z3.Cache;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Envelope schema v1.1 conformance (loop plan D1.4) and choke-point bypass
/// checks (D1.2). Drift in the serialized document shape or a verification
/// status minted outside <c>ProofOutcome.Assign</c> is a build failure.
/// </summary>
/// <summary>
/// Schema validator shared by the conformance tests; per-command tests reuse it
/// as surfaces adopt the envelope (D1.3).
/// </summary>
public static class EnvelopeSchemaValidator
{
    internal static readonly string[] Severities = ["error", "warning", "info"];
    internal static readonly string[] ProofStatusNames = ["proven", "refuted", "unknown", "timeout", "unsupported"];

    /// <summary>Validates a top-level envelope document (schema v1.1).</summary>
    public static void ValidateEnvelopeDocument(JsonElement root)
    {
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(JsonDiagnosticFormatter.SchemaVersion, root.GetProperty("version").GetString());

        var diagnostics = root.GetProperty("diagnostics");
        Assert.Equal(JsonValueKind.Array, diagnostics.ValueKind);
        foreach (var entry in diagnostics.EnumerateArray())
            ValidateDiagnosticEntry(entry);

        var summary = root.GetProperty("summary");
        var total = summary.GetProperty("total").GetInt32();
        var errors = summary.GetProperty("errors").GetInt32();
        var warnings = summary.GetProperty("warnings").GetInt32();
        var info = summary.GetProperty("info").GetInt32();
        Assert.Equal(diagnostics.GetArrayLength(), total);
        Assert.Equal(total, errors + warnings + info);

        if (root.TryGetProperty("command", out var command))
            Assert.Equal(JsonValueKind.String, command.ValueKind);
    }

    /// <summary>Validates one diagnostic entry (EnvelopeDiagnostic shape).</summary>
    public static void ValidateDiagnosticEntry(JsonElement entry)
    {
        Assert.Equal(JsonValueKind.Object, entry.ValueKind);
        Assert.False(string.IsNullOrEmpty(entry.GetProperty("code").GetString()));
        Assert.Equal(JsonValueKind.String, entry.GetProperty("message").ValueKind);
        Assert.Contains(entry.GetProperty("severity").GetString(), Severities);

        var location = entry.GetProperty("location");
        Assert.True(location.GetProperty("line").GetInt32() >= 0);
        Assert.True(location.GetProperty("column").GetInt32() >= 0);
        Assert.True(location.GetProperty("length").GetInt32() >= 0);
        if (location.TryGetProperty("file", out var file))
            Assert.Equal(JsonValueKind.String, file.ValueKind);

        // Null fields are omitted, never serialized as null
        foreach (var property in entry.EnumerateObject())
            Assert.NotEqual(JsonValueKind.Null, property.Value.ValueKind);

        if (entry.TryGetProperty("declarationId", out var declarationId))
            Assert.False(string.IsNullOrEmpty(declarationId.GetString()));

        if (entry.TryGetProperty("verification", out var verification))
        {
            Assert.Contains(verification.GetProperty("status").GetString(), ProofStatusNames);
            if (verification.TryGetProperty("counterexample", out var counterexample))
            {
                Assert.Equal(JsonValueKind.String, counterexample.GetProperty("rendered").ValueKind);
                foreach (var binding in counterexample.GetProperty("bindings").EnumerateArray())
                {
                    Assert.Equal(JsonValueKind.String, binding.GetProperty("name").ValueKind);
                    Assert.Equal(JsonValueKind.String, binding.GetProperty("value").ValueKind);
                }
            }
        }

        if (entry.TryGetProperty("fix", out var fix))
        {
            Assert.Equal(JsonValueKind.String, fix.GetProperty("description").ValueKind);
            foreach (var edit in fix.GetProperty("edits").EnumerateArray())
            {
                Assert.Equal(JsonValueKind.String, edit.GetProperty("filePath").ValueKind);
                Assert.Equal(JsonValueKind.String, edit.GetProperty("newText").ValueKind);
                Assert.True(edit.GetProperty("startLine").GetInt32() >= 0);
            }
        }
    }
}

public class EnvelopeConformanceTests
{
    private static string RepoRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static void ValidateEnvelopeDocument(JsonElement root)
        => EnvelopeSchemaValidator.ValidateEnvelopeDocument(root);

    // ------------------------------------------------------------------
    // Document-level conformance
    // ------------------------------------------------------------------

    [Fact]
    public void CompileErrors_ProduceConformantDocument_WithFix()
    {
        // §RET is a lexer-level mistake with an attached machine fix
        var result = Program.Compile("§M{m1:Demo}\n  §F{f1:F:pub} () -> void\n    §RET\n", "demo.calr");
        Assert.True(result.HasErrors);

        var json = new JsonDiagnosticFormatter().Format(result.Diagnostics);
        using var doc = JsonDocument.Parse(json);
        ValidateEnvelopeDocument(doc.RootElement);
    }

    [SkippableFact]
    public void ContractDiagnostics_ProduceConformantDocument_WithDeclarationIdAndVerification()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = "§M{m1:Demo}\n  §F{f1:Dec:pub} (i32:x) -> i32\n    §Q (> x 0)\n    §S (> result 10)\n    §R (- x 1)\n";
        var options = new CompilationOptions
        {
            VerifyContracts = true,
            StatusWriter = TextWriter.Null,
            VerificationCacheOptions = new VerificationCacheOptions { Enabled = false }
        };
        var result = Program.Compile(source, "demo.calr", options);
        Assert.NotNull(result.Ast);

        var resolver = new DeclarationIdResolver();
        resolver.AddFile("demo.calr", source, result.Ast!);

        var formatter = new JsonDiagnosticFormatter { DeclarationIds = resolver };
        using var doc = JsonDocument.Parse(formatter.Format(result.Diagnostics));
        ValidateEnvelopeDocument(doc.RootElement);

        var refuted = doc.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Single(d => d.GetProperty("code").GetString() == DiagnosticCode.PostconditionMayBeViolated);
        Assert.Equal("f1", refuted.GetProperty("declarationId").GetString());
        Assert.Equal("refuted", refuted.GetProperty("verification").GetProperty("status").GetString());
        Assert.True(refuted.GetProperty("verification").GetProperty("counterexample")
            .GetProperty("bindings").GetArrayLength() > 0);
    }

    [Fact]
    public void EmptyBag_ProducesConformantDocument()
    {
        var json = new JsonDiagnosticFormatter().Format(new DiagnosticBag());
        using var doc = JsonDocument.Parse(json);
        ValidateEnvelopeDocument(doc.RootElement);
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("total").GetInt32());
    }

    // ------------------------------------------------------------------
    // Choke-point bypass checks (D1.2)
    // ------------------------------------------------------------------

    [Fact]
    public void ProofOutcome_HasNoPublicConstructor()
    {
        var constructors = typeof(ProofOutcome).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(constructors);
    }

    [Fact]
    public void ProofOutcome_IsConstructedOnlyInsideChokePointFile()
    {
        var srcDir = Path.Combine(RepoRoot(), "src");
        Assert.True(Directory.Exists(srcDir), $"src directory not found at {srcDir}");

        var offenders = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(Path.Combine("Verification", "ProofOutcome.cs"), StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("new ProofOutcome"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "ProofOutcome must only be constructed inside its choke point (ProofOutcome.cs). Offenders: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void ProofStatus_VocabularyIsClosedAtFive()
    {
        var values = Enum.GetValues<ProofStatus>();
        Assert.Equal(5, values.Length);

        var wireNames = values
            .Select(v => ProofOutcome.Rehydrate(v.ToString().ToLowerInvariant(), null, null).StatusName)
            .Distinct()
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(EnvelopeSchemaValidator.ProofStatusNames.OrderBy(n => n).ToArray(), wireNames);
    }
}
