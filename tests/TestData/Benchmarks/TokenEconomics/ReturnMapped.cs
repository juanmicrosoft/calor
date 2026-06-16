using System;

namespace ReturnMapped
{
    public static class ReturnMappedModule
    {
        public static int Double(int x)
        {
            return x * 2;
        }

        public static int Process(int n)
        {
            return Double(n);
        }
    }
}
