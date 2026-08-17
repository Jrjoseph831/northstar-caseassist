using System.Text.RegularExpressions;

namespace Northstar.Application.Assistant;

public sealed partial class PromptInjectionDetector
{
    public IReadOnlyList<string> Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reasons = new List<string>();
        if (IgnoreInstructionsPattern().IsMatch(text)) reasons.Add("INJECTION_IGNORE_INSTRUCTIONS");
        if (SystemPromptPattern().IsMatch(text)) reasons.Add("INJECTION_SYSTEM_PROMPT");
        if (RoleOverridePattern().IsMatch(text)) reasons.Add("INJECTION_ROLE_OVERRIDE");
        if (SecretExfiltrationPattern().IsMatch(text)) reasons.Add("INJECTION_SECRET_EXFILTRATION");
        return reasons;
    }

    [GeneratedRegex(@"\b(ignore|disregard|forget)\b.{0,40}\b(previous|prior|above|system)\b.{0,20}\b(instruction|message|rule)s?\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex IgnoreInstructionsPattern();

    [GeneratedRegex(@"(<\s*system\s*>|\bsystem\s*(prompt|message)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SystemPromptPattern();

    [GeneratedRegex(@"\b(you are now|act as|switch roles?|developer message|assistant:)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleOverridePattern();

    [GeneratedRegex(@"\b(reveal|print|return|extract)\b.{0,40}\b(secret|token|credential|api key|hidden prompt)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SecretExfiltrationPattern();
}

public sealed partial class RiskClassifier
{
    public RiskAssessment Classify(
        string requestText,
        string outputText,
        bool citationsValid,
        bool outputContainsPii,
        IReadOnlyList<string> injectionReasons)
    {
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        if (EligibilityPattern().IsMatch(requestText) || OutputDecisionPattern().IsMatch(outputText)) reasons.Add("PROTECTED_ELIGIBILITY_DECISION");
        if (PaymentPattern().IsMatch(requestText) || OutputPaymentPattern().IsMatch(outputText)) reasons.Add("PAYMENT_AUTHORIZATION");
        if (ClosurePattern().IsMatch(requestText)) reasons.Add("CASE_CLOSURE");
        if (ContactPattern().IsMatch(requestText)) reasons.Add("AUTONOMOUS_APPLICANT_CONTACT");
        if (ConflictPattern().IsMatch(requestText)) reasons.Add("CONFLICTING_EVIDENCE");
        if (!citationsValid) reasons.Add("CITATION_VALIDATION_FAILED");
        if (outputContainsPii) reasons.Add("OUTPUT_PII_DETECTED");
        foreach (var reason in injectionReasons) reasons.Add(reason);

        var ordered = reasons.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        return new RiskAssessment(ordered.Length == 0 ? "Low" : "High", ordered.Length > 0, ordered);
    }

    [GeneratedRegex(@"\b(approve|approved|deny|denied|eligible|ineligible)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EligibilityPattern();

    [GeneratedRegex(@"\b(applicant|case|request)\b.{0,30}\b(is eligible|is ineligible|should be approved|should be denied|is approved|is denied)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex OutputDecisionPattern();

    [GeneratedRegex(@"\b(authori[sz]e|issue|set|determine)\b.{0,30}\b(payment|benefit amount|award)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex PaymentPattern();

    [GeneratedRegex(@"\b(payment|benefit|award)\b.{0,25}\b(is authorized|is approved|amount is|equals \$)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex OutputPaymentPattern();

    [GeneratedRegex(@"\b(close|closed|terminate)\b.{0,20}\b(case|application)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ClosurePattern();

    [GeneratedRegex(@"\b(send|email|call|contact)\b.{0,25}\b(applicant|client|resident)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ContactPattern();

    [GeneratedRegex(@"\b(conflict|conflicting|discrepancy|mismatch)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConflictPattern();
}

public sealed class CitationValidator
{
    public CitationValidationResult Validate(ModelGeneration generation, IReadOnlyList<PolicyHit> retrievedSources)
    {
        var retrievedIds = retrievedSources.Select(item => item.SourceId).ToHashSet(StringComparer.Ordinal);
        var reasons = new List<string>();
        if (generation.CitationSourceIds.Count == 0) reasons.Add("NO_CITATIONS");
        foreach (var sourceId in generation.CitationSourceIds.Distinct(StringComparer.Ordinal))
        {
            var source = retrievedSources.SingleOrDefault(item => item.SourceId == sourceId);
            if (!retrievedIds.Contains(sourceId)) reasons.Add($"SOURCE_NOT_RETRIEVED:{sourceId}");
            if (!generation.Text.Contains($"[{sourceId}]", StringComparison.Ordinal)) reasons.Add($"CITATION_NOT_DISPLAYED:{sourceId}");
            if (source is not null && !generation.Text.Contains(source.Content, StringComparison.Ordinal)) reasons.Add($"EXCERPT_NOT_PRESENT:{sourceId}");
        }

        return new CitationValidationResult(reasons.Count == 0, reasons);
    }
}
