#nullable enable

using SqlToAi.Domain;

namespace SqlToAi.Tests.Integration;

/// <summary>
/// Shared assertion helpers for integration tests. The <see cref="Result{T}"/> type throws when
/// <c>Error</c> is accessed on a successful result, so the message argument to
/// <c>Assert.True(...)</c> must be wrapped in a helper that only touches the property when the
/// result is actually a failure.
/// </summary>
internal static class IntegrationAssertions
{
    /// <summary>
    /// Returns a human-readable failure message if the result is a failure, or
    /// <c>"&lt;success&gt;"</c> otherwise. Safe to call on any <see cref="Result{T}"/>.
    /// </summary>
    public static string FormatFailure<T>(Result<T> result)
    {
        if (result.IsFailure)
        {
            return $"{result.Error.Code}: {result.Error.Message}";
        }
        return "<success>";
    }
}
