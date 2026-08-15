#nullable enable

namespace SqlToAi.Database;

/// <summary>
/// Centralized SQL Server error numbers. Used by guardrail, error-mapping, and permission-detection
/// logic so the magic-number literals no longer leak into call sites.
/// </summary>
internal static class SqlServerErrorCode
{
    // Permissions.
    public const int ShowplanPermissionMissing = 262;
    public const int ActionPermissionDenied = 297;
    public const int InsufficientPermission = 300;

    // Timeouts.
    public const int ClientQueryTimeout = -2;
    public const int SemaphoreTimeout = 121;
    public const int WaitTimeout = 258;

    // Connectivity & Auth.
    public const int ConnectionInitializationError = 233;
    public const int ConnectionReset = 10054;
    public const int ConnectionTimedOut = 10060;
    public const int ConnectionRefused = 10061;
    public const int LoginFailed = 18456;

    // Misc infrastructure failures.
    public const int InstanceNotFound = 20;
    public const int StatementTooComplex = 40;
    public const int ServerNotFound = 53;
}
