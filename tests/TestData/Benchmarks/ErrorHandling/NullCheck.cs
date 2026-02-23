using System;
namespace ErrorHandling
{
    public static class NullCheck
    {
        public static int SafeLength(string? s) => s?.Length ?? 0;
        public static string Coalesce(string? a, string fallback) => a ?? fallback;
        public static int CoalesceInt(int? value, int fallback) => value ?? fallback;
    }
}
