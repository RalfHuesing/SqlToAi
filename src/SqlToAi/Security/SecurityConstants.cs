#nullable enable

namespace SqlToAi.Security;

/// <summary>
/// Centralized security-related constants. Currently limited to the ReDoS-protection
/// timeout used by every regex-based matcher and validator across the server.
/// </summary>
public static class SecurityConstants
{
    /// <summary>
    /// ReDoS-protection timeout applied to every regex compiled by the server.
    /// Exceeding this budget aborts the match and is treated as "not safe" (fail-closed).
    /// </summary>
    public static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromMilliseconds(200);
}
