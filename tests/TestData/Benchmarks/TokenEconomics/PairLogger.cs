using System;

namespace PairLogger
{
    public static class PairLoggerModule
    {
        public static void LogPair(string key, int value)
        {
        }

        public static void RunReport(string k1, int v1, string k2, int v2)
        {
            LogPair(k1, v1);
            LogPair(k2, v2);
        }
    }
}
