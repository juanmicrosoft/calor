using System;
namespace PatternMatching
{
    public record Temperature(double Value, string Unit);
    public static class PropertyMatch
    {
        public static string Classify(Temperature t) => t switch
        {
            { Value: < 0 } => "Freezing",
            { Value: < 15 } => "Cold",
            { Value: < 30 } => "Warm",
            _ => "Hot"
        };
    }
}
