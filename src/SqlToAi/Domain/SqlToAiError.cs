#nullable enable

namespace SqlToAi.Domain;

/// <summary>
/// Represents a standardized error in the SqlToAi system.
/// </summary>
public sealed record SqlToAiError(string Code, string Message)
{
    // Error catalog codes according to architecture-spec.md
    internal const string InvalidParametersCode = "SQL-AI-0001";
    internal const string MultipleStatementsForbiddenCode = "SQL-AI-0101";
    internal const string QueryErrorCode = "SQL-AI-0102";
    internal const string ObjectNotFoundCode = "SQL-AI-0103";
    internal const string SafetyCheckFailedCode = "SQL-AI-0104";
    internal const string InfrastructureErrorCode = "SQL-AI-0105";
    internal const string TimeoutCode = "SQL-AI-0106";
    internal const string WriteOperationBlockedCode = "SQL-AI-0107";
    internal const string InvalidReferenceTypeCode = "SQL-AI-0108";
    internal const string InvalidParameterTypeCode = "SQL-AI-0109";
    internal const string InvalidDetailQueryTypeCode = "SQL-AI-0110";

    public static SqlToAiError InvalidParameters(string details) =>
        new(InvalidParametersCode, $"Invalid parameters: {details}");

    public static SqlToAiError MultipleStatementsForbidden() =>
        new(MultipleStatementsForbiddenCode, "Execution of multiple main SQL statements or multi-batch scripts (e.g. 'GO' or multiple SELECTs) is not allowed. Only a single main query (with optional DECLARE/SET preamble) is permitted.");

    public static SqlToAiError QueryError(string message) =>
        new(QueryErrorCode, $"Query error: {message}");

    public static SqlToAiError ObjectNotFound(string objectName) =>
        new(ObjectNotFoundCode, $"Object not found: The requested database object '{objectName}' does not exist.");

    public static SqlToAiError SafetyCheckFailed(string details) =>
        new(SafetyCheckFailedCode, $"Safety check failed: {details}");

    public static SqlToAiError InfrastructureError(string message) =>
        new(InfrastructureErrorCode, $"Infrastructure error: {message}");

    public static SqlToAiError Timeout() =>
        new(TimeoutCode, "The SQL query execution exceeded the configured time limit.");

    public static SqlToAiError WriteOperationBlocked() =>
        new(WriteOperationBlockedCode, "Write operation blocked: A mutating statement was rejected in read-only mode, or data query access was blocked by the SchemaOnly access level.");

    public static SqlToAiError WriteOperationBlocked(string details) =>
        new(WriteOperationBlockedCode, $"Write operation blocked: {details}");

    public static SqlToAiError InvalidReferenceType(string objectName) =>
        new(InvalidReferenceTypeCode, $"Invalid type for references: Object references can only be queried for tables and views. Object: {objectName}");

    public static SqlToAiError InvalidParameterType(string objectName) =>
        new(InvalidParameterTypeCode, $"Invalid type for parameters: Routine parameters can only be read for procedures and functions. Object: {objectName}");

    public static SqlToAiError InvalidDetailQueryType(string objectName) =>
        new(InvalidDetailQueryTypeCode, $"Invalid type for detail query: Foreign keys, indexes, and constraints can only be queried for tables and views. Object: {objectName}");
}
