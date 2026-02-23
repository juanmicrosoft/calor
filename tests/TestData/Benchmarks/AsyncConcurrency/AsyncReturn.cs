using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class AsyncReturn
    {
        public static async Task<int> ComputeAsync(int x) { await Task.Yield(); return x * x; }
        public static int ComputeSync(int x) => x * x;
    }
}
