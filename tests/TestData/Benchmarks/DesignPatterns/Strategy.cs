using System;

namespace DesignPatterns
{
    public interface ISortStrategy { bool ShouldSwap(int a, int b); }

    public class AscendingSort : ISortStrategy
    {
        public bool ShouldSwap(int a, int b) => a > b;
    }

    public class DescendingSort : ISortStrategy
    {
        public bool ShouldSwap(int a, int b) => a < b;
    }

    public class Sorter
    {
        private ISortStrategy strategy;
        public Sorter(ISortStrategy strategy) { this.strategy = strategy; }

        public void Sort(int[] arr)
        {
            for (int i = 0; i < arr.Length - 1; i++)
                for (int j = 0; j < arr.Length - i - 1; j++)
                    if (strategy.ShouldSwap(arr[j], arr[j + 1]))
                        (arr[j], arr[j + 1]) = (arr[j + 1], arr[j]);
        }
    }
}
