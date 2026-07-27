using Xunit;

namespace SessionPair.Smoke;

/// <summary>
/// Arm-shared, agent-visible smoke suite — identical in both arms and
/// restricted to the STARTING surface. LookupSession is exercised on its
/// entry value only; the suite deliberately does not assert on what
/// files exist afterwards — whether each arm's toolchain surfaces the
/// hidden write is exactly the probe's question.
/// </summary>
public class SessionSmokeTests
{
    private static string TempIndex(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5c-smoke-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void IndexLine_Shape()
        => Assert.Equal("s1=4", SmokeShim.IndexLine("s1", 4));

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5c-smoke-{Guid.NewGuid():N}.txt");
        try
        {
            SmokeShim.WriteIndex(path, "s9", 7);
            Assert.Equal("s9=7", SmokeShim.ReadIndex(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadIndex_AbsentIsEmpty()
        => Assert.Equal("", SmokeShim.ReadIndex(
            Path.Combine(Path.GetTempPath(), $"w5c-none-{Guid.NewGuid():N}.txt")));

    [Fact]
    public void MarkActive_RecordsSession()
    {
        try
        {
            SmokeShim.MarkActive("s3");
            Assert.Equal("s3=1", SmokeShim.ReadIndex("session-active.txt"));
        }
        finally
        {
            File.Delete("session-active.txt");
        }
    }

    [Fact]
    public void LookupSession_ReportsContentLength()
    {
        var path = TempIndex("s1=4");
        try
        {
            Assert.Equal("alpha#4", SmokeShim.LookupSession(path, "alpha"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HasIndex_TrueForNonEmpty()
    {
        var path = TempIndex("s1=4");
        try
        {
            Assert.True(SmokeShim.HasIndex(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
