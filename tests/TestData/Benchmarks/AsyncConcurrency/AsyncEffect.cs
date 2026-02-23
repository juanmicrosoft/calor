using System;
using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class AsyncEffect
    {
        public static async Task<string> FetchData(string url) { await Task.Delay(1); return $"Data from {url}"; }
        public static async Task<string> LogAndFetch(string url)
        {
            Console.WriteLine($"Fetching {url}");
            return await FetchData(url);
        }
    }
}
