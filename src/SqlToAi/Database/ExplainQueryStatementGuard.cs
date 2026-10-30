using System;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlToAi.Domain;
using SqlToAi.Security;

namespace SqlToAi.Database;

/// <summary>
/// Pre-connection validation guard enforcing statement allowlist and database access policies for <c>sql_explain_query</c>.
/// </summary>
public sealed class ExplainQueryStatementGuard : IExplainQueryStatementGuard
{
    private readonly ISecurityGuard _securityGuard;
    private readonly IReadOnlyGuard _readOnlyGuard;

    public ExplainQueryStatementGuard(ISecurityGuard securityGuard, IReadOnlyGuard readOnlyGuard)
    {
        _securityGuard = securityGuard ?? throw new ArgumentNullException(nameof(securityGuard));
        _readOnlyGuard = readOnlyGuard ?? throw new ArgumentNullException(nameof(readOnlyGuard));
    }

    /// <inheritdoc/>
    public Result ValidateAccess(DatabaseTargetContext target, AccessLevel accessLevel)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_securityGuard.IsTargetAllowed(target))
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{target.QualifiedName}' is blocked by security policies (static whitelist).");
        }

        if (accessLevel == AccessLevel.None)
        {
            return SqlToAiError.SafetyCheckFailed($"Database '{target.QualifiedName}' access was denied (AccessLevel: None).");
        }

        return Result.Success();
    }

    /// <inheritdoc/>
    public Result ValidateStatement(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return SqlToAiError.InvalidParameters("Query must not be empty.");
        }

        // 1. Parsing - unparseable SQL fails closed with SQL-AI-0102
        var parseResult = SqlScriptDomParser.ParseScript(query);
        if (!parseResult.Success || parseResult.Script is null)
        {
            return SqlToAiError.QueryError("SQL syntax could not be parsed safely before explain query execution.");
        }

        // 2. Caller controls anywhere in the batch/script - fails with SQL-AI-0104
        var controlVisitor = new ControlStatementVisitor();
        parseResult.Script.Accept(controlVisitor);
        if (controlVisitor.HasControl || SqlUseStatementDetector.ContainsUseStatement(query))
        {
            return SqlToAiError.SafetyCheckFailed("Execution, session, transaction, plan, or database controls (such as SET, USE, or transaction statements) are not permitted.");
        }

        // 3. Mutating statements across all access levels - fails with SQL-AI-0107
        if (!_readOnlyGuard.IsQuerySafe(query))
        {
            return SqlToAiError.WriteOperationBlocked(
                "The query contains mutating SQL statements (e.g. INSERT, UPDATE, DELETE, MERGE, SELECT INTO, DDL, EXEC) and was rejected.");
        }

        // 4. Closed statement shape allowlist: optional leading DECLARE statements,
        // followed by exactly one SELECT without INTO; nothing may follow the SELECT.
        // Multiple main statements or batches fail with SQL-AI-0101
        var script = parseResult.Script;
        if (script.Batches.Count != 1)
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        var statements = script.Batches[0].Statements;
        if (statements.Count == 0)
        {
            return SqlToAiError.InvalidParameters("Query contains no executable statements.");
        }

        bool seenSelect = false;
        for (int i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];

            if (!seenSelect)
            {
                if (statement is DeclareVariableStatement or DeclareTableVariableStatement)
                {
                    continue;
                }

                if (statement is SelectStatement selectStatement)
                {
                    if (selectStatement.Into is not null)
                    {
                        return SqlToAiError.WriteOperationBlocked("SELECT INTO statements are mutating and not permitted.");
                    }

                    seenSelect = true;

                    if (i != statements.Count - 1)
                    {
                        return SqlToAiError.MultipleStatementsForbidden();
                    }
                }
                else
                {
                    return SqlToAiError.MultipleStatementsForbidden();
                }
            }
        }

        if (!seenSelect)
        {
            return SqlToAiError.MultipleStatementsForbidden();
        }

        return Result.Success();
    }

    /// <inheritdoc/>
    public Result Validate(DatabaseTargetContext target, AccessLevel accessLevel, string query)
    {
        var accessResult = ValidateAccess(target, accessLevel);
        if (accessResult.IsFailure)
        {
            return accessResult;
        }

        return ValidateStatement(query);
    }

    private sealed class ControlStatementVisitor : TSqlFragmentVisitor
    {
        public bool HasControl { get; private set; }

        public override void Visit(TSqlFragment node)
        {
            if (HasControl) return;

            if (IsControlFragment(node))
            {
                HasControl = true;
            }
        }

        private static bool IsControlFragment(TSqlFragment node)
        {
            if (node is UseStatement or UseFederationStatement or TransactionStatement)
            {
                return true;
            }

            if (node is PredicateSetStatement
                or SetCommandStatement
                or SetOnOffStatement
                or SetVariableStatement
                or SetTransactionIsolationLevelStatement
                or SetRowCountStatement
                or SetErrorLevelStatement
                or SetIdentityInsertStatement
                or SetOffsetsStatement
                or SetTextSizeStatement
                or SetUserStatement
                or AlterDatabaseSetStatement
                or AlterDatabaseScopedConfigurationSetStatement)
            {
                return true;
            }

            if (node is TSqlStatement stmt && stmt.GetType().Name.StartsWith("Set", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
