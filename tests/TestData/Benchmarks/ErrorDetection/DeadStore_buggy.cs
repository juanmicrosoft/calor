using System;

namespace DeadStoreExample
{
    public static class DeadStoreExampleModule
    {
        // Bug: First assignment to temp is never read (dead store)
        public static int Compute(int x)
        {
            int temp = x + 1; // Dead store - overwritten immediately
            temp = x * 2;
            return temp;
        }
    }
}
