using System.Text.Json;
using Opal.Compiler.Diagnostics;
using Opal.Compiler.Parsing;
using Xunit;

namespace Opal.Compiler.Tests;

public class DiagnosticFormatterTests
{
    private static Diagnostic CreateDiagnostic(
        string code = "OPAL0001",
        string message = "Test message",
        int line = 1,
        int column = 1,
        int length = 5,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        string? filePath = null)
    {
        var span = new TextSpan(0, length, line, column);
        return new Diagnostic(code, message, span, severity, filePath);
    }

    #region TextDiagnosticFormatter

    [Fact]
    public void TextFormatter_ProducesExpectedFormat()
    {
        var diagnostic = CreateDiagnostic(
            code: "OPAL0100",
            message: "Unexpected token",
            line: 10,
            column: 5,
            filePath: "test.opal");

        var formatter = new TextDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        Assert.Contains("test.opal(10,5)", result);
        Assert.Contains("error", result);
        Assert.Contains("OPAL0100", result);
        Assert.Contains("Unexpected token", result);
    }

    [Fact]
    public void TextFormatter_NoFilePath_UsesPositionOnly()
    {
        var diagnostic = CreateDiagnostic(
            code: "OPAL0100",
            message: "Test",
            line: 5,
            column: 10);

        var formatter = new TextDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        Assert.Contains("(5,10)", result);
    }

    [Fact]
    public void TextFormatter_MultipleDiagnostics_SeparatedByNewlines()
    {
        var diagnostics = new[]
        {
            CreateDiagnostic(code: "OPAL0001", message: "First"),
            CreateDiagnostic(code: "OPAL0002", message: "Second"),
        };

        var formatter = new TextDiagnosticFormatter();
        var result = formatter.Format(diagnostics);

        Assert.Contains("First", result);
        Assert.Contains("Second", result);
        Assert.Contains(Environment.NewLine, result);
    }

    [Fact]
    public void TextFormatter_ContentType_IsTextPlain()
    {
        var formatter = new TextDiagnosticFormatter();
        Assert.Equal("text/plain", formatter.ContentType);
    }

    #endregion

    #region JsonDiagnosticFormatter

    [Fact]
    public void JsonFormatter_ProducesValidJson()
    {
        var diagnostic = CreateDiagnostic();
        var formatter = new JsonDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        // Should parse without throwing
        var doc = JsonDocument.Parse(result);
        Assert.NotNull(doc);
    }

    [Fact]
    public void JsonFormatter_IncludesVersionField()
    {
        var diagnostic = CreateDiagnostic();
        var formatter = new JsonDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        var doc = JsonDocument.Parse(result);
        var version = doc.RootElement.GetProperty("version").GetString();
        Assert.Equal("1.0", version);
    }

    [Fact]
    public void JsonFormatter_IncludesDiagnosticsArray()
    {
        var diagnostics = new[]
        {
            CreateDiagnostic(code: "OPAL0001"),
            CreateDiagnostic(code: "OPAL0002"),
        };

        var formatter = new JsonDiagnosticFormatter();
        var result = formatter.Format(diagnostics);

        var doc = JsonDocument.Parse(result);
        var diags = doc.RootElement.GetProperty("diagnostics");
        Assert.Equal(JsonValueKind.Array, diags.ValueKind);
        Assert.Equal(2, diags.GetArrayLength());
    }

    [Fact]
    public void JsonFormatter_IncludesSummaryCounts()
    {
        var diagnostics = new[]
        {
            CreateDiagnostic(severity: DiagnosticSeverity.Error),
            CreateDiagnostic(severity: DiagnosticSeverity.Error),
            CreateDiagnostic(severity: DiagnosticSeverity.Warning),
            CreateDiagnostic(severity: DiagnosticSeverity.Info),
        };

        var formatter = new JsonDiagnosticFormatter();
        var result = formatter.Format(diagnostics);

        var doc = JsonDocument.Parse(result);
        var summary = doc.RootElement.GetProperty("summary");

        Assert.Equal(4, summary.GetProperty("total").GetInt32());
        Assert.Equal(2, summary.GetProperty("errors").GetInt32());
        Assert.Equal(1, summary.GetProperty("warnings").GetInt32());
        Assert.Equal(1, summary.GetProperty("info").GetInt32());
    }

