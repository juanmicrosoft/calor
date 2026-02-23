using System;
namespace ErrorHandling
{
    public static class TryFinally
    {
        public static int Process(int input)
        {
            bool resourceOpen = true;
            try { return input * 2; }
            finally { resourceOpen = false; }
        }
    }
}
