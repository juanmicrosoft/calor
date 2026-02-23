using System;
using System.Collections.Generic;
using System.Linq;

namespace CollectionsLINQ
{
    // C# LINQ pipelines are extremely compact — one-liner method chains.
    // Calor has no LINQ equivalent; must decompose into individual fold functions.
    // Adversarial: C# decisively wins on token economy for collection operations.
    public static class LinqPipeline
    {
        public static int SumPositive(List<int> list) => list.Where(x => x > 0).Sum();
        public static int CountEven(List<int> list) => list.Count(x => x % 2 == 0);
        public static int Max(List<int> list) => list.Max();
        public static bool AnyAbove(List<int> list, int threshold) => list.Any(x => x > threshold);
        public static double AveragePositive(List<int> list) => list.Where(x => x > 0).Average();
    }
}
