using System;

namespace OOPGenerics
{
    public class Person
    {
        public string FirstName { get; }
        public string LastName { get; }
        public int BirthYear { get; }

        public Person(string first, string last, int birthYear)
        {
            FirstName = first; LastName = last; BirthYear = birthYear;
        }

        public int Age(int currentYear) => currentYear - BirthYear;
        public bool IsAdult(int currentYear) => Age(currentYear) >= 18;
        public string FullName => $"{FirstName} {LastName}";
    }
}
