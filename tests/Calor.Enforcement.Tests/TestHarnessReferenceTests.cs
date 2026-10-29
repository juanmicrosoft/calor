using System.Reflection;
using Calor.Runtime;
using Xunit;

namespace Calor.Enforcement.Tests;

public sealed class TestHarnessReferenceTests
{
    [Fact]
    public void ConcurrentExecutionsReuseExactlyTheOriginalRuntimeMetadata()
    {
        var samples = Enumerable.Range(0, 16).AsParallel()
            .Select(_ => TestHarness.CompilationReferences)
            .ToArray();
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] paths =
        [
            typeof(object).Assembly.Location,
            typeof(Console).Assembly.Location,
            typeof(ContractViolationException).Assembly.Location,
            Assembly.Load("System.Runtime").Location,
            Path.Combine(runtimeDir, "System.Collections.dll"),
            Path.Combine(runtimeDir, "System.Linq.dll"),
            Path.Combine(runtimeDir, "System.Text.RegularExpressions.dll")
        ];
        Assert.Equal(paths, samples[0].Select(reference => reference.FilePath));
        var metadataIds = samples[0].Select(reference => reference.GetMetadataId()).ToArray();
        foreach (var sample in samples.Skip(1))
        {
            Assert.Equal(paths.Length, sample.Length);
            for (var index = 0; index < sample.Length; index++)
            {
                Assert.Same(samples[0][index], sample[index]);
                Assert.Equal(metadataIds[index], sample[index].GetMetadataId());
            }
        }
    }
}
