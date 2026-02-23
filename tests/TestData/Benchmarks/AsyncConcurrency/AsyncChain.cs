using System.Threading.Tasks;
namespace AsyncConcurrency
{
    public static class AsyncChain
    {
        public static async Task<int> Step1(int input) { await Task.Yield(); return input + 1; }
        public static async Task<int> Step2(int input) { await Task.Yield(); return input * 2; }
        public static async Task<int> Pipeline(int input)
        {
            int s1 = await Step1(input);
            return await Step2(s1);
        }
    }
}
