using System;

namespace DesignPatterns
{
    public class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public string Email { get; set; } = "";
    }

    public class PersonBuilder
    {
        private Person person = new Person();

        public PersonBuilder WithName(string name) { person.Name = name; return this; }
        public PersonBuilder WithAge(int age) { person.Age = age; return this; }
        public PersonBuilder WithEmail(string email) { person.Email = email; return this; }

        public Person Build()
        {
            if (string.IsNullOrEmpty(person.Name))
                throw new InvalidOperationException("Name is required");
            return person;
        }
    }
}
