using System.Collections.Generic;
using System.Linq;

namespace MigrateFixture;

public static class OrderReports
{
    public static IEnumerable<string> PendingLabels(IEnumerable<int> statusCodes)
    {
        return statusCodes
            .Where(code => code >= 0)
            .Select(code => code switch
            {
                0 => "pending",
                1 => "shipped",
                2 => "delivered",
                _ => "unknown",
            });
    }

    public static int TotalPending(IEnumerable<int> statusCodes)
    {
        var sum = 0;
        foreach (var code in statusCodes)
        {
            if (code == 0)
            {
                sum++;
            }
        }
        return sum;
    }
}