    [Fact]
    public void JsonFormatter_LocationHasAllFields()
    {
        var diagnostic = CreateDiagnostic(
            line: 10,
            column: 5,
            length: 8,
            filePath: "test.opal");

        var formatter = new JsonDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        var doc = JsonDocument.Parse(result);
        var location = doc.RootElement
            .GetProperty("diagnostics")[0]
            .GetProperty("location");

        Assert.Equal("test.opal", location.GetProperty("file").GetString());
        Assert.Equal(10, location.GetProperty("line").GetInt32());
        Assert.Equal(5, location.GetProperty("column").GetInt32());
        Assert.Equal(8, location.GetProperty("length").GetInt32());
    }

    [Fact]
    public void JsonFormatter_UsesCamelCaseNaming()
    {
        var diagnostic = CreateDiagnostic();
        var formatter = new JsonDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        // Should use camelCase, not PascalCase
        Assert.Contains("\"diagnostics\"", result);
        Assert.Contains("\"version\"", result);
        Assert.Contains("\"summary\"", result);
        Assert.DoesNotContain("\"Diagnostics\"", result);
    }

    [Fact]
    public void JsonFormatter_ContentType_IsApplicationJson()
    {
        var formatter = new JsonDiagnosticFormatter();
        Assert.Equal("application/json", formatter.ContentType);
    }

    #endregion

    #region SarifDiagnosticFormatter

    [Fact]
    public void SarifFormatter_ProducesValidSarif()
    {
        var diagnostic = CreateDiagnostic();
        var formatter = new SarifDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        var doc = JsonDocument.Parse(result);
        Assert.NotNull(doc);

        // Check SARIF version
        var version = doc.RootElement.GetProperty("version").GetString();
        Assert.Equal("2.1.0", version);
    }

    [Fact]
    public void SarifFormatter_IncludesSchema()
    {
        var diagnostic = CreateDiagnostic();
        var formatter = new SarifDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        var doc = JsonDocument.Parse(result);
        var schema = doc.RootElement.GetProperty("$schema").GetString();
        Assert.Contains("sarif-schema-2.1.0.json", schema);
    }

    [Fact]
    public void SarifFormatter_IncludesToolInfo()
    {
        var diagnostic = CreateDiagnostic();
        var formatter = new SarifDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        var doc = JsonDocument.Parse(result);
        var driver = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("tool")
            .GetProperty("driver");

        Assert.Equal("opalc", driver.GetProperty("name").GetString());
        Assert.Equal("1.0.0", driver.GetProperty("version").GetString());
    }

    [Fact]
    public void SarifFormatter_MapsSeverityCorrectly_Error()
    {
        var diagnostic = CreateDiagnostic(severity: DiagnosticSeverity.Error);
        var formatter = new SarifDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        var doc = JsonDocument.Parse(result);
        var level = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("level").GetString();

        Assert.Equal("error", level);
    }

    [Fact]
    public void SarifFormatter_MapsSeverityCorrectly_Warning()
    {
        var diagnostic = CreateDiagnostic(severity: DiagnosticSeverity.Warning);
        var formatter = new SarifDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        var doc = JsonDocument.Parse(result);
        var level = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("level").GetString();

        Assert.Equal("warning", level);
    }

    [Fact]
    public void SarifFormatter_MapsSeverityCorrectly_Info()
    {
        var diagnostic = CreateDiagnostic(severity: DiagnosticSeverity.Info);
        var formatter = new SarifDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        var doc = JsonDocument.Parse(result);
        var level = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("level").GetString();

        Assert.Equal("note", level);
    }

