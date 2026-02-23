using System;
using System.Linq;

namespace DomainProblems
{
    public static class PasswordValidator
    {
        public static bool LongEnough(string password, int minLength) => password.Length >= minLength;
        public static bool HasDigit(string password) => password.Any(char.IsDigit);
        public static bool HasUpper(string password) => password.Any(char.IsUpper);
        public static bool HasSpecial(string password) => password.Any(c => !char.IsLetterOrDigit(c));

        public static int StrengthScore(string password, int minLength = 8)
        {
            int score = 0;
            if (LongEnough(password, minLength)) score++;
            if (HasDigit(password)) score++;
            if (HasUpper(password)) score++;
            if (HasSpecial(password)) score++;
            return score;
        }
    }
}
