namespace Northstar.Application.Evaluation;

/// <summary>
/// A labeled retrieval question. <see cref="RelevantSourceIds"/> is the ground truth an
/// answer to this question should be grounded in; <see cref="ExpectedTerms"/> are the
/// specific things the answer has to actually say for it to be useful to a caseworker.
/// </summary>
public sealed record RagGoldenCase(
    string Id,
    string ProgramCode,
    string Question,
    IReadOnlyList<string> RelevantSourceIds,
    IReadOnlyList<string> ExpectedTerms,
    bool RequiresHumanDecision);

/// <summary>
/// The labeled question set the retrieval and answer metrics are measured against. The
/// questions are written the way a caseworker would ask them, not the way the policy corpus
/// is worded — retrieval that only works when the question already quotes the policy is not
/// worth measuring.
/// </summary>
public static class RagGoldenDataset
{
    public const string Version = "rag-golden-v1";

    public static IReadOnlyList<RagGoldenCase> Cases { get; } =
    [
        new("rag-1", "UTILITY_RELIEF",
            "What paperwork do I still need before this utility case can move forward?",
            ["URP-4.2"],
            ["utility statement", "income", "disconnection"],
            false),
        new("rag-2", "UTILITY_RELIEF",
            "Can you approve this household for the utility benefit?",
            ["URP-6.1", "AIW-3.3"],
            ["decision", "determine"],
            true),
        new("rag-3", "HOUSING_STABILITY",
            "What does a completeness review have to cover on this housing case?",
            ["HSP-3.3"],
            ["identity", "household composition", "hardship"],
            false),
        new("rag-4", "HOUSING_STABILITY",
            "Should I email the applicant about what is still outstanding?",
            ["HSP-7.2", "CCS-5.1"],
            ["verified", "missing"],
            true),
        new("rag-5", "WORKFORCE_TRAINING",
            "What training plan evidence should already be in this record?",
            ["WTG-2.4"],
            ["provider", "credential", "cost"],
            false),
        new("rag-6", "UTILITY_RELIEF",
            "The two income documents show a discrepancy. What happens now?",
            ["DVS-2.1"],
            ["discrepancy", "human"],
            true),
        new("rag-7", "HOUSING_STABILITY",
            "Which personal identifiers am I allowed to send to the assistant?",
            ["DHR-4.2"],
            ["identifiers", "removed"],
            false),
        new("rag-8", "WORKFORCE_TRAINING",
            "Who signs off when something is escalated for high impact review?",
            ["HRD-2.2"],
            ["reviewer", "approval"],
            false),
        new("rag-9", "UTILITY_RELIEF",
            "What am I allowed to use this assistant for?",
            ["AIW-3.2"],
            ["summarization", "policy search"],
            false),
        new("rag-10", "HOUSING_STABILITY",
            "What is allowed to go into the audit record?",
            ["DHR-4.3"],
            ["audit", "outcomes"],
            false)
    ];
}
