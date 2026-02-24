namespace ParallelPlinq
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Collections.Generic;

    public static class ParallelExamples
    {
        public static void ParallelFor()
        {
            int[] results = new int[100];
            Parallel.For(0, 100, i =>
            {
                results[i] = i * i;
            });
        }

        public static void ParallelForEach(IEnumerable<string> items)
        {
            Parallel.ForEach(items, item =>
            {
                Console.WriteLine(item);
            });
        }

        public static void ParallelInvoke()
        {
            Parallel.Invoke(
                () => Console.WriteLine("Task A"),
                () => Console.WriteLine("Task B"),
                () => Console.WriteLine("Task C")
            );
        }

        public static IEnumerable<int> PlinqQuery(IEnumerable<int> numbers)
        {
            return numbers
                .AsParallel()
                .Where(n => n % 2 == 0)
                .Select(n => n * n);
        }
    }
}
