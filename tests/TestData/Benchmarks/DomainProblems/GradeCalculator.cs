using System;

namespace DomainProblems
{
    public static class GradeCalculator
    {
        public static double WeightedScore(double score, double weight) => score * weight;

        public static string LetterGrade(double score)
        {
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
        }

        public static bool IsPassing(double score) => score >= 60;
    }
}
