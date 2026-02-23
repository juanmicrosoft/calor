using System;

namespace OOPGenerics
{
    // C# auto-properties with init are extremely compact — one line per property.
    // Calor models properties as getter functions (§F/§I/§O/§R/§/F = 5 lines each).
    // Adversarial: C# wins on token economy for data-holding classes.
    public class Point
    {
        public int X { get; init; }
        public int Y { get; init; }
    }

    public class Contact
    {
        public string Name { get; init; } = "";
        public int Age { get; init; }
        public string Email { get; init; } = "";
    }
}
