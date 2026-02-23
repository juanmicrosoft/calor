using System;
namespace OOPGenerics
{
    public abstract class Processor
    {
        public int Process(int input) { Validate(input); return Transform(input); }
        protected abstract void Validate(int input);
        protected abstract int Transform(int input);
    }
    public class Doubler : Processor
    {
        protected override void Validate(int input) { if (input < 0) throw new ArgumentException(); }
        protected override int Transform(int input) => input * 2;
    }
}
