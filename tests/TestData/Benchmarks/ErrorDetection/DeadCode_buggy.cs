using System;

namespace DeadCodeExample
{
    public static class DeadCodeExampleModule
    {
        // Bug: Unreachable code after return
        public static int GetStatus(int code)
        {
            return code;
            int unused = 42; // CS0162: Unreachable code detected
        }
    }
}
