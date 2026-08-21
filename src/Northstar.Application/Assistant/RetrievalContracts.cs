namespace Northstar.Application.Assistant;

/// <summary>
/// An approved policy section pulled from the corpus before ranking. <see cref="SourceScore"/>
/// is whatever score the candidate store itself produced (the search service's own relevance,
/// zero when the store has no opinion); the ranking stages score it again.
/// </summary>
public sealed record PolicyCandidate(
    string SourceId,
    string DocumentTitle,
    string DocumentVersion,
    string SectionLabel,
    string Content,
    double SourceScore)
{
    /// <summary>The text that is indexed and ranked for this section.</summary>
    public string SearchableText => $"{DocumentTitle} {SectionLabel} {Content}";
}

/// <summary>Supplies the candidate pool the ranking stages work over.</summary>
public interface IPolicyCandidateSource
{
    string Name { get; }

    Task<IReadOnlyList<PolicyCandidate>> FetchAsync(
        string programCode,
        IReadOnlyList<string> queries,
        int take,
        CancellationToken cancellationToken = default);
}

/// <summary>Dense (vector) representation of a query or passage.</summary>
public interface IEmbeddingProvider
{
    string ModelName { get; }
    int Dimensions { get; }
    bool IsLive { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}

/// <summary>Generates the similar queries that are searched alongside the original question.</summary>
public interface IQueryExpander
{
    string Name { get; }
    bool IsLive { get; }

    Task<IReadOnlyList<string>> ExpandAsync(
        string programCode,
        string question,
        int count,
        CancellationToken cancellationToken = default);
}

public sealed record RerankedCandidate(PolicyCandidate Candidate, double Score);

/// <summary>Token-level (late-interaction) scoring applied to the fused shortlist.</summary>
public interface ILateInteractionReranker
{
    string ModelName { get; }

    /// <param name="queryTermWeights">
    /// Optional per-term importance (inverse document frequency) so that a match on a rare,
    /// discriminating word counts for more than a match on a word every section contains.
    /// </param>
    IReadOnlyList<RerankedCandidate> Rerank(
        string question,
        IReadOnlyList<PolicyCandidate> candidates,
        IReadOnlyDictionary<string, double>? queryTermWeights = null);
}

/// <summary>
/// Per-section scores from each retrieval stage, kept for the safety trace. The dense and
/// sparse ranks are for the caseworker's own wording; the fusion score covers every generated
/// query, so a section can rank modestly on the original question and still fuse to the top.
/// </summary>
public sealed record RetrievalStageScore(
    string SourceId,
    int DenseRank,
    double DenseScore,
    int SparseRank,
    double SparseScore,
    int FusionRank,
    double FusionScore,
    double RerankScore,
    int FinalRank);

/// <summary>Everything an auditor needs to see how a set of sources was chosen.</summary>
public sealed record RetrievalDiagnostics(
    string Strategy,
    string CandidateSource,
    IReadOnlyList<string> Queries,
    int CandidateCount,
    string DenseModel,
    int DenseDimensions,
    bool DenseIsLive,
    string SparseModel,
    string RerankModel,
    string QueryExpansionProvider,
    bool QueryExpansionIsLive,
    int FusionConstant,
    int RerankDepth,
    IReadOnlyList<RetrievalStageScore> Ranking,
    long ElapsedMilliseconds);

public sealed record PolicyRetrievalResult(
    IReadOnlyList<PolicyHit> Hits,
    RetrievalDiagnostics Diagnostics);
