#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlToAi.Database;

namespace SqlToAi.Security;

/// <summary>
/// Validates SQL queries using an AST visitor to ensure they are strictly read-only and contain no mutating commands.
/// </summary>
public sealed class ReadOnlyGuard : IReadOnlyGuard
{
    private static readonly HashSet<Type> MutatingFragmentTypes =
    [
        typeof(DataModificationStatement),
        typeof(DataModificationSpecification),
        typeof(InsertStatement),
        typeof(InsertSpecification),
        typeof(UpdateStatement),
        typeof(UpdateSpecification),
        typeof(DeleteStatement),
        typeof(DeleteSpecification),
        typeof(MergeStatement),
        typeof(MergeSpecification),
        typeof(TruncateTableStatement),
        typeof(ExecuteStatement),
        typeof(ExecuteSpecification),
        typeof(CreateTableStatement),
        typeof(AlterTableStatement),
        typeof(DropTableStatement),
        typeof(DropObjectsStatement),
        typeof(CreateIndexStatement),
        typeof(AlterIndexStatement),
        typeof(DropIndexStatement),
        typeof(CreateViewStatement),
        typeof(AlterViewStatement),
        typeof(DropViewStatement),
        typeof(CreateProcedureStatement),
        typeof(AlterProcedureStatement),
        typeof(DropProcedureStatement),
        typeof(CreateFunctionStatement),
        typeof(AlterFunctionStatement),
        typeof(DropFunctionStatement),
        typeof(CreateTriggerStatement),
        typeof(AlterTriggerStatement),
        typeof(DropTriggerStatement),
        typeof(CreateSchemaStatement),
        typeof(AlterSchemaStatement),
        typeof(DropSchemaStatement),
        typeof(CreateDatabaseStatement),
        typeof(AlterDatabaseStatement),
        typeof(DropDatabaseStatement),
        typeof(CreateTypeStatement),
        typeof(DropTypeStatement),
        typeof(CreateSequenceStatement),
        typeof(AlterSequenceStatement),
        typeof(DropSequenceStatement),
        typeof(SecurityStatement),
        typeof(GrantStatement),
        typeof(DenyStatement),
        typeof(RevokeStatement),
        typeof(CreateRoleStatement),
        typeof(AlterRoleStatement),
        typeof(DropRoleStatement),
        typeof(CreateUserStatement),
        typeof(AlterUserStatement),
        typeof(DropUserStatement),
        typeof(CreateLoginStatement),
        typeof(AlterLoginStatement),
        typeof(DropLoginStatement),
        typeof(BackupStatement),
        typeof(RestoreStatement),
        typeof(CheckpointStatement),
        typeof(ReconfigureStatement),
        typeof(DbccStatement),
        typeof(ShutdownStatement),
        typeof(KillStatement)
    ];

    /// <summary>
    /// Checks if a query is safe for read-only execution by parsing it into an AST and verifying
    /// that no mutating, DDL, administrative, or procedure-execution statements are present.
    /// </summary>
    /// <param name="query">The SQL query string to evaluate.</param>
    /// <returns>True if the query is safe (read-only); otherwise, false.</returns>
    public bool IsQuerySafe(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var parseResult = SqlScriptDomParser.Parse(query);
        if (parseResult.Fragment is null)
        {
            return false;
        }

        var visitor = new ReadOnlyStatementVisitor();
        parseResult.Fragment.Accept(visitor);
        if (!visitor.IsSafe)
        {
            return false;
        }

        // If parse errors occurred (e.g. unclosed statements or missing MERGE semicolon),
        // inspect the lexer token stream for any mutating keyword tokens.
        if (parseResult.Errors.Count > 0 && parseResult.Fragment.ScriptTokenStream is not null)
        {
            if (ContainsMutatingTokens(parseResult.Fragment.ScriptTokenStream))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsMutatingTokens(IList<TSqlParserToken> tokens)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (IsMutatingTokenType(token.TokenType))
            {
                if (token.TokenType is TSqlTokenType.Execute or TSqlTokenType.Exec && IsExecuteAsSequence(tokens, i))
                {
                    continue;
                }

                return true;
            }

            if (token.TokenType is TSqlTokenType.Identifier
                && (token.Text.Equals("sp_executesql", StringComparison.OrdinalIgnoreCase)
                    || token.Text.Equals("sys.sp_executesql", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExecuteAsSequence(IList<TSqlParserToken> tokens, int execIndex)
    {
        for (int j = execIndex + 1; j < tokens.Count; j++)
        {
            var next = tokens[j];
            if (next.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            {
                continue;
            }

            return next.TokenType is TSqlTokenType.As;
        }

        return false;
    }

    private static bool IsMutatingTokenType(TSqlTokenType type)
    {
        return type is TSqlTokenType.Insert
            or TSqlTokenType.Update
            or TSqlTokenType.Delete
            or TSqlTokenType.Merge
            or TSqlTokenType.Truncate
            or TSqlTokenType.Drop
            or TSqlTokenType.Alter
            or TSqlTokenType.Create
            or TSqlTokenType.Exec
            or TSqlTokenType.Execute
            or TSqlTokenType.Grant
            or TSqlTokenType.Revoke
            or TSqlTokenType.Deny
            or TSqlTokenType.Backup
            or TSqlTokenType.Restore
            or TSqlTokenType.Reconfigure
            or TSqlTokenType.Checkpoint
            or TSqlTokenType.Kill
            or TSqlTokenType.Shutdown
            or TSqlTokenType.Dbcc;
    }

    private sealed class ReadOnlyStatementVisitor : TSqlFragmentVisitor
    {
        public bool IsSafe { get; private set; } = true;

        public override void Visit(TSqlFragment node)
        {
            if (IsMutatingFragment(node))
            {
                IsSafe = false;
            }
        }

        private static bool IsMutatingFragment(TSqlFragment node)
        {
            if (node is SelectStatement select)
            {
                return select.Into is not null;
            }

            return IsRegisteredMutatingType(node.GetType());
        }

        private static bool IsRegisteredMutatingType(Type type)
        {
            for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                if (MutatingFragmentTypes.Contains(current))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
