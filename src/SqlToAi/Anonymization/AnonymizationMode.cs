#nullable enable

namespace SqlToAi.Anonymization;

/// <summary>
/// Canonical mode values accepted by <see cref="Anonymizer"/>. The strings are part
/// of the <c>AnonymizerOptions.DefaultMode</c> contract; both values are referenced
/// from <see cref="Anonymizer"/> so the on/off selection is grep-able in one place.
/// </summary>
internal static class AnonymizationMode
{
    public const string Hash = "Hash";
    public const string Scramble = "Scramble";
}
