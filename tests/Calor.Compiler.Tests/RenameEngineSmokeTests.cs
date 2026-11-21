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
            module Alpha
              func Compute () → int
                !
                return 42
              func Use () → int
                !
                return Compute()
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
            module Shadow
              func First () → int
                !
                let int:value = 1
                return value
              func Second () → int
                !
                let int:value = 2
                return value
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
            module Collide
              func First () → int
                !
                return 1
              func Second () → int
                !
                return 2
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
    public void RenamesFileLabelIndependentlyOfAnotherFilesIdenticalLabel()
    {
        var a = Write("part-a.calr", """
            module Split
              func One () → int
                !
                return 1
            """);
        var b = Write("part-b.calr", """
            module Split
              func Two () → int
                !
                return 2
            """);

        var index = ProjectSymbolIndex.Build([a, b], out _);
        var source = File.ReadAllText(a);
        var occurrence = index.Resolve(a, source.IndexOf("Split", StringComparison.Ordinal));
        Assert.NotNull(occurrence);

        var result = RenameEngine.Rename(index, occurrence.SymbolId, "Renamed");
        Assert.Equal(RenameRefusal.None, result.Refusal);
        var edit = Assert.Single(result.Edits);
        Assert.Equal(a, edit.FilePath);
        Assert.Equal("Split", source.Substring(edit.Span.Start, edit.Span.Length));
        Assert.Equal("Renamed", edit.NewText);
        Assert.Equal(source, File.ReadAllText(a));
        Assert.StartsWith("module Split", File.ReadAllText(b));
    }

    [Theory]
    [InlineData(
        "module First\n  class Worker partial\n    field int:Left",
        "module Second\n  class Worker partial\n    field int:Right", true)]
    [InlineData(
        "module First Shared\n  class Worker partial\n    field int:Left",
        "module Second Shared\n  class Worker partial\n    field int:Right", true)]
    [InlineData(
        "module Same Left\n  class Worker partial\n    field int:Left",
        "module Same Right\n  class Worker partial\n    field int:Right", false)]
    [InlineData(
        "module Same\n  class Worker<T> partial\n    field int:Left",
        "module Same\n  class Worker<T,U> partial\n    field int:Right", false)]
    [InlineData(
        "module Same\n  class OwnerA\n    class Worker partial\n      field int:Left",
        "module Same\n  class OwnerB\n    class Worker partial\n      field int:Right", false)]
    [InlineData(
        "module Same\n  class Owner<T>\n    class Worker partial\n      field int:Left",
        "module Same\n  class Owner<T,U>\n    class Worker partial\n      field int:Right", false)]
    public void TypeSplitGuardUsesActualQualifiedIdentity(string first, string second, bool splitType)
    {
        var a = Write("first.calr", first);
        var b = Write("second.calr", second);
        var index = ProjectSymbolIndex.Build([a, b], out var skipped);
        Assert.Empty(skipped);
        var occurrence = index.Resolve(a, first.IndexOf("Worker", StringComparison.Ordinal));
        Assert.NotNull(occurrence);
        Assert.True(occurrence.IsTypeDeclaration);
        Assert.Equal(splitType, occurrence.IsSplitDeclaration);
        var result = RenameEngine.Rename(index, occurrence.SymbolId, "Renamed");
        Assert.Equal(splitType ? RenameRefusal.SplitDeclaration : RenameRefusal.TypeReferencesNotIndexed, result.Refusal);
        Assert.Empty(result.Edits);
        Assert.Equal(first, File.ReadAllText(a));
        Assert.Equal(second, File.ReadAllText(b));
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
