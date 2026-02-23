using System;
namespace ErrorHandling
{
    public static class MultipleCatch
    {
        public static int ParseInt(string input, int defaultVal)
        {
            try { return int.Parse(input); }
            catch (FormatException) { return defaultVal; }
            catch (OverflowException) { return defaultVal; }
        }
    }
}
