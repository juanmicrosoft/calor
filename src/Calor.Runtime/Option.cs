namespace Calor.Runtime;

/// <summary>
/// Represents an optional value that may or may not be present.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public readonly struct Option<T> : IEquatable<Option<T>>
{
    private readonly T? _value;
    private readonly bool _hasValue;

    private Option(T value)
    {
        _value = value;
        _hasValue = true;
    }

    private Option(bool _)
    {
        _value = default;
        _hasValue = false;
    }

    /// <summary>
    /// Returns true if this option contains a value.
    /// </summary>
    public bool IsSome => _hasValue;

    /// <summary>
    /// Returns true if this option is empty.
    /// </summary>
    public bool IsNone => !_hasValue;

    /// <summary>
    /// Creates an option containing the given value.
    /// </summary>
    public static Option<T> Some(T value) => new(value);

    /// <summary>
    /// Creates an empty option.
    /// </summary>
    public static Option<T> None() => new(false);

    /// <summary>
    /// Gets the contained value, throwing if this option is empty.
    /// </summary>
    public T Unwrap()
    {
        if (!_hasValue)
        {
            throw new InvalidOperationException("Called Unwrap on a None option");
        }
        return _value!;
    }

    /// <summary>
    /// Gets the contained value, or the provided default if this option is empty.
    /// </summary>
    public T UnwrapOr(T defaultValue)
    {
        return _hasValue ? _value! : defaultValue;
    }

    /// <summary>
    /// Gets the contained value, or computes a default if this option is empty.
    /// </summary>
    public T UnwrapOrElse(Func<T> defaultFactory)
    {
        return _hasValue ? _value! : defaultFactory();
    }

    /// <summary>
    /// Gets the contained value, or default(T) if this option is empty.
    /// </summary>
    public T? UnwrapOrDefault()
    {
        return _hasValue ? _value : default;
    }

    /// <summary>
    /// Transforms the contained value using the provided function.
    /// </summary>
    public Option<U> Map<U>(Func<T, U> mapper)
    {
        return _hasValue ? Option<U>.Some(mapper(_value!)) : Option<U>.None();
    }

    /// <summary>
    /// Chains another option-producing operation.
    /// </summary>
    public Option<U> AndThen<U>(Func<T, Option<U>> next)
    {
        return _hasValue ? next(_value!) : Option<U>.None();
    }

    /// <summary>
    /// Returns this option if it has a value, otherwise returns the alternative.
    /// </summary>
    public Option<T> Or(Option<T> alternative)
    {
        return _hasValue ? this : alternative;
    }

    /// <summary>
    /// Returns this option if it has a value, otherwise computes an alternative.
    /// </summary>
    public Option<T> OrElse(Func<Option<T>> alternativeFactory)
    {
        return _hasValue ? this : alternativeFactory();
    }

    /// <summary>
    /// Filters the option based on a predicate.
    /// </summary>
    public Option<T> Filter(Func<T, bool> predicate)
    {
        return _hasValue && predicate(_value!) ? this : None();
    }

    /// <summary>
    /// Pattern matches on this option.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSome, Func<TResult> onNone)
    {
        return _hasValue ? onSome(_value!) : onNone();
    }

    /// <summary>
    /// Pattern matches on this option with side effects.
    /// </summary>
    public void Match(Action<T> onSome, Action onNone)
    {
        if (_hasValue)
            onSome(_value!);
        else
            onNone();
    }

    /// <summary>
    /// Converts this option to a Result, using the provided error if empty.
    /// </summary>
    public Result<T, E> OkOr<E>(E error)
    {
        return _hasValue ? Result<T, E>.Ok(_value!) : Result<T, E>.Err(error);
    }

    /// <summary>
    /// Converts this option to a Result, computing the error if empty.
    /// </summary>
    public Result<T, E> OkOrElse<E>(Func<E> errorFactory)
    {
        return _hasValue ? Result<T, E>.Ok(_value!) : Result<T, E>.Err(errorFactory());
    }

    /// <summary>
    /// Returns an enumerable with zero or one element.
    /// </summary>
    public IEnumerable<T> AsEnumerable()
    {
        if (_hasValue)
            yield return _value!;
    }

    public bool Equals(Option<T> other)
    {
        if (_hasValue != other._hasValue) return false;
        return !_hasValue || EqualityComparer<T>.Default.Equals(_value, other._value);
    }

    public override bool Equals(object? obj)
        => obj is Option<T> other && Equals(other);

    public override int GetHashCode()
        => _hasValue ? HashCode.Combine(true, _value) : HashCode.Combine(false);

    public static bool operator ==(Option<T> left, Option<T> right) => left.Equals(right);
    public static bool operator !=(Option<T> left, Option<T> right) => !left.Equals(right);

    public override string ToString()
        => _hasValue ? $"Some({_value})" : "None";

    /// <summary>
    /// Implicit conversion from a value to Some(value).
    /// </summary>
    public static implicit operator Option<T>(T value) => Some(value);
}

/// <summary>
/// Helper class for creating Option values without specifying type parameters.
/// </summary>
public static class Option
{
    /// <summary>
    /// Creates an option containing the given value.
    /// </summary>
    public static Option<T> Some<T>(T value) => Option<T>.Some(value);

    /// <summary>
    /// Creates an empty option.
    /// </summary>
    public static Option<T> None<T>() => Option<T>.None();

    /// <summary>
    /// Creates an option from a nullable value.
    /// </summary>
    public static Option<T> FromNullable<T>(T? value) where T : struct
    {
        return value.HasValue ? Option<T>.Some(value.Value) : Option<T>.None();
    }

    /// <summary>
    /// Creates an option from a reference type that might be null.
    /// </summary>
    public static Option<T> FromNullable<T>(T? value) where T : class
    {
        return value is not null ? Option<T>.Some(value) : Option<T>.None();
    }
}
