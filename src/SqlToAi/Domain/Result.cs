#nullable enable

using System.Diagnostics.CodeAnalysis;

namespace SqlToAi.Domain;

#pragma warning disable CA1000 // Do not declare static members on generic types

/// <summary>
/// Represents the result of an operation that does not return a value, which can be either a success or a failure.
/// </summary>
public sealed class Result
{
    private readonly SqlToAiError? _error;

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    [MemberNotNullWhen(false, nameof(_error))]
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_error))]
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error associated with the failure. Throws if the operation succeeded.
    /// </summary>
    public SqlToAiError Error => IsFailure ? _error : throw new InvalidOperationException("Cannot access error of a successful result.");

    private Result(bool isSuccess, SqlToAiError? error)
    {
        IsSuccess = isSuccess;
        _error = error;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result Success() => new(true, null);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static Result Failure(SqlToAiError error) => new(false, error);

    public static implicit operator Result(SqlToAiError error) => Failure(error);
}

/// <summary>
/// Represents the result of an operation that returns a value, which can be either a success or a failure.
/// </summary>
/// <typeparam name="T">The type of the returned value.</typeparam>
public sealed class Result<T>
{
    private readonly T? _value;
    private readonly SqlToAiError? _error;

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_value))]
    [MemberNotNullWhen(false, nameof(_error))]
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_error))]
    [MemberNotNullWhen(false, nameof(_value))]
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the value associated with the success. Throws if the operation failed.
    /// </summary>
    public T Value => IsSuccess ? _value : throw new InvalidOperationException("Cannot access value of a failed result.");

    /// <summary>
    /// Gets the error associated with the failure. Throws if the operation succeeded.
    /// </summary>
    public SqlToAiError Error => IsFailure ? _error : throw new InvalidOperationException("Cannot access error of a successful result.");

    private Result(T value)
    {
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private Result(SqlToAiError error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static Result<T> Failure(SqlToAiError error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(SqlToAiError error) => Failure(error);
}
