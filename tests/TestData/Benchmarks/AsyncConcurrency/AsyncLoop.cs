using System.Collections.Generic;
using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class AsyncLoop
    {
        public static async Task<List<int>> ProcessAll(int[] items)
        {
            var results = new List<int>();
            foreach (var item in items)
            {
                await Task.Yield();
                results.Add(item * 2);
            }
            return results;
        }
    }
}
