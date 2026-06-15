using System;

namespace ThreeWayMerge
{
    public static class ThreeWayMergeModule
    {
        public static int Merge(int a, int b, int c)
        {
            return a + b + c;
        }

        public static int Run(int x, int y, int z)
        {
            return Merge(x, y, z);
        }
    }
}
