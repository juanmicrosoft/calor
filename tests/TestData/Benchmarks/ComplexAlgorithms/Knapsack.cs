using System;

namespace ComplexAlgorithms
{
    public static class Knapsack
    {
        public static int Solve(int[] weights, int[] values, int capacity)
        {
            int n = weights.Length;
            var dp = new int[n + 1, capacity + 1];
            for (int i = 1; i <= n; i++)
            {
                for (int w = 0; w <= capacity; w++)
                {
                    dp[i, w] = dp[i - 1, w];
                    if (weights[i - 1] <= w)
                        dp[i, w] = Math.Max(dp[i, w], dp[i - 1, w - weights[i - 1]] + values[i - 1]);
                }
            }
            return dp[n, capacity];
        }
    }
}
