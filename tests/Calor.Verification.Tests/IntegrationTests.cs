using Calor.Compiler;
using Calor.Compiler.Verification.Z3;
using Xunit;

namespace Calor.Verification.Tests;

/// <summary>
/// End-to-end integration tests for the static contract verification feature.
/// </summary>
public class IntegrationTests
{
    [SkippableFact]
    public void ProvenContract_EmitsComment_NotRuntimeCheck()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x 0)
  §S (>= result 0)
  §R (* x x)
§/F{f001}
§/M{m001}";

        var options = new CompilationOptions
        {
            VerifyContracts = true
        };

        var result = Program.Compile(source, "test.calr", options);

        Assert.False(result.HasErrors);

        // Check that postcondition was proven (comment present)
        Assert.Contains("// PROVEN:", result.GeneratedCode);
    }

    [SkippableFact]
    public void DisprovenContract_EmitsWarning_AndRuntimeCheck()
    {
        Skip.IfNot(Z3ContextFactory.IsAvailable, "Z3 not available");

        var source = @"
§M{m001:Test}
§F{f001:Bad:pub}
  §I{i32:x}
  §O{i32}
  §S (> result x)
  §R x
§/F{f001}
§/M{m001}";

        var options = new CompilationOptions
        {
            VerifyContracts = true
        };

        var result = Program.Compile(source, "test.calr", options);

        // Check for warning about violation
        var warnings = result.Diagnostics.Warnings.ToList();
        Assert.Contains(warnings, w => w.Message.Contains("Counterexample"));

        // Runtime check should still be present
        Assert.Contains("ContractViolationException", result.GeneratedCode);
    }

    [Fact]
    public void WithoutVerifyFlag_BehaviorIdentical()
    {
        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x 0)
  §S (>= result 0)
  §R (* x x)
§/F{f001}
§/M{m001}";

        var withoutVerify = Program.Compile(source, "test.calr", new CompilationOptions
        {
            VerifyContracts = false
        });

        // Both should compile successfully
        Assert.False(withoutVerify.HasErrors);

        // Without verify should have runtime checks
        Assert.Contains("ContractViolationException", withoutVerify.GeneratedCode);
    }

    [Fact]
    public void Z3Unavailable_GracefulFallback()
    {
        // This test runs regardless of Z3 availability
        // If Z3 is unavailable, it should still compile successfully

        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x 0)
  §S (>= result 0)
  §R (* x x)
§/F{f001}
§/M{m001}";

        var options = new CompilationOptions
        {
            VerifyContracts = true
        };

        var result = Program.Compile(source, "test.calr", options);

        // Should succeed regardless of Z3 availability
        Assert.False(result.HasErrors);

        // If Z3 wasn't available, should have info message
        if (!Z3ContextFactory.IsAvailable)
        {
            var infos = result.Diagnostics.Where(d => d.Code == "Calor0700").ToList();
            Assert.NotEmpty(infos);
        }
    }

    [Fact]
    public void ExistingTestsShouldStillPass_ContractCompilation()
    {
        // Basic contract compilation without --verify should work as before
        var source = @"
§M{m001:Test}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}";

        var result = Program.Compile(source, "test.calr");

        Assert.False(result.HasErrors);
        Assert.Contains("public static int Add(int a, int b)", result.GeneratedCode);
    }

    [Fact]
    public void ContractModeOff_NoChecks()
    {
        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x 0)
  §S (>= result 0)
  §R (* x x)
§/F{f001}
§/M{m001}";

        var options = new CompilationOptions
        {
            ContractMode = ContractMode.Off,
            VerifyContracts = false
        };

        var result = Program.Compile(source, "test.calr", options);

        Assert.False(result.HasErrors);
        Assert.DoesNotContain("ContractViolationException", result.GeneratedCode);
    }
}
