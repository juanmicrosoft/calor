// C#-arm shim (harness-provided, fixed, not agent-editable).
namespace LoyaltyPair.Harness;

internal static class TestShim
{
    public static int BasePoints(int spend) => global::Loyalty.LoyaltyModule.BasePoints(spend);
    public static int BonusPoints(int visits) => global::Loyalty.LoyaltyModule.BonusPoints(visits);
    public static int AwardWithFloor(int earned, int bonus, int minPoints) => global::Loyalty.LoyaltyModule.AwardWithFloor(earned, bonus, minPoints);
    public static int TotalAward(int spend, int visits, int minPoints) => global::Loyalty.LoyaltyModule.TotalAward(spend, visits, minPoints);
    public static int AwardWithCap(int earned, int bonus, int minPoints, int maxPoints) => global::Loyalty.LoyaltyModule.AwardWithCap(earned, bonus, minPoints, maxPoints);
    public static string FormatAward(int points) => global::Loyalty.LoyaltyModule.FormatAward(points);
}
