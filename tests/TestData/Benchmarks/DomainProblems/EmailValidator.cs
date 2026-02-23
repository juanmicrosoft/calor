using System;

namespace DomainProblems
{
    public static class EmailValidator
    {
        public static bool IsValid(string email)
        {
            if (email.Length < 5 || email.Length > 254) return false;
            int atIndex = email.IndexOf('@');
            if (atIndex <= 0 || atIndex == email.Length - 1) return false;
            int dotIndex = email.LastIndexOf('.');
            return dotIndex > atIndex + 1 && dotIndex < email.Length - 1;
        }
    }
}
