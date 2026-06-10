using System;

namespace LogPipeline
{
    public static class LogPipelineModule
    {
        public static void Log(string msg)
        {
        }

        public static void RunBatch(string a, string b, string c)
        {
            Log(a);
            Log(b);
            Log(c);
        }
    }
}
