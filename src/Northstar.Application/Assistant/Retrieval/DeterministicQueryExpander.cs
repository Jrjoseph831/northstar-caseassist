namespace Northstar.Application.Assistant.Retrieval;

/// <summary>
/// Generates the similar queries that are searched alongside the caseworker's original
/// wording. Caseworkers ask in caseworker language ("what else do I need from them?"); the
/// policy corpus is written in policy language ("required documents", "completeness review").
/// One search of the original phrasing misses sections that a rephrasing would have found,
/// so each variant is searched and the results are fused.
///
/// This implementation is rule-based, so it costs nothing, adds no latency and returns the
/// same variants for the same question — which is what makes an audited retrieval trace
/// reproducible. A hosted model can generate the variants instead; see the retrieval
/// configuration.
/// </summary>
public sealed class DeterministicQueryExpander : IQueryExpander
{
    public string Name => "deterministic-rule-based-v1";

    public bool IsLive => false;

    private static readonly Dictionary<string, string> ProgramVocabulary = new(StringComparer.Ordinal)
    {
        ["UTILITY_RELIEF"] = "utility statement household income evidence disconnection notice required documents",
        ["HOUSING_STABILITY"] = "identity documentation housing obligation household composition hardship documentation completeness review",
        ["WORKFORCE_TRAINING"] = "training provider program dates expected credential itemized cost estimate"
    };

    private const string DefaultVocabulary = "approved program requirements documentation";

    private static readonly (string[] Triggers, string Vocabulary)[] Intents =
    [
        (["approve", "approval", "deny", "denial", "eligible", "eligibility", "determine", "determination", "decision", "payment", "authorize", "close", "closure"],
            "decision authority eligibility determination benefit approval prohibited uses authorized decision maker"),
        (["missing", "document", "documents", "outstanding", "needed", "required", "requirement", "checklist", "complete", "completeness", "verify", "evidence"],
            "required documents completeness review document gap evidence on file verification"),
        (["draft", "email", "message", "letter", "contact", "notify", "communicate", "write", "reply", "respond"],
            "applicant communication drafting boundaries verified facts missing information caseworker review before use"),
        (["conflict", "conflicting", "discrepancy", "mismatch", "inconsistent", "differ", "disagree"],
            "conflicting evidence discrepancy recorded routed for human resolution"),
        (["identifier", "identifiers", "redact", "redaction", "privacy", "retention", "ssn", "address", "personal", "share", "send", "transmit"],
            "minimum necessary data identifiers permitted prohibited removed before processing retention"),
        (["review", "reviewer", "approver", "separation", "duties", "escalate", "oversight", "submit"],
            "human review separation of duties assigned reviewer approval authority"),
        (["audit", "log", "logged", "trace", "trail", "record", "records"],
            "audit records identifiers control outcomes counts credentials excluded"),
        (["allowed", "permitted", "use", "uses", "using", "assistant", "tool", "appropriate", "supposed", "help"],
            "approved uses of the assistant policy search case summarization document completeness checks communication drafts prohibited uses")
    ];

    public Task<IReadOnlyList<string>> ExpandAsync(
        string programCode,
        string question,
        int count,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> variants = Expand(programCode, question, count);
        return Task.FromResult(variants);
    }

    public static IReadOnlyList<string> Expand(string programCode, string question, int count)
    {
        if (count <= 0 || string.IsNullOrWhiteSpace(question)) return [];

        var keywords = TextAnalysis.Tokenize(question)
            .Where(token => !TextAnalysis.IsStopWord(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var keywordQuery = string.Join(' ', keywords);
        var programTerms = ProgramVocabulary.GetValueOrDefault(programCode, DefaultVocabulary);
        var questionStems = keywords.Select(TextAnalysis.Stem).ToHashSet(StringComparer.Ordinal);
        var intentTerms = Intents
            .Where(intent => intent.Triggers.Any(trigger => questionStems.Contains(TextAnalysis.Stem(trigger))))
            .Select(intent => intent.Vocabulary)
            .Take(2)
            .ToArray();

        var candidates = new List<string>
        {
            $"{keywordQuery} {programTerms}".Trim()
        };
        foreach (var intent in intentTerms)
        {
            candidates.Add($"{keywordQuery} {intent}".Trim());
        }
        if (intentTerms.Length > 0)
        {
            candidates.Add($"{intentTerms[0]} {programTerms}".Trim());
        }
        candidates.Add(keywordQuery);
        candidates.Add($"{programTerms} {DefaultVocabulary}".Trim());

        var expanded = new List<string>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (candidate.Equals(question.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if (expanded.Contains(candidate, StringComparer.OrdinalIgnoreCase)) continue;
            expanded.Add(candidate);
            if (expanded.Count == count) break;
        }
        return expanded;
    }
}
