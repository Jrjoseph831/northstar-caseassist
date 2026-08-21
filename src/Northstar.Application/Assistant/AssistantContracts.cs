namespace Northstar.Application.Assistant;

public sealed record PolicyHit(
    string SourceId,
    string DocumentTitle,
    string DocumentVersion,
    string SectionLabel,
    string Content,
    double Score);

/// <summary>
/// Selects the approved policy sections an answer may be grounded in. Implementations return
/// both the chosen sections and a per-stage account of how they were chosen, which is what the
/// safety trace shows an auditor.
/// </summary>
public interface IPolicyRetriever
{
    Task<PolicyRetrievalResult> SearchAsync(
        string programCode,
        string redactedQuestion,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed record ModelRequest(
    string RequestId,
    string RedactedQuestion,
    string ProgramCode,
    string CaseContext,
    IReadOnlyList<PolicyHit> Sources,
    string PromptVersion,
    int MaximumOutputTokens);

public sealed record ModelGeneration(
    string Text,
    IReadOnlyList<string> CitationSourceIds,
    int TokenUsage,
    decimal EstimatedCost,
    string Provider,
    string ModelName,
    bool IsLive,
    AssistantDraft? Draft = null);

/// <summary>
/// Structured, render-ready view of an assistant answer. The flattened
/// <see cref="ModelGeneration.Text"/> remains the source of truth for the safety
/// pipeline (citation validation, PII scan, risk classification); this record only
/// shapes the same content for the UI.
/// </summary>
public sealed record AssistantDraft(
    string Summary,
    IReadOnlyList<AssistantMissingDocument> MissingDocuments,
    string? HandlingNote);

public sealed record AssistantMissingDocument(
    string Title,
    string Detail,
    string? SourceId);

public interface IAssistantModelProvider
{
    Task<ModelGeneration> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default);
}

public sealed record ContentSafetyCategory(string Category, int Severity);

public sealed record ContentSafetyAnalysis(
    string Provider,
    bool IsLive,
    bool IsAllowed,
    IReadOnlyList<ContentSafetyCategory> Categories,
    IReadOnlyList<string> ReasonCodes);

public interface IContentSafetyScanner
{
    Task<ContentSafetyAnalysis> AnalyzeAsync(
        string text,
        CancellationToken cancellationToken = default);
}

public sealed record CitationValidationResult(bool IsValid, IReadOnlyList<string> ReasonCodes);

public sealed record RiskAssessment(string Classification, bool RequiresReview, IReadOnlyList<string> ReasonCodes);
