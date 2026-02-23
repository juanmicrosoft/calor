using System;

namespace TokenEconomics
{
    public static class IsAlphaModule
    {
        public static bool IsUpper(char c) => c >= 'A' && c <= 'Z';
        public static bool IsLower(char c) => c >= 'a' && c <= 'z';
        public static bool IsAlpha(char c) => IsUpper(c) || IsLower(c);
    }
}
