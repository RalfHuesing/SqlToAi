using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Security;

/// <summary>
/// Enforces database security policy guardrails using configured database access levels and excluded database patterns.
/// </summary>
public sealed class SecurityGuard : ISecurityGuard
{
    private readonly SqlToAiOptions _options;
    private readonly TargetAccessPolicy _targetPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityGuard"/> class.
    /// </summary>
    /// <param name="options">The bound options containing databases configurations.</param>
    public SecurityGuard(IOptions<SqlToAiOptions> options)
    {
        _options = options.Value;
        _targetPolicy = new TargetAccessPolicy(_options);
    }

    public bool IsTargetAllowed(DatabaseTargetContext target) =>
        _targetPolicy.GetAccessLevel(target) != AccessLevel.None;
}
