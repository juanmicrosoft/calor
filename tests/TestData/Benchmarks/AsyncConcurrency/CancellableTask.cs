using System;
using System.Threading;
using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class CancellableTask
    {
        public static async Task<int> CountUntilCancelled(CancellationToken ct, int max)
        {
            int i = 0;
            while (!ct.IsCancellationRequested && i < max)
            {
                await Task.Delay(10, ct);
                i++;
            }
            return i;
        }
    }
}
