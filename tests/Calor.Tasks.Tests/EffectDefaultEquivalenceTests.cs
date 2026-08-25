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

    /// <summary>
    /// Roadmap §4.5 (v0.15): proof-based guard elision is default-on, and the
    /// default must be the same on every surface — CompilationOptions (SDK / MCP /
    /// review-packet / watch inherit it), the MSBuild task, Sdk.targets, the emitter
    /// and the verifier's diagnostic mirror. A drift on any one surface would make
    /// the same source emit different C# depending on how it was built.
    /// </summary>
    [Fact]
    public void ElideProvenGuardsDefault_AgreesAcrossSurfaces()
    {
        var task = new Calor.Tasks.CompileCalor();
        var compilerOptions = new Calor.Compiler.CompilationOptions();
        var verificationOptions = new Calor.Compiler.Verification.Z3.VerificationOptions();
        var emitter = new Calor.Compiler.CodeGen.CSharpEmitter(Calor.Compiler.ContractMode.Debug);

        Assert.True(compilerOptions.ElideProvenGuards);
        Assert.Equal(compilerOptions.ElideProvenGuards, task.ElideProvenGuards);
        Assert.Equal(compilerOptions.ElideProvenGuards, verificationOptions.ElideProvenGuards);
        Assert.Equal(compilerOptions.ElideProvenGuards, emitter.ElideProvenGuards);

        // Sdk.targets seeds CalorElideProvenGuards for project builds; its literal
        // default must match the in-process one.
        var targets = File.ReadAllText(FindSdkTargets());
        var match = System.Text.RegularExpressions.Regex.Match(
            targets,
            @"<CalorElideProvenGuards Condition=""'\$\(CalorElideProvenGuards\)' == ''"">(\w+)</CalorElideProvenGuards>");
        Assert.True(match.Success, "Sdk.targets must seed a CalorElideProvenGuards default");
        Assert.Equal(
            compilerOptions.ElideProvenGuards.ToString().ToLowerInvariant(),
            match.Groups[1].Value);
    }

    private static string FindSdkTargets()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src", "Calor.Sdk", "Sdk", "Sdk.targets");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("src/Calor.Sdk/Sdk/Sdk.targets not found above " + AppContext.BaseDirectory);
    }
}
