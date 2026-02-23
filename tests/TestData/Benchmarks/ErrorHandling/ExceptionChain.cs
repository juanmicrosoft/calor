using System;
namespace ErrorHandling
{
    public static class ExceptionChain
    {
        public static int WrapError(int code)
        {
            try { if (code < 0) throw new InvalidOperationException("Inner"); return code; }
            catch (Exception ex) { throw new ApplicationException("Outer", ex); }
        }
    }
}
