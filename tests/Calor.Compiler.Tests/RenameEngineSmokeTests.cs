using Calor.Compiler.Refactoring;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class RenameEngineSmokeTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "rn-" + Guid.NewGuid().ToString("N")[..8]);

    public RenameEngineSmokeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void RenamesFunctionAcrossFiles()
    {
        var a = Write("a.calr", """
            §M{m001:Alpha}
              §F{f001:Compute:pub} () -> i32
                §E{}
                §R INT:42
              §F{f002:Use:pub} () -> i32
                §E{}
                §R §C{Compute} §/C
            """);

        var index = ProjectSymbolIndex.Build([a], out var skipped);
        Assert.Empty(skipped);

        var source = File.ReadAllText(a);
        var offset = source.IndexOf("Compute", StringComparison.Ordinal);
        var occurrence = index.Resolve(a, offset);
        Assert.NotNull(occurrence);

        var result = RenameEngine.Rename(index, occurrence.SymbolId, "Calculate");
        Assert.Equal(RenameRefusal.None, result.Refusal);
        Assert.Equal("Compute", result.OldName);

        var updated = RenameEngine.Apply(
            new Dictionary<string, string>(StringComparer.Ordinal) { [a] = source },
            result.Edits);
        Assert.DoesNotContain("Compute", updated[a]);
        Assert.Equal(2, CountOccurrences(updated[a], "Calculate"));
    }

    [Fact]
    public void RenamesLocalWithoutTouchingSameNamedLocalInAnotherFunction()
    {
        var a = Write("shadow.calr", """
            §M{m001:Shadow}
              §F{f001:First:pub} () -> i32
                §E{}
                §B{value:i32} INT:1
                §R value
              §F{f002:Second:pub} () -> i32
                §E{}
                §B{value:i32} INT:2
                §R value
            """);

        var index = ProjectSymbolIndex.Build([a], out _);
        var source = File.ReadAllText(a);
        var firstValue = source.IndexOf("value", StringComparison.Ordinal);
        var occurrence = index.Resolve(a, firstValue);
        Assert.NotNull(occurrence);

        var result = RenameEngine.Rename(index, occurrence.SymbolId, "amount");
        Assert.Equal(RenameRefusal.None, result.Refusal);

        var updated = RenameEngine.Apply(
            new Dictionary<string, string>(StringComparer.Ordinal) { [a] = source },
            result.Edits);

        // Only the first function's local moved; the second function keeps its
        // own `value`, which is a different symbol with the same name.
        Assert.Equal(2, CountOccurrences(updated[a], "amount"));
        Assert.Equal(2, CountOccurrences(updated[a], "value"));
    }

    [Fact]
    public void RefusesRenameThatWouldCollideWithAnExistingName()
    {
        var a = Write("collide.calr", """
            §M{m001:Collide}
              §F{f001:First:pub} () -> i32
                §E{}
                §R INT:1
              §F{f002:Second:pub} () -> i32
                §E{}
                §R INT:2
            """);

        var index = ProjectSymbolIndex.Build([a], out _);
        var source = File.ReadAllText(a);
        var occurrence = index.Resolve(a, source.IndexOf("First", StringComparison.Ordinal));
        Assert.NotNull(occurrence);

        var result = RenameEngine.Rename(index, occurrence.SymbolId, "Second");
        Assert.Equal(RenameRefusal.NameCollision, result.Refusal);
        Assert.Empty(result.Edits);
    }

    [Fact]
    public void RefusesModuleDeclaredInSeveralFiles()
    {
        var a = Write("part-a.calr", """
            §M{m001:Split}
              §F{f001:One:pub} () -> i32
                §E{}
                §R INT:1
            """);
        var b = Write("part-b.calr", """
            §M{m002:Split}
              §F{f001:Two:pub} () -> i32
                §E{}
                §R INT:2
            """);

        var index = ProjectSymbolIndex.Build([a, b], out _);
        var source = File.ReadAllText(a);
        var occurrence = index.Resolve(a, source.IndexOf("Split", StringComparison.Ordinal));
        Assert.NotNull(occurrence);

        var result = RenameEngine.Rename(index, occurrence.SymbolId, "Renamed");
        Assert.Equal(RenameRefusal.SplitDeclaration, result.Refusal);
        Assert.Empty(result.Edits);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0;
             (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0;
             index += value.Length)
        {
            count++;
        }
        return count;
    }
}
