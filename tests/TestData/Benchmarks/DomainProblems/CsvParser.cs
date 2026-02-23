using System;
using System.Collections.Generic;

namespace DomainProblems
{
    public static class CsvParser
    {
        public static List<string[]> Parse(string csv)
        {
            var result = new List<string[]>();
            var lines = csv.Split('
');
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                result.Add(line.Split(','));
            }
            return result;
        }

        public static int FieldCount(string line) => line.Split(',').Length;
    }
}
