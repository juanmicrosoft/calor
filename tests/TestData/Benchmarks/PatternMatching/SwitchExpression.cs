using System;

namespace PatternMatching
{
    // C# switch expressions are extremely compact for multi-way branching.
    // Calor requires §IF/§EI/§EL chains with explicit arrow syntax.
    // Adversarial: C# wins on token economy for pattern dispatch.
    public static class SwitchExpression
    {
        public static string Season(int month) => month switch
        {
            12 or 1 or 2 => "Winter",
            <= 5 => "Spring",
            <= 8 => "Summer",
            <= 11 => "Autumn",
            _ => "Winter"
        };

        public static string DayType(int day) => day switch
        {
            0 or 6 => "Weekend",
            _ => "Weekday"
        };

        public static string HttpCategory(int status) => status switch
        {
            < 200 => "Informational",
            < 300 => "Success",
            < 400 => "Redirection",
            < 500 => "Client Error",
            _ => "Server Error"
        };
    }
}
