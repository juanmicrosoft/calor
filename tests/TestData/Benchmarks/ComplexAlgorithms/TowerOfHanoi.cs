using System;

namespace ComplexAlgorithms
{
    public static class TowerOfHanoi
    {
        public static int TotalMoves(int disks)
        {
            if (disks <= 0) throw new ArgumentException("Must have at least 1 disk");
            return (int)Math.Pow(2, disks) - 1;
        }

        public static void Solve(int n, string from, string to, string aux, Action<string> log)
        {
            if (n == 1)
            {
                log($"Move disk 1 from {from} to {to}");
                return;
            }
            Solve(n - 1, from, aux, to, log);
            log($"Move disk {n} from {from} to {to}");
            Solve(n - 1, aux, to, from, log);
        }
    }
}
