using System;
namespace ErrorHandling
{
    public static class BasicTryCatch
    {
        public static int SafeDivide(int a, int b)
        {
            try { return a / b; }
            catch (DivideByZeroException) { return 0; }
        }
    }
}
