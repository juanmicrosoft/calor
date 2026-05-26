using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Compiler.Tests.Diagnostics;

/// <summary>
/// Tests for <see cref="DiagnosticAddress"/> — RFC v3+ §8.3 standalone
/// diagnostic addressing.
/// </summary>
public class DiagnosticAddressTests
{
    private const string UlidFullId = "f_01J5X7K9M2NPQRSTABWXYZ12AB";
    private const string CompactFullId = "f_abc123def456";

    [Fact]
    public void Format_WhenBothInputsBlank_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, DiagnosticAddress.Format(null, null));
        Assert.Equal(string.Empty, DiagnosticAddress.Format(string.Empty, string.Empty));
        Assert.Equal(string.Empty, DiagnosticAddress.Format("   ", "\t"));
    }

    [Fact]
    public void Format_WithOnlyName_ReturnsName()
    {
        Assert.Equal("Calculator.Divide",
            DiagnosticAddress.Format("Calculator.Divide", null));
    }

    [Fact]
    public void Format_WithOnlyUlidId_TruncatesAndWrapsInParens()
    {
        // Payload is exactly 26 chars → truncate to 7 + ellipsis.
        var s = DiagnosticAddress.Format(null, UlidFullId);
        Assert.Equal("(f_01J5X7K\u2026)", s);
    }

    [Fact]
    public void Format_WithOnlyCompactId_DoesNotTruncate()
    {
        // 12-char payload → no truncation.
        var s = DiagnosticAddress.Format(null, CompactFullId);
        Assert.Equal($"({CompactFullId})", s);
    }

    [Fact]
    public void Format_WithNameAndUlidId_ProducesRfcExampleShape()
    {
        // The exact shape called out by RFC §8.3.
        var s = DiagnosticAddress.Format("Calculator.Divide", UlidFullId);
        Assert.Equal("Calculator.Divide (f_01J5X7K\u2026)", s);
    }

    [Fact]
    public void Format_WithNameAndCompactId_ShowsFullCompactId()
    {
        var s = DiagnosticAddress.Format("Calculator.Divide", CompactFullId);
        Assert.Equal($"Calculator.Divide ({CompactFullId})", s);
    }

    [Theory]
    [InlineData("f_01J5X7K9M2NPQRSTABWXYZ12AB", "f_01J5X7K\u2026")]
    [InlineData("m_01J5X7K9M2NPQRSTABWXYZ12AB", "m_01J5X7K\u2026")]
    [InlineData("ctor_01J5X7K9M2NPQRSTABWXYZ12AB", "ctor_01J5X7K\u2026")]
    [InlineData("op_01J5X7K9M2NPQRSTABWXYZ12AB", "op_01J5X7K\u2026")]
    public void TruncateForDisplay_UlidPayload_TruncatesToSevenCharsPlusEllipsis(
        string input, string expected)
    {
        Assert.Equal(expected, DiagnosticAddress.TruncateForDisplay(input));
    }

    [Theory]
    [InlineData("f_abc123def456")] // 12-char compact payload
    [InlineData("c_xyzpqr987abc")] // 12-char compact payload
    [InlineData("f_short")]        // Short non-ULID payload
    [InlineData("noprefix")]       // No underscore at all
    public void TruncateForDisplay_NonUlid_PassesThroughUnchanged(string input)
    {
        Assert.Equal(input, DiagnosticAddress.TruncateForDisplay(input));
    }

    [Fact]
    public void TruncateForDisplay_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DiagnosticAddress.TruncateForDisplay(string.Empty));
    }

    [Fact]
    public void TruncateForDisplay_TrailingUnderscore_TreatedAsNoPrefix()
    {
        // "_" at end is not a valid prefix delimiter; the whole thing is
        // treated as the payload (and shorter than 26 chars, so verbatim).
        Assert.Equal("weird_", DiagnosticAddress.TruncateForDisplay("weird_"));
    }
}
