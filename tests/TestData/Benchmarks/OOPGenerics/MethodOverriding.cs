using System;
namespace OOPGenerics
{
    public class Animal { public virtual string Speak() => "..."; }
    public class Dog : Animal { public override string Speak() => "Woof!"; }
    public class Cat : Animal { public override string Speak() => "Meow!"; }
}
