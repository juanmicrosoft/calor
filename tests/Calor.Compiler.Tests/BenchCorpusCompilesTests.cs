using Calor.Compiler;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Every agent-native benchmark PAIR program must compile clean.
///
/// <para>This exists because of a gap in the instrument, not a gap in the code. When
/// <c>EnableTypeChecking</c> was flipped default-on, the whole test suite went green — and two
/// benchmark <b>gold reference</b> programs had started failing to compile, one of them dropping
/// from 53 proven contracts to <b>zero</b> with a non-zero exit. Nothing caught it: no test
/// project compiles anything under <c>bench/</c>, so 781 shipped <c>.calr</c> files were outside
/// every gate while being the corpus the program's headline measurements run on.</para>
///
/// <para>Scoped to <c>pairs/**/{calor,reference/calor}/</c> deliberately. Those are the
/// hand-authored task and gold programs — definitionally correct, and the ones an epoch's numbers
/// depend on. The rest of <c>bench/</c> includes generated and deliberately-mutated fixtures that
/// are not expected to compile, so sweeping it wholesale would pin the wrong thing.</para>
/// </summary>
public class BenchCorpusCompilesTests
{
    public static TheoryData<string> PairPrograms()
    {
        var data = new TheoryData<string>();
        var root = FindRepoRoot();
        if (root == null)
        {
            return data;
        }

        var pairs = Path.Combine(root, "bench", "phase0-agent-native", "pairs");
        if (!Directory.Exists(pairs))
        {
            return data;
        }

        foreach (var file in Directory.EnumerateFiles(pairs, "*.calr", SearchOption.AllDirectories))
        {
            // `<pair>/reference/calor/…` ONLY — the GOLD programs.
            //
            // Not `<pair>/calor/…`: those are the task programs, and they are deliberately
            // defective. That is the whole point of the benchmark — `W5A-001-report-audit`'s
            // task, for instance, is missing an `fs:w` effect declaration, which is precisely
            // the bug the agent under test is supposed to find. Asserting they compile would
            // pin the benchmark's defects out of existence.
            var dir = Path.GetDirectoryName(file)!;
            var parent = Path.GetFileName(Path.GetDirectoryName(dir) ?? string.Empty);
            if (Path.GetFileName(dir).Equals("calor", StringComparison.Ordinal)
                && parent.Equals("reference", StringComparison.Ordinal))
            {
                data.Add(Path.GetRelativePath(root, file));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PairPrograms))]
    public void PairProgram_CompilesWithoutErrors(string relativePath)
    {
        var root = FindRepoRoot();
        Assert.NotNull(root);

        var full = Path.Combine(root!, relativePath);
        var result = Program.Compile(File.ReadAllText(full), full, new CompilationOptions());

        Assert.False(result.HasErrors,
            $"{relativePath} must compile clean:\n  " +
            string.Join("\n  ", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    /// <summary>
    /// The corpus must not silently empty out — a discovery bug that found zero files would make
    /// every case above vacuous, which is the same class of failure this file exists to prevent.
    /// </summary>
    [Fact]
    public void CorpusIsNotEmpty()
    {
        var count = PairPrograms().Count();
        Assert.True(count >= 20, $"expected the pair corpus to be discovered; found {count}");
    }

    /// <summary>
    /// Same guard for the samples corpus, which was written with the same silent-empty return and
    /// no check. Found by release review of v0.12: the guard above was added for exactly this
    /// failure mode and then not applied to the second discovery function in the same file.
    /// </summary>
    [Fact]
    public void SampleCorpusIsNotEmpty()
    {
        // 11 on disk at v0.12.0. The floor is a discovery guard, not an inventory pin — it must
        // catch "found zero" without failing on an intentional removal of one sample.
        var count = SamplePrograms().Count();
        Assert.True(count >= 8, $"expected the samples corpus to be discovered; found {count}");
    }

    public static TheoryData<string> SamplePrograms()
    {
        var data = new TheoryData<string>();
        var root = FindRepoRoot();
        var samples = root == null ? null : Path.Combine(root, "samples");
        if (samples == null || !Directory.Exists(samples))
        {
            return data;
        }

        foreach (var file in Directory.EnumerateFiles(samples, "*.calr", SearchOption.AllDirectories))
        {
            if (!file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                data.Add(Path.GetRelativePath(root!, file));
            }
        }

        return data;
    }

    /// <summary>
    /// Samples are what a reader copies, so they must be clean — and clean means WARNING-free too,
    /// not just error-free. `samples/Generics/generics.calr` is why this asserts warnings: the
    /// unresolved-type warning fired on its declared generic parameter `T`, telling a reader that
    /// a correct, documented construct might be a typo. Nothing compiled `samples/` before.
    /// </summary>
    [Theory]
    [MemberData(nameof(SamplePrograms))]
    public void Sample_CompilesWithoutErrorsOrTypeWarnings(string relativePath)
    {
        var root = FindRepoRoot();
        Assert.NotNull(root);

        var full = Path.Combine(root!, relativePath);
        var result = Program.Compile(File.ReadAllText(full), full, new CompilationOptions());

        Assert.False(result.HasErrors,
            $"{relativePath}:\n  " +
            string.Join("\n  ", result.Diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));

        var typeWarnings = result.Diagnostics.Warnings
            .Where(d => d.Message.Contains("is not known to the Calor type checker"))
            .ToList();
        Assert.True(typeWarnings.Count == 0,
            $"{relativePath} emits unresolved-type warnings:\n  " +
            string.Join("\n  ", typeWarnings.Select(d => d.Message)));
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "bench")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName;
    }
}
