#nullable enable

namespace SqlToAi.Tests.TestSupport;

/// <summary>
/// One column inside a <c>&lt;ColumnGroup&gt;</c> element of a ShowPlan XML document. Extracted
/// from <see cref="ShowPlanTestHelper"/> to a namespace-level type so the LLM-friendly file
/// listing (AiNetLinter <c>BanPublicNestedTypes</c>) keeps the type visible on its own — and so
/// consumers can name it directly without the <c>ShowPlanTestHelper.</c> prefix
/// (step-003 / DRY-T2).
/// </summary>
internal sealed record ColumnSpec(string Name, string Usage, bool? Descending = null);
