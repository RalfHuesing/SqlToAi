#nullable enable

using System.Collections.Generic;

namespace SqlToAi.Domain;

/// <summary>
/// Contains the query output data along with details of any anonymization applied.
/// </summary>
public sealed record QueryExecutionResult(
    string Data,
    bool WasAnonymized,
    IReadOnlyList<string> AnonymizedColumns,
    string AnonymizationMode);
