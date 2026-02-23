using System;
using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class AsyncErrorHandling
    {
        public static async Task<string> SafeFetch(string url)
        {
            try { await Task.Delay(1); return $"Data from {url}"; }
            catch (Exception) { return "Error"; }
        }
    }
}
