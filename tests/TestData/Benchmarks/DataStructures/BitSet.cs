using System;

namespace DataStructures
{
    public static class BitSet
    {
        public static int SetBit(int bits, int pos) => bits | (1 << pos);
        public static int ClearBit(int bits, int pos) => bits & ~(1 << pos);
        public static bool TestBit(int bits, int pos) => (bits & (1 << pos)) != 0;

        public static int PopCount(int bits)
        {
            int count = 0;
            int n = bits;
            while (n != 0) { count += n & 1; n >>= 1; }
            return count;
        }
    }
}
