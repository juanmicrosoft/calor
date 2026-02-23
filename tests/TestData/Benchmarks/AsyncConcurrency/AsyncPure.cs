using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class AsyncPure
    {
        public static async Task<int> PureAdd(int a, int b) { await Task.Yield(); return a + b; }
        public static async Task<int> PureMultiply(int a, int b) { await Task.Yield(); return a * b; }
    }
}
