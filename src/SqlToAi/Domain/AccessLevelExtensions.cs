namespace SqlToAi.Domain;

internal static class AccessLevelExtensions
{
    internal static bool IsWriteAllowed(this AccessLevel accessLevel) =>
        accessLevel is AccessLevel.ReadWrite or AccessLevel.ReadWriteAnonymized;

    internal static bool RequiresAnonymization(this AccessLevel accessLevel) =>
        accessLevel is AccessLevel.ReadOnlyAnonymized or AccessLevel.ReadWriteAnonymized;
}
