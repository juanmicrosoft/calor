using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class DelayedResult
    {
        public static async Task<int> DelayedValue(int value, int delayMs)
        {
            await Task.Delay(delayMs);
            return value;
        }
        public static int ImmediateValue(int value) => value;
    }
}
