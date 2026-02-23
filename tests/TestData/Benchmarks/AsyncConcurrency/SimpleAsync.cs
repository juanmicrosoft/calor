using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class SimpleAsync
    {
        public static async Task<int> GetValue() { await Task.Delay(1); return 42; }
        public static int Fallback() => 0;
    }
}
