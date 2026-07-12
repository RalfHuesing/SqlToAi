#nullable enable

namespace SqlToAi.Domain;

/// <summary>
/// Represents a standardized error in the SqlToAi system.
/// </summary>
public sealed record SqlToAiError(string Code, string Message)
{
    // Error catalog codes according to mcp-specification.md
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

    public static SqlToAiError InvalidParameters(string details) =>
        new(InvalidParametersCode, $"Ungültige Parameter: {details}");

    public static SqlToAiError MultipleStatementsForbidden() =>
        new(MultipleStatementsForbiddenCode, "Die Ausführung von mehreren SQL-Statements (z. B. getrennt durch ';') ist nicht erlaubt.");

    public static SqlToAiError QueryError(string message) =>
        new(QueryErrorCode, $"Abfragefehler: {message}");

    public static SqlToAiError ObjectNotFound(string objectName) =>
        new(ObjectNotFoundCode, $"Objekt nicht gefunden: Das angeforderte Datenbankobjekt '{objectName}' existiert nicht.");

    public static SqlToAiError SafetyCheckFailed(string details) =>
        new(SafetyCheckFailedCode, $"Safety-Check fehlgeschlagen: {details}");

    public static SqlToAiError InfrastructureError(string message) =>
        new(InfrastructureErrorCode, $"Infrastrukturfehler: {message}");

    public static SqlToAiError Timeout() =>
        new(TimeoutCode, "Die Ausführung der SQL-Abfrage hat das konfigurierte Zeitlimit überschritten.");

    public static SqlToAiError WriteOperationBlocked() =>
        new(WriteOperationBlockedCode, "Schreiboperation blockiert: Ein mutierendes Statement wurde im Read-Only-Modus abgewiesen oder der Zugriff auf Datenabfragen wurde durch das Access-Level SchemaOnly blockiert.");

    public static SqlToAiError WriteOperationBlocked(string details) =>
        new(WriteOperationBlockedCode, $"Schreiboperation blockiert: {details}");

    public static SqlToAiError InvalidReferenceType(string objectName) =>
        new(InvalidReferenceTypeCode, $"Ungültiger Typ für Referenzen: Objektreferenzen können nur für Tabellen und Sichten abgefragt werden. Objekt: {objectName}");

    public static SqlToAiError InvalidParameterType(string objectName) =>
        new(InvalidParameterTypeCode, $"Ungültiger Typ für Parameter: Routine-Parameter können nur für Prozeduren und Funktionen gelesen werden. Objekt: {objectName}");
}
