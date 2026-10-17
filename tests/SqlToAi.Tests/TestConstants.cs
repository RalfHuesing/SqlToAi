namespace SqlToAi.Tests;

/// <summary>
/// Central location for test constants to avoid magic literals in test classes.
/// </summary>
public static class TestConstants
{
    /// <summary>
    /// The raw physical database name used across integration and unit tests.
    /// </summary>
    public const string DatabaseName = "DemoDB";

    /// <summary>
    /// Qualified target for <see cref="DatabaseName"/> on the default test server alias.
    /// Migrated services (query execution, validation) require this form.
    /// </summary>
    public const string QualifiedDatabaseName = "test/DemoDB";

    /// <summary>
    /// Qualified target with the same raw database name on a second policy owner.
    /// </summary>
    public const string AlternateQualifiedDatabaseName = "prod/DemoDB";
}
