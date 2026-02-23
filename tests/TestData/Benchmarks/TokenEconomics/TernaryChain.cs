using System;

namespace TokenEconomics
{
    // C# ternary chains are extremely compact.
    // Calor requires §IF/§EI/§EL/§/I block syntax.
    // Adversarial: C# wins on token economy for branching logic.
    public static class TernaryChain
    {
        public static int Classify(int score) =>
            score >= 90 ? 4 : score >= 80 ? 3 : score >= 70 ? 2 : score >= 60 ? 1 : 0;

        public static int Signum(int x) => x > 0 ? 1 : x < 0 ? -1 : 0;

        public static int Clamp100(int x) => x < 0 ? 0 : x > 100 ? 100 : x;
    }
}
