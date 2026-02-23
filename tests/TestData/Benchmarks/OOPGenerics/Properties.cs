using System;

namespace OOPGenerics
{
    public class UserProfile
    {
        public string Name { get; set; }
        private int age;
        public int Age
        {
            get => age;
            set { if (value < 0 || value > 150) throw new ArgumentException(); age = value; }
        }
        public string Email { get; set; }

        public UserProfile(string name, int age, string email)
        {
            Name = name; Age = age; Email = email;
        }
    }
}
