using Calor.Compiler.Migration;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CSharpMetadataReferenceReuseTests
{
    [Fact]
    public void RuntimeReferencesAndLoadedMetadataAreSharedAcrossConverters()
    {
        var samples = Enumerable.Range(0, 8)
            .Select(_ => new CSharpToCalorConverter().GetSemanticMetadataReferences())
            .ToArray();
        var metadataIds = samples.Select(references => references
            .Select(reference => Assert.IsAssignableFrom<PortableExecutableReference>(reference).GetMetadataId())
            .ToArray()).ToArray();

        Assert.NotEmpty(samples[0]);
        Assert.Contains(samples[0], reference =>
            Assert.IsAssignableFrom<PortableExecutableReference>(reference).FilePath == typeof(object).Assembly.Location);
        for (var sample = 1; sample < samples.Length; sample++)
        {
            Assert.NotSame(samples[0], samples[sample]);
            Assert.Equal(samples[0].Length, samples[sample].Length);
            for (var index = 0; index < samples[0].Length; index++)
            {
                Assert.Same(samples[0][index], samples[sample][index]);
                Assert.Equal(metadataIds[0][index], metadataIds[sample][index]);
            }
        }
    }

    [Fact]
    public void ConcurrentConvertersShareTheSameRuntimeReferenceSet()
    {
        var samples = Enumerable.Range(0, 32).AsParallel()
            .Select(_ => new CSharpToCalorConverter().GetSemanticMetadataReferences())
            .ToArray();

        Assert.NotEmpty(samples[0]);
        foreach (var sample in samples.Skip(1))
        {
            Assert.Equal(samples[0].Length, sample.Length);
            for (var index = 0; index < sample.Length; index++)
                Assert.Same(samples[0][index], sample[index]);
        }
    }

    [Fact]
    public void ConfiguredReferencesStayFreshAndKeepAliasesAndCompleteSetPolicy()
    {
        var runtime = new CSharpToCalorConverter().GetSemanticMetadataReferences();
        foreach (var complete in new[] { false, true })
        {
            var converter = new CSharpToCalorConverter(new ConversionOptions
            {
                ReferencesAreComplete = complete,
                References = [new ConversionReference(typeof(Program).Assembly.Location, ["projectAlias"])]
            });
            var first = converter.GetSemanticMetadataReferences();
            var second = converter.GetSemanticMetadataReferences();
            Assert.Equal(complete ? 1 : runtime.Length + 1, first.Length);
            Assert.Equal(first.Length, second.Length);
            Assert.NotSame(first, second);
            Assert.NotSame(first[^1], second[^1]);
            Assert.Equal(new[] { "projectAlias" }, first[^1].Properties.Aliases);
            Assert.Equal(first[^1].Properties, second[^1].Properties);
            Assert.Equal(typeof(Program).Assembly.Location,
                Assert.IsAssignableFrom<PortableExecutableReference>(first[^1]).FilePath);
            if (!complete)
                for (var index = 0; index < runtime.Length; index++)
                    Assert.Same(runtime[index], first[index]);
        }
    }
}
