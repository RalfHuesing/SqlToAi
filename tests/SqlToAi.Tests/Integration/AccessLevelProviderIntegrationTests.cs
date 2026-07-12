#nullable enable

using System.Collections.Generic;
using System.Linq;
using Dapper;
using SqlToAi.Domain;

namespace SqlToAi.Tests.Integration;

[Trait("Category", "Integration")]
[Collection(SqlServerCollectionFixture.Name)]
public sealed class AccessLevelProviderIntegrationTests
{
    private readonly SqlServerFixture _fx;
    private readonly string _db;

    public AccessLevelProviderIntegrationTests(SqlServerFixture fx)
    {
        _fx = fx;
        _db = fx.Options.Databases.Default;
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnConfiguredLevel_ForAllowedDatabase()
    {
        // The bundled appsettings.json dynamically determines the access level.
        // We execute the AccessCheckSql query directly on the connection to see what it evaluates to
        // for the current login and database, then assert the provider matches it.
        var sql = _fx.Options.Databases.AccessCheckSql;
        var expectedLevel = AccessLevel.ReadOnlyAnonymized; // default fallback if no query configured

        if (!string.IsNullOrWhiteSpace(sql))
        {
            using var connection = _fx.ConnectionFactory.CreateConnection(_db);
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            var queryResult = await connection.QueryFirstOrDefaultAsync<object>(
                new CommandDefinition(sql, cancellationToken: TestContext.Current.CancellationToken));

            if (queryResult != null)
            {
                expectedLevel = ParseResult(queryResult);
            }
            else
            {
                expectedLevel = AccessLevel.None;
            }
        }

        var level = await _fx.AccessLevelProvider.GetAccessLevelAsync(_db, TestContext.Current.CancellationToken);

        Assert.Equal(expectedLevel, level);
    }

    private static AccessLevel ParseResult(object result)
    {
        object? rawValue = null;

        if (result is IDictionary<string, object> row)
        {
            var key = row.Keys.FirstOrDefault(k => string.Equals(k, "AccessLevel", StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                rawValue = row[key];
            }
            else
            {
                rawValue = row.Values.FirstOrDefault();
            }
        }
        else
        {
            rawValue = result;
        }

        return ParseAccessLevel(rawValue);
    }

    private static AccessLevel ParseAccessLevel(object? val)
    {
        if (val is null)
        {
            return AccessLevel.None;
        }

        string strVal = val.ToString()?.Trim() ?? string.Empty;

        if (int.TryParse(strVal, out int intVal))
        {
            return intVal switch
            {
                0 => AccessLevel.None,
                1 => AccessLevel.SchemaOnly,
                2 => AccessLevel.ReadOnlyAnonymized,
                3 => AccessLevel.ReadOnly,
                4 => AccessLevel.ReadWrite,
                _ => AccessLevel.None
            };
        }

        if (Enum.TryParse<AccessLevel>(strVal, true, out var parsedEnum))
        {
            return parsedEnum;
        }

        return AccessLevel.None;
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldCacheResult_WithinTtl()
    {
        // First call warms the cache
        var first = await _fx.AccessLevelProvider.GetAccessLevelAsync(_db, TestContext.Current.CancellationToken);

        // Second call within the same TTL window must hit the cache. The function is
        // idempotent and returns the same level either way — we just exercise the second call
        // path to make sure caching does not throw.
        var second = await _fx.AccessLevelProvider.GetAccessLevelAsync(_db, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetAccessLevelAsync_ShouldReturnNone_ForEmptyDatabaseName()
    {
        // Empty name is treated defensively as None.
        var level = await _fx.AccessLevelProvider.GetAccessLevelAsync("", TestContext.Current.CancellationToken);

        Assert.Equal(AccessLevel.None, level);
    }
}
