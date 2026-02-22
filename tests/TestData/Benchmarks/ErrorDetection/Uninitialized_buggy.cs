using System;

namespace UninitExample
{
    public static class UninitExampleModule
    {
        // Bug: Variable may be used before assignment on the else path
        public static int Process(int n)
        {
            int result;
            if (n > 0)
            {
                result = n;
            }
            return result; // CS0165: Use of unassigned local variable
        }
    }
}
