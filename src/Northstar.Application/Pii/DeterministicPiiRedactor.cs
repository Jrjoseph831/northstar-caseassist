using System.Text.RegularExpressions;

namespace Northstar.Application.Pii;

public interface IPiiRedactor
{
    /// <summary>Redacts every supported identifier category.</summary>
    RedactionResult Redact(string input);

    /// <summary>
    /// Redacts only the given identifier categories, leaving others in place. Used to enforce
    /// the data-handling policy: prohibited categories are removed before an AI service sees
    /// the text, while policy-permitted categories are retained.
    /// </summary>
    RedactionResult Redact(string input, IReadOnlySet<string> categories);
}

public sealed record RedactionCount(string EntityType, int Count);

public sealed record RedactionResult(string RedactedText, IReadOnlyList<RedactionCount> Summary)
{
    public int TotalRedactions => Summary.Sum(item => item.Count);
}

/// <summary>
/// Code enforcement of the Data Handling and Retention Standard, section 4.2 (source id
/// <c>DHR-4.2</c>). The policy document names which identifiers may be included in AI-assistant
/// processing and which must never be transmitted to an AI service; these sets keep the code
/// and the published policy in lockstep.
/// </summary>
public static class DataHandlingPolicy
{
    public const string SourceId = "DHR-4.2";

    // Never transmitted to an AI service.
    public static readonly IReadOnlySet<string> ProhibitedFromAi =
        new HashSet<string>(StringComparer.Ordinal) { "SSN", "STREET_ADDRESS", "PERSON" };

    // May be included in AI-assistant processing when relevant to the task.
    public static readonly IReadOnlySet<string> PermittedForAi =
        new HashSet<string>(StringComparer.Ordinal) { "EMAIL", "PHONE", "CASE_IDENTIFIER" };
}

public sealed partial class DeterministicPiiRedactor : IPiiRedactor
{
    private static readonly IReadOnlySet<string> AllCategories =
        new HashSet<string>(StringComparer.Ordinal)
        { "EMAIL", "SSN", "PHONE", "CASE_IDENTIFIER", "STREET_ADDRESS", "PERSON" };

    public RedactionResult Redact(string input) => Redact(input, AllCategories);

    public RedactionResult Redact(string input, IReadOnlySet<string> categories)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(categories);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var redacted = input;
        if (categories.Contains("EMAIL")) redacted = Replace(EmailPattern(), redacted, "[REDACTED_EMAIL]", "EMAIL", counts);
        if (categories.Contains("SSN")) redacted = Replace(SsnPattern(), redacted, "[REDACTED_SSN]", "SSN", counts);
        if (categories.Contains("PHONE")) redacted = Replace(PhonePattern(), redacted, "[REDACTED_PHONE]", "PHONE", counts);
        if (categories.Contains("CASE_IDENTIFIER")) redacted = Replace(CaseIdentifierPattern(), redacted, "[REDACTED_CASE_ID]", "CASE_IDENTIFIER", counts);
        if (categories.Contains("STREET_ADDRESS")) redacted = Replace(StreetAddressPattern(), redacted, "[REDACTED_ADDRESS]", "STREET_ADDRESS", counts);
        if (categories.Contains("PERSON")) redacted = Replace(LabeledPersonPattern(), redacted, "[REDACTED_PERSON]", "PERSON", counts);

        var summary = counts
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new RedactionCount(item.Key, item.Value))
            .ToArray();

        return new RedactionResult(redacted, summary);
    }

    private static string Replace(
        Regex pattern,
        string input,
        string replacement,
        string entityType,
        IDictionary<string, int> counts)
    {
        return pattern.Replace(input, _ =>
        {
            counts.TryGetValue(entityType, out var currentCount);
            counts[entityType] = currentCount + 1;
            return replacement;
        });
    }

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)\d{3}-\d{2}-\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex SsnPattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?1[ .-]?)?(?:\(\d{3}\)|\d{3})[ .-]\d{3}[ .-]\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"\b(?:NS|APP)-\d{4}(?:-\d{4})?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CaseIdentifierPattern();

    [GeneratedRegex(@"\b\d{1,5}\s+(?:[A-Z][A-Za-z.-]*\s+){1,4}(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln)\b(?:,?\s+[A-Z][A-Za-z.-]+){0,3}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StreetAddressPattern();

    [GeneratedRegex(@"(?<=\bApplicant\s)[A-Z][a-z]+(?:\s+[A-Z][a-z]+){1,2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex LabeledPersonPattern();
}
