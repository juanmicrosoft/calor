namespace Loyalty;

/// <summary>Loyalty points calculator. All functions are pure.</summary>
public static class LoyaltyModule
{
    /// <summary>Base points: 2 per unit of spend.</summary>
    public static int BasePoints(int spend) => spend * 2;

    /// <summary>Bonus points: 5 per visit.</summary>
    public static int BonusPoints(int visits) => visits * 5;

    /// <summary>
    /// Earned plus bonus, floored: the returned award is never below
    /// <paramref name="minPoints"/> — the program's tier statements are
    /// printed against it directly.
    /// </summary>
    public static int AwardWithFloor(int earned, int bonus, int minPoints)
    {
        var total = earned + bonus;
        if (total < minPoints)
        {
            return minPoints;
        }

        return total;
    }

    /// <summary>Full award: floored spend-plus-visit points.</summary>
    public static int TotalAward(int spend, int visits, int minPoints)
    {
        var earned = BasePoints(spend);
        var bonus = BonusPoints(visits);
        return AwardWithFloor(earned, bonus, minPoints);
    }

    /// <summary>The floored award, but never above maxPoints; maxPoints wins over minPoints.</summary>
    public static int AwardWithCap(int earned, int bonus, int minPoints, int maxPoints)
    {
        var floored = AwardWithFloor(earned, bonus, minPoints);
        if (floored > maxPoints)
        {
            return maxPoints;
        }

        return floored;
    }

    /// <summary>Pure formatting.</summary>
    public static string FormatAward(int points) => "points: " + points.ToString();
}
