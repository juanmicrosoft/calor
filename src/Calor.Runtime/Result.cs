namespace Calor.Runtime;

/// <summary>
/// Represents the result of an operation that can either succeed with a value of type T
/// or fail with an error of type E.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
/// <typeparam name="E">The type of the error value.</typeparam>
public readonly struct Result<T, E> : IEquatable<Result<T, E>>
{
    private readonly T? _value;
    private readonly E? _error;
    private readonly bool _isOk;

    private Result(T value)
    {
        _value = value;
        _error = default;
        _isOk = true;
    }

    private Result(E error, bool _)
    {
        _value = default;
        _error = error;
        _isOk = false;
    }

    /// <summary>
    /// Returns true if this result represents a success.
    /// </summary>
    public bool IsOk => _isOk;

    /// <summary>
    /// Returns true if this result represents an error.
    /// </summary>
    public bool IsErr => !_isOk;

    /// <summary>
    /// Creates a successful result with the given value.
    /// </summary>
    public static Result<T, E> Ok(T value) => new(value);

    /// <summary>
    /// Creates an error result with the given error.
    /// </summary>
    public static Result<T, E> Err(E error) => new(error, false);

    /// <summary>
    /// Gets the success value, throwing if this is an error result.
    /// </summary>
    public T Unwrap()
    {
        if (!_isOk)
        {
            throw new InvalidOperationException($"Called Unwrap on an Err result: {_error}");
        }
        return _value!;
    }

    /// <summary>
    /// Gets the success value, or the provided default if this is an error result.
    /// </summary>
    public T UnwrapOr(T defaultValue)
    {
        return _isOk ? _value! : defaultValue;
    }

    /// <summary>
    /// Gets the success value, or computes a default from the error if this is an error result.
    /// </summary>
    public T UnwrapOrElse(Func<E, T> defaultFactory)
    {
        return _isOk ? _value! : defaultFactory(_error!);
    }

    /// <summary>
    /// Gets the error value, throwing if this is a success result.
    /// </summary>
    public E UnwrapErr()
    {
        if (_isOk)
        {
            throw new InvalidOperationException($"Called UnwrapErr on an Ok result: {_value}");
        }
        return _error!;
    }

    /// <summary>
    /// Transforms the success value using the provided function.
    /// </summary>
    public Result<U, E> Map<U>(Func<T, U> mapper)
    {
        return _isOk ? Result<U, E>.Ok(mapper(_value!)) : Result<U, E>.Err(_error!);
    }

    /// <summary>
    /// Transforms the error value using the provided function.
    /// </summary>
    public Result<T, F> MapErr<F>(Func<E, F> mapper)
    {
        return _isOk ? Result<T, F>.Ok(_value!) : Result<T, F>.Err(mapper(_error!));
    }

    /// <summary>
    /// Chains another result-producing operation on success.
    /// </summary>
    public Result<U, E> AndThen<U>(Func<T, Result<U, E>> next)
    {
        return _isOk ? next(_value!) : Result<U, E>.Err(_error!);
    }

    /// <summary>
    /// Chains another result-producing operation on error.
    /// </summary>
    public Result<T, F> OrElse<F>(Func<E, Result<T, F>> next)
    {
        return _isOk ? Result<T, F>.Ok(_value!) : next(_error!);
    }

    /// <summary>
    /// Pattern matches on this result.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onOk, Func<E, TResult> onErr)
    {
        return _isOk ? onOk(_value!) : onErr(_error!);
    }

    /// <summary>
    /// Pattern matches on this result with side effects.
    /// </summary>
    public void Match(Action<T> onOk, Action<E> onErr)
    {
        if (_isOk)
            onOk(_value!);
        else
            onErr(_error!);
    }

    /// <summary>
    /// Converts this result to an Option, discarding any error.
    /// </summary>
    public Option<T> ToOption()
    {
        return _isOk ? Option<T>.Some(_value!) : Option<T>.None();
    }

    public bool Equals(Result<T, E> other)
    {
        if (_isOk != other._isOk) return false;
        return _isOk
            ? EqualityComparer<T>.Default.Equals(_value, other._value)
            : EqualityComparer<E>.Default.Equals(_error, other._error);
    }

    public override bool Equals(object? obj)
        => obj is Result<T, E> other && Equals(other);

    public override int GetHashCode()
        => _isOk ? HashCode.Combine(true, _value) : HashCode.Combine(false, _error);

    public static bool operator ==(Result<T, E> left, Result<T, E> right) => left.Equals(right);
    public static bool operator !=(Result<T, E> left, Result<T, E> right) => !left.Equals(right);

    public override string ToString()
        => _isOk ? $"Ok({_value})" : $"Err({_error})";
}

/// <summary>
/// Helper class for creating Result values without specifying type parameters.
/// </summary>
public static class Result
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result<T, E> Ok<T, E>(T value) => Result<T, E>.Ok(value);

    /// <summary>
    /// Creates an error result.
    /// </summary>
    public static Result<T, E> Err<T, E>(E error) => Result<T, E>.Err(error);

    /// <summary>
    /// Tries to execute a function, returning Ok if it succeeds or Err if it throws.
    /// </summary>
    public static Result<T, Exception> Try<T>(Func<T> action)
    {
        try
        {
            return Result<T, Exception>.Ok(action());
        }
        catch (Exception ex)
        {
            return Result<T, Exception>.Err(ex);
        }
    }

    /// <summary>
    /// Tries to execute an action, returning Ok(Unit) if it succeeds or Err if it throws.
    /// </summary>
    public static Result<Unit, Exception> Try(Action action)
    {
        try
        {
            action();
            return Result<Unit, Exception>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, Exception>.Err(ex);
        }
    }
}

/// <summary>
/// Represents the absence of a meaningful value (like void but usable as a type parameter).
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    public static readonly Unit Value = default;

    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public override string ToString() => "()";

    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
}
