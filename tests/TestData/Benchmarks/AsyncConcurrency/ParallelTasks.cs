using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class ParallelTasks
    {
        public static async Task<int> RunBoth(Task<int> a, Task<int> b)
        {
            var results = await Task.WhenAll(a, b);
            return results[0] + results[1];
        }
    }
}
