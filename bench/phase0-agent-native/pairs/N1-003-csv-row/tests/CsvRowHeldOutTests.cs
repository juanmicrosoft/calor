// Arm-shared held-out tests (never shown to the agent under test).
// Binds to the pinned public surface through the harness-provided TestShim
// (per-arm, fixed, not agent-editable): NeedsQuoting / EscapeField /
// JoinRow / SplitRow.
using Xunit;

namespace CsvRow.HeldOut;

public sealed class CsvRowHeldOutTests
{
    // --- NeedsQuoting (existing behavior must be preserved) ---

    [Fact]
    public void NeedsQuoting_PlainField_False()
    {
        Assert.False(TestShim.NeedsQuoting("plain text"));
        Assert.False(TestShim.NeedsQuoting(""));
    }

    [Fact]
    public void NeedsQuoting_SpecialCharacters_True()
    {
        Assert.True(TestShim.NeedsQuoting("a,b"));
        Assert.True(TestShim.NeedsQuoting("say \"hi\""));
        Assert.True(TestShim.NeedsQuoting("line\nbreak"));
        Assert.True(TestShim.NeedsQuoting("cr\rhere"));
    }

    // --- EscapeField ---

    [Fact]
    public void EscapeField_PlainField_Unchanged()
    {
        Assert.Equal("hello", TestShim.EscapeField("hello"));
    }

    [Fact]
    public void EscapeField_EmptyField_StaysEmpty()
    {
        Assert.Equal("", TestShim.EscapeField(""));
    }

    [Fact]
    public void EscapeField_Comma_GetsQuoted()
    {
        Assert.Equal("\"a,b\"", TestShim.EscapeField("a,b"));
    }

    [Fact]
    public void EscapeField_InternalQuotesAreDoubled()
    {
        Assert.Equal("\"say \"\"hi\"\"\"", TestShim.EscapeField("say \"hi\""));
    }

    [Fact]
    public void EscapeField_FieldThatIsOnlyAQuote()
    {
        // One quote -> wrapped and doubled: "" inside quotes.
        Assert.Equal("\"\"\"\"", TestShim.EscapeField("\""));
    }

    [Fact]
    public void EscapeField_Newline_GetsQuoted()
    {
        Assert.Equal("\"a\nb\"", TestShim.EscapeField("a\nb"));
    }

    // --- JoinRow ---

    [Fact]
    public void JoinRow_EmptyList_EmptyString()
    {
        Assert.Equal("", TestShim.JoinRow(new List<string>()));
    }

    [Fact]
    public void JoinRow_SingleField_NoSeparator()
    {
        Assert.Equal("a", TestShim.JoinRow(new List<string> { "a" }));
    }

    [Fact]
    public void JoinRow_EscapesOnlyWhatNeedsIt()
    {
        Assert.Equal("\"a,b\",c", TestShim.JoinRow(new List<string> { "a,b", "c" }));
    }

    [Fact]
    public void JoinRow_AllEmptyFields_JustCommas()
    {
        Assert.Equal(",,", TestShim.JoinRow(new List<string> { "", "", "" }));
    }

    // --- SplitRow ---

    [Fact]
    public void SplitRow_SimpleFields()
    {
        Assert.Equal(new List<string> { "a", "b", "c" }, TestShim.SplitRow("a,b,c"));
    }

    [Fact]
    public void SplitRow_EmptyLine_OneEmptyField()
    {
        Assert.Equal(new List<string> { "" }, TestShim.SplitRow(""));
    }

    [Fact]
    public void SplitRow_TrailingComma_EmptyLastField()
    {
        Assert.Equal(new List<string> { "a", "" }, TestShim.SplitRow("a,"));
    }

    [Fact]
    public void SplitRow_ConsecutiveCommas_EmptyMiddleFields()
    {
        Assert.Equal(new List<string> { "", "", "" }, TestShim.SplitRow(",,"));
    }

    [Fact]
    public void SplitRow_QuotedFieldWithComma()
    {
        Assert.Equal(new List<string> { "a,b", "c" }, TestShim.SplitRow("\"a,b\",c"));
    }

    [Fact]
    public void SplitRow_EscapedQuotesBecomeLiterals()
    {
        Assert.Equal(new List<string> { "say \"hi\"" }, TestShim.SplitRow("\"say \"\"hi\"\"\""));
    }

    [Fact]
    public void SplitRow_QuotedEmptyField()
    {
        Assert.Equal(new List<string> { "", "x" }, TestShim.SplitRow("\"\",x"));
    }

    [Fact]
    public void RoundTrip_HostileFields()
    {
        var fields = new List<string> { "plain", "a,b", "say \"hi\"", "", "line\nbreak", "\"" };
        Assert.Equal(fields, TestShim.SplitRow(TestShim.JoinRow(fields)));
    }
}
