using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;

namespace SqlToAi.Security;

/// <summary>
/// Implements in-memory database access level determination based on configured level lists.
/// </summary>
public sealed class AccessLevelProvider : IAccessLevelProvider
{
    private readonly SqlToAiOptions _options;
    private readonly TargetAccessPolicy _targetPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessLevelProvider"/> class.
    /// </summary>
    /// <param name="options">Options containing database access level configurations.</param>
    public AccessLevelProvider(IOptions<SqlToAiOptions> options)
    {
        _options = options.Value;
        _targetPolicy = new TargetAccessPolicy(_options);
    }

    public AccessLevelProvider(
        IDatabaseConnectionFactory connectionFactory,
        IOptions<SqlToAiOptions> options,
        Microsoft.Extensions.Logging.ILogger<AccessLevelProvider> logger)
        : this(options)
    {
    }

    public Task<AccessLevel> GetTargetAccessLevelAsync(DatabaseTargetContext target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_targetPolicy.GetAccessLevel(target));
    }
}
