using Calor.Compiler.Migration;
using Calor.RoundTrip.Harness.TaskGen;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

/// <summary>
/// Exhaustive pins for the D-W4.1 eligibility predicate — both clauses, the D-W4.3 attribution
/// guard, and the identical-failure requirement. Pure over hand-built evidence (no corpus).
/// </summary>
public class TaskGenPredicateTests
{
    private static FileConversionResult NativeFile()
    {
        var f = new FileConversionResult { FilePath = "Lib/A.cs", Status = FileStatus.Replaced };
        f.ApplyLossLedger([]); // zero losses => ConvertedNative
        return f;
    }

    private static FileConversionResult LossyFile()
    {
        var f = new FileConversionResult { FilePath = "Lib/A.cs", Status = FileStatus.Replaced };
        f.ApplyLossLedger([new ConversionLoss { Kind = ConversionLossKind.InteropPreserved, Feature = "x", Description = "d" }]);
        return f;
    }

    private static EligibilityEvidence Base(FileConversionResult file) => new()
    {
        MutatedFileResult = file,
        HasCoveringTest = true,
        CSharpArmHeldOutOutcome = "Failed",
        CalorArmHeldOutOutcome = "Failed",
        CSharpArmFailureSignature = "Assert.Equal()",
        CalorArmFailureSignature = "Assert.Equal()",
        Attribution = AttributionOutcome.AttributedToMutation,
    };

    [Fact]
    public void Eligible_WhenNativeSurvivesIdenticallyAndAttributed()
    {
        var v = EligibilityPredicate.Evaluate(Base(NativeFile()));
        Assert.True(v.Eligible);
        Assert.Equal(ExclusionReason.None, v.Reason);
    }

    [Fact]
    public void ClauseA_ExcludesNonNativeRegion()
    {
        var v = EligibilityPredicate.Evaluate(Base(LossyFile()));
        Assert.False(v.Eligible);
        Assert.Equal(ExclusionReason.NotNativeRegion, v.Reason);
        Assert.True(v.IsClauseA);
    }

    [Fact]
    public void ClauseA_ExcludesRevertedMutatedFile()
    {
        var reverted = new FileConversionResult { FilePath = "Lib/A.cs", Status = FileStatus.Reverted };
        var v = EligibilityPredicate.Evaluate(Base(reverted));
        Assert.False(v.Eligible);
        Assert.Equal(ExclusionReason.MutatedFileReverted, v.Reason);
        Assert.True(v.IsClauseA);
    }

    [Fact]
    public void NoCoveringTest_Excluded()
    {
        var ev = new EligibilityEvidence
        {
            MutatedFileResult = NativeFile(),
            HasCoveringTest = false,
            CSharpArmHeldOutOutcome = "Failed",
            CalorArmHeldOutOutcome = "Failed",
        };
        var v = EligibilityPredicate.Evaluate(ev);
        Assert.Equal(ExclusionReason.NoCoveringTest, v.Reason);
    }

    [Fact]
    public void ClauseB_NoObservableDefect_WhenCSharpArmPasses()
    {
        var ev = new EligibilityEvidence
        {
            MutatedFileResult = NativeFile(),
            HasCoveringTest = true,
            CSharpArmHeldOutOutcome = "Passed",
            CalorArmHeldOutOutcome = "Failed",
        };
        var v = EligibilityPredicate.Evaluate(ev);
        Assert.Equal(ExclusionReason.NoObservableDefect, v.Reason);
        Assert.True(v.IsClauseB);
    }

    [Fact]
    public void ClauseB_MutationDidNotSurvive_WhenCalorArmPasses()
    {
        var ev = new EligibilityEvidence
        {
            MutatedFileResult = NativeFile(),
            HasCoveringTest = true,
            CSharpArmHeldOutOutcome = "Failed",
            CalorArmHeldOutOutcome = "Passed",
        };
        var v = EligibilityPredicate.Evaluate(ev);
        Assert.Equal(ExclusionReason.MutationDidNotSurviveConversion, v.Reason);
        Assert.True(v.IsClauseB);
    }

    [Fact]
    public void ClauseB_ArmsDiverge_WhenSignaturesDiffer_AndRequireIdentical()
    {
        var ev = new EligibilityEvidence
        {
            MutatedFileResult = NativeFile(),
            HasCoveringTest = true,
            CSharpArmHeldOutOutcome = "Failed",
            CalorArmHeldOutOutcome = "Failed",
            CSharpArmFailureSignature = "Assert.Equal()",
            CalorArmFailureSignature = "System.DivideByZeroException",
            Attribution = AttributionOutcome.AttributedToMutation,
            RequireIdenticalSignature = true,
        };
        var v = EligibilityPredicate.Evaluate(ev);
        Assert.Equal(ExclusionReason.ArmsDiverge, v.Reason);
    }

    [Fact]
    public void ClauseB_DivergentSignaturesTolerated_WhenNotRequiringIdentical()
    {
        var ev = new EligibilityEvidence
        {
            MutatedFileResult = NativeFile(),
            HasCoveringTest = true,
            CSharpArmHeldOutOutcome = "Failed",
            CalorArmHeldOutOutcome = "Failed",
            CSharpArmFailureSignature = "Assert.Equal()",
            CalorArmFailureSignature = "System.DivideByZeroException",
            Attribution = AttributionOutcome.AttributedToMutation,
            RequireIdenticalSignature = false,
        };
        var v = EligibilityPredicate.Evaluate(ev);
        Assert.True(v.Eligible);
    }

    [Fact]
    public void Attribution_ExcludesConverterDivergenceInOtherFile()
    {
        var ev = new EligibilityEvidence
        {
            MutatedFileResult = NativeFile(),
            HasCoveringTest = true,
            CSharpArmHeldOutOutcome = "Failed",
            CalorArmHeldOutOutcome = "Failed",
            CSharpArmFailureSignature = "Assert.Equal()",
            CalorArmFailureSignature = "Assert.Equal()",
            Attribution = AttributionOutcome.DivergentOtherFile,
        };
        var v = EligibilityPredicate.Evaluate(ev);
        Assert.Equal(ExclusionReason.ConverterAttributed, v.Reason);
    }

    [Theory]
    [InlineData("Assert.Equal() Failure: Values differ\nExpected: 5\nActual: 6", "Assert.Equal()")]
    [InlineData("System.DivideByZeroException : Cannot divide by zero", "System.DivideByZeroException")]
    [InlineData("Assert.True() Failure", "Assert.True()")]
    [InlineData("", null)]
    public void NormalizeFailureSignature_ExtractsKind(string message, string? expected)
        => Assert.Equal(expected, EligibilityPredicate.NormalizeFailureSignature(message));
}
