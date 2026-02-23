using System;
namespace ErrorHandling
{
    public class Result<T>
    {
        public T? Value { get; }
        public string? Error { get; }
        public bool IsSuccess => Error == null;
        private Result(T value) { Value = value; }
        private Result(string error) { Error = error; }
        public static Result<T> Ok(T value) => new(value);
        public static Result<T> Fail(string error) => new(error);
        public T ValueOrDefault(T def) => IsSuccess ? Value! : def;
    }
}
