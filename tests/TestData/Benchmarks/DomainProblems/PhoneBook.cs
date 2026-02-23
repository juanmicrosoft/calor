using System;
using System.Collections.Generic;

namespace DomainProblems
{
    public class PhoneBook
    {
        private Dictionary<string, string> contacts = new Dictionary<string, string>();

        public void Add(string name, string phone) => contacts[name] = phone;
        public void Remove(string name) => contacts.Remove(name);
        public string? Lookup(string name) => contacts.GetValueOrDefault(name);
        public bool Contains(string name) => contacts.ContainsKey(name);
        public int Count => contacts.Count;
        public bool IsEmpty => contacts.Count == 0;
    }
}