    [Fact]
    public void SarifFormatter_IncludesLocationRegion()
    {
        var diagnostic = CreateDiagnostic(line: 15, column: 8, length: 10);
        var formatter = new SarifDiagnosticFormatter();
        var result = formatter.Format(new[] { diagnostic });

        var doc = JsonDocument.Parse(result);
        var region = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("region");

        Assert.Equal(15, region.GetProperty("startLine").GetInt32());
        Assert.Equal(8, region.GetProperty("startColumn").GetInt32());
        Assert.Equal(18, region.GetProperty("endColumn").GetInt32()); // 8 + 10
    }

    [Fact]
    public void SarifFormatter_IncludesRulesFromDiagnostics()
    {
        var diagnostics = new[]
        {
            CreateDiagnostic(code: DiagnosticCode.NonExhaustiveMatch),
            CreateDiagnostic(code: DiagnosticCode.UnreachablePattern),
        };

        var formatter = new SarifDiagnosticFormatter();
        var result = formatter.Format(diagnostics);

        var doc = JsonDocument.Parse(result);
        var rules = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("tool")
            .GetProperty("driver")
            .GetProperty("rules");

        Assert.Equal(2, rules.GetArrayLength());
    }

    [Fact]
    public void SarifFormatter_ContentType_IsSarifJson()
    {
        var formatter = new SarifDiagnosticFormatter();
        Assert.Equal("application/sarif+json", formatter.ContentType);
    }

    #endregion

    #region DiagnosticFormatterFactory

    [Fact]
    public void Factory_CreatesJsonFormatter()
    {
        var formatter = DiagnosticFormatterFactory.Create("json");
        Assert.IsType<JsonDiagnosticFormatter>(formatter);
    }

    [Fact]
    public void Factory_CreatesSarifFormatter()
    {
        var formatter = DiagnosticFormatterFactory.Create("sarif");
        Assert.IsType<SarifDiagnosticFormatter>(formatter);
    }

    [Fact]
    public void Factory_CreatesTextFormatterForText()
    {
        var formatter = DiagnosticFormatterFactory.Create("text");
        Assert.IsType<TextDiagnosticFormatter>(formatter);
    }

    [Fact]
    public void Factory_CreatesTextFormatterAsDefault()
    {
        var formatter = DiagnosticFormatterFactory.Create("unknown");
        Assert.IsType<TextDiagnosticFormatter>(formatter);
    }

    [Fact]
    public void Factory_IsCaseInsensitive()
    {
        var jsonFormatter = DiagnosticFormatterFactory.Create("JSON");
        var sarifFormatter = DiagnosticFormatterFactory.Create("SARIF");

        Assert.IsType<JsonDiagnosticFormatter>(jsonFormatter);
        Assert.IsType<SarifDiagnosticFormatter>(sarifFormatter);
    }

    #endregion

    #region Empty Diagnostics

    [Fact]
    public void TextFormatter_EmptyDiagnostics_ReturnsEmpty()
    {
        var formatter = new TextDiagnosticFormatter();
        var result = formatter.Format(Array.Empty<Diagnostic>());
        Assert.Empty(result);
    }

    [Fact]
    public void JsonFormatter_EmptyDiagnostics_ReturnsValidJson()
    {
        var formatter = new JsonDiagnosticFormatter();
        var result = formatter.Format(Array.Empty<Diagnostic>());

        var doc = JsonDocument.Parse(result);
        var diags = doc.RootElement.GetProperty("diagnostics");
        Assert.Equal(0, diags.GetArrayLength());

        var summary = doc.RootElement.GetProperty("summary");
        Assert.Equal(0, summary.GetProperty("total").GetInt32());
    }

    [Fact]
    public void SarifFormatter_EmptyDiagnostics_ReturnsValidSarif()
    {
        var formatter = new SarifDiagnosticFormatter();
        var result = formatter.Format(Array.Empty<Diagnostic>());

        var doc = JsonDocument.Parse(result);
        var results = doc.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results");

        Assert.Equal(0, results.GetArrayLength());
    }

    #endregion
}
