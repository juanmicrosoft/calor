using System;

namespace OOPGenerics
{
    public abstract class Animal
    {
        public abstract string Sound();
        public abstract int Legs { get; }
        public virtual bool CanFly => false;
    }

    public class Dog : Animal
    {
        public override string Sound() => "Woof";
        public override int Legs => 4;
    }

    public class Cat : Animal
    {
        public override string Sound() => "Meow";
        public override int Legs => 4;
    }

    public class Bird : Animal
    {
        public override string Sound() => "Tweet";
        public override int Legs => 2;
        public override bool CanFly => true;
    }
}
