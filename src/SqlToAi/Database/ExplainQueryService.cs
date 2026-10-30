using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Database;

public sealed class ExplainQueryService : IExplainQueryService
{
    private readonly IShowplanExecutionService _showplanExecutionService;
    private readonly IShowplanXmlParser _showplanXmlParser;
    private readonly IExplainQueryStatementGuard _statementGuard;
    private readonly ISecurityGuard _securityGuard;
    private readonly IAccessLevelProvider _accessLevelProvider;
    private readonly SqlToAiOptions _options;
    private readonly DatabaseTargetResolver _targets;
    private readonly ILogger<ExplainQueryService> _logger;

    public ExplainQueryService(
        IShowplanExecutionService showplanExecutionService,
        IShowplanXmlParser showplanXmlParser,
        IExplainQueryStatementGuard statementGuard,
        ISecurityGuard securityGuard,
        IAccessLevelProvider accessLevelProvider,
        IOptions<SqlToAiOptions> options,
        ILogger<ExplainQueryService> logger)
    {
        _showplanExecutionService = showplanExecutionService ?? throw new ArgumentNullException(nameof(showplanExecutionService));
        _showplanXmlParser = showplanXmlParser ?? throw new ArgumentNullException(nameof(showplanXmlParser));
        _statementGuard = statementGuard ?? throw new ArgumentNullException(nameof(statementGuard));
        _securityGuard = securityGuard ?? throw new ArgumentNullException(nameof(securityGuard));
        _accessLevelProvider = accessLevelProvider ?? throw new ArgumentNullException(nameof(accessLevelProvider));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _targets = new DatabaseTargetResolver(_options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<Result<ExplainQueryResult>> ExplainQueryAsync(
        string databaseName,
        string query,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return Task.FromResult(Result<ExplainQueryResult>.Failure(
                SqlToAiError.InvalidParameters("database name must not be empty.")));
        }

        var targetResult = _targets.Resolve(databaseName);
        if (targetResult.IsFailure)
        {
            return Task.FromResult(Result<ExplainQueryResult>.Failure(targetResult.Error));
        }

        return ExplainQueryAsync(targetResult.Value, query, parameters, cancellationToken);
    }

    public async Task<Result<ExplainQueryResult>> ExplainQueryAsync(
        DatabaseTargetContext target,
        string query,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_securityGuard.IsTargetAllowed(target))
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{target.QualifiedName}' is blocked by security policies (static whitelist).");
        }

        var statementValidation = _statementGuard.ValidateStatement(query);
        if (statementValidation.IsFailure)
        {
            return Result<ExplainQueryResult>.Failure(statementValidation.Error);
        }

        var accessLevel = await _accessLevelProvider.GetTargetAccessLevelAsync(target, cancellationToken);
        var accessValidation = _statementGuard.ValidateAccess(target, accessLevel);
        if (accessValidation.IsFailure)
        {
            return Result<ExplainQueryResult>.Failure(accessValidation.Error);
        }

        var showplanResult = await _showplanExecutionService.ExecuteShowplanAsync(target, query, parameters, cancellationToken);
        if (showplanResult.IsFailure)
        {
            return Result<ExplainQueryResult>.Failure(showplanResult.Error);
        }

        return _showplanXmlParser.Parse(showplanResult.Value, target.QualifiedName, _options.ExplainQuery);
    }
}
