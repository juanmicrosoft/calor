using System;
namespace ErrorHandling
{
    public class ValidationException : Exception
    {
        public string Field { get; }
        public ValidationException(string field, string msg) : base(msg) { Field = field; }
    }
    public static class CustomException
    {
        public static int Validate(int value, int min, int max)
        {
            if (value < min || value > max)
                throw new ValidationException("value", $"Must be between {min} and {max}");
            return value;
        }
    }
}
