using System;
namespace PatternMatching
{
    public static class WildcardMatch
    {
        public static int DefaultValue(int typeCode) => typeCode switch { 0 => 0, 1 => 1, _ => -1 };
        public static bool IsKnown(int typeCode) => typeCode is 0 or 1;
    }
}
