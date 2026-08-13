using Xunit;

namespace Calor.Tasks.Tests;

public sealed class EffectDefaultEquivalenceTests
{
    [Fact]
    public void MsBuildTaskDefault_MatchesCompilerSdkDefault()
    {
        var task = new Calor.Tasks.CompileCalor();
        var compilerOptions = new Calor.Compiler.CompilationOptions();

        Assert.Equal(compilerOptions.EnforceEffects, task.EnforceEffects);
        Assert.True(task.EnforceEffects);
    }
}
