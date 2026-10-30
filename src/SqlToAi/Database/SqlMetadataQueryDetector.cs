using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlToAi.Database;

internal static class SqlMetadataQueryDetector
{
    private static readonly HashSet<string> CatalogViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "all_columns",
        "all_objects",
        "all_parameters",
        "check_constraints",
        "columns",
        "computed_columns",
        "default_constraints",
        "extended_properties",
        "foreign_key_columns",
        "foreign_keys",
        "index_columns",
        "indexes",
        "key_constraints",
        "objects",
        "parameters",
        "procedures",
        "schemas",
        "sql_expression_dependencies",
        "sql_modules",
        "synonyms",
        "table_types",
        "tables",
        "triggers",
        "types",
        "user_types",
        "views"
    };

    private static readonly HashSet<string> InformationSchemaViews = new(StringComparer.OrdinalIgnoreCase)
    {
        "CHECK_CONSTRAINTS",
        "COLUMN_DOMAIN_USAGE",
        "COLUMN_PRIVILEGES",
        "COLUMNS",
        "CONSTRAINT_COLUMN_USAGE",
        "CONSTRAINT_TABLE_USAGE",
        "KEY_COLUMN_USAGE",
        "PARAMETERS",
        "REFERENTIAL_CONSTRAINTS",
        "ROUTINES",
        "SCHEMATA",
        "TABLE_CONSTRAINTS",
        "TABLE_PRIVILEGES",
        "TABLES",
        "VIEW_COLUMN_USAGE",
        "VIEW_TABLE_USAGE",
        "VIEWS"
    };

    public static bool ContainsSchemaMetadataReference(string? query)
    {
        SqlScriptParseResult parsed = SqlScriptDomParser.ParseScript(query);
        if (!parsed.Success)
        {
            return false;
        }

        var visitor = new MetadataReferenceVisitor();
        parsed.Script!.Accept(visitor);
        return visitor.Found;
    }

    public static string? GetResponseHint(string? query)
    {
        return ContainsSchemaMetadataReference(query)
            ? "Schema hint: Direct catalog results may be anonymized. Use sql_get_schema for columns and view/routine definitions, sql_get_trigger_definition for triggers, or sql_search_objects to find object names."
            : null;
    }

    private sealed class MetadataReferenceVisitor : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }

        public override void Visit(NamedTableReference node)
        {
            SchemaObjectName? name = node.SchemaObject;
            if (name is not null)
            {
                string? schema = name.SchemaIdentifier?.Value;
                string? objectName = name.BaseIdentifier?.Value;
                if (IsCatalogView(schema, objectName))
                {
                    Found = true;
                }
            }
        }

        public override void Visit(FunctionCall node)
        {
            if (node.CallTarget is null
                && string.Equals(node.FunctionName?.Value, "OBJECT_DEFINITION", StringComparison.OrdinalIgnoreCase))
            {
                Found = true;
            }
        }

        private static bool IsCatalogView(string? schema, string? objectName)
        {
            return (string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase)
                    && objectName is not null
                    && CatalogViews.Contains(objectName))
                || (string.Equals(schema, "INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase)
                    && objectName is not null
                    && InformationSchemaViews.Contains(objectName));
        }
    }
}
