using System;

namespace UnsafeOption
{
    public static class UnsafeOptionModule
    {
        // Bug: No null check before accessing .Value
        public static int GetValue(int? maybeVal)
        {
            return maybeVal.Value;
        }
    }
}
