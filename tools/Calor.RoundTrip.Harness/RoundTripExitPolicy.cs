namespace Calor.RoundTrip.Harness;

public static class RoundTripExitPolicy
{
    public static bool IsFailure(RoundTripReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Inconclusive || report.Comparison?.Status != ComparisonStatus.Pass;
    }
}
