using System;

namespace VoidSequence
{
    public static class VoidSequenceModule
    {
        public static void Initialize()
        {
        }

        public static void Process()
        {
        }

        public static void Shutdown()
        {
        }

        public static void Execute()
        {
            Initialize();
            Process();
            Shutdown();
        }
    }
}
