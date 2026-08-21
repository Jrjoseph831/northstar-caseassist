using System.Diagnostics;

namespace Northstar.Application.Assistant.Retrieval;

public sealed record HybridRetrievalOptions
{
    /// <summary>Similar queries generated alongside the caseworker's original wording.</summary>
    public int QueryVariants { get; init; } = 4;

    /// <summary>How many approved sections are pulled into the pool before ranking.</summary>
    public int CandidatePoolSize { get; init; } = 24;

    /// <summary>How deep into the fused list the late-interaction reranker runs.</summary>
    public int RerankDepth { get; init; } = 8;

    public int FusionConstant { get; init; } = ReciprocalRankFusion.DefaultConstant;

    /// <summary>
    /// Share of the final score taken from the reranker; the rest comes from fusion. Fusion
    /// leads deliberately: on the current corpus, sweeping this weight over the labeled
    /// question set showed recall and MRR falling once the reranker outvoted the agreement
    /// between searches. Reranking still reorders the shortlist, it just does not overrule it.
    /// </summary>
    public double RerankWeight { get; init; } = 0.25;

    public bool UseQueryExpansion { get; init; } = true;

    public bool UseReranking { get; init; } = true;
}

/// <summary>
/// The retrieval pipeline behind every CaseAssist answer.
///
/// 1. Expand the question into several similar queries.
/// 2. Search each query two ways: dense vectors (meaning) and BM25 sparse vectors (exact terms).
/// 3. Fuse every ranked list with Reciprocal Rank Fusion.
/// 4. Rerank the fused shortlist with late-interaction MaxSim scoring.
/// 5. Return the top sections, with per-stage scores for the safety trace.
///
/// Only approved sections for the case's program (or corpus-wide sections) ever enter the
/// pool, so the ranking stages can reorder evidence but can never introduce an unapproved
/// source.
/// </summary>
public sealed class HybridPolicyRetriever(
    IPolicyCandidateSource candidateSource,
    IEmbeddingProvider embeddings,
    IQueryExpander queryExpander,
    ILateInteractionReranker reranker,
    PolicyVectorIndex vectorIndex,
    HybridRetrievalOptions options) : IPolicyRetriever
{
    private readonly Bm25SparseEncoder _sparseEncoder = new();

    public async Task<PolicyRetrievalResult> SearchAsync(
        string programCode,
        string redactedQuestion,
        int take,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var limit = Math.Clamp(take, 1, 10);
        var queries = await BuildQueriesAsync(programCode, redactedQuestion, cancellationToken);
        var candidates = await candidateSource.FetchAsync(
            programCode,
            queries,
            Math.Max(options.CandidatePoolSize, limit),
            cancellationToken);

        if (candidates.Count == 0)
        {
            stopwatch.Stop();
            return new PolicyRetrievalResult([], Diagnostics(queries, 0, [], stopwatch.ElapsedMilliseconds));
        }

        var candidateVectors = await EmbedCandidatesAsync(candidates, queries, cancellationToken);
        var sparseIndex = _sparseEncoder.Build(candidates);

        var rankedLists = new List<IReadOnlyList<string>>();
        var denseRanks = new Dictionary<string, (int Rank, double Score)>(StringComparer.Ordinal);
        var sparseRanks = new Dictionary<string, (int Rank, double Score)>(StringComparer.Ordinal);

        for (var index = 0; index < queries.Count; index++)
        {
            var queryVector = candidateVectors.QueryVectors[index];
            var denseRanking = candidates
                .Select(candidate => new ScoredSource(
                    candidate.SourceId,
                    candidateVectors.Vectors.TryGetValue(candidate.SourceId, out var vector)
                        ? HashingVectorizer.Dot(queryVector, vector)
                        : 0))
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal)
                .ToArray();
            var sparseRanking = sparseIndex.Rank(queries[index]);

            rankedLists.Add(denseRanking.Select(item => item.SourceId).ToArray());
            rankedLists.Add(sparseRanking.Select(item => item.SourceId).ToArray());
            if (index == 0)
            {
                RecordRanks(denseRanks, denseRanking);
                RecordRanks(sparseRanks, sparseRanking);
            }
        }

        // When the candidate store ranked the pool itself (Azure AI Search), its opinion joins
        // the fusion as one more ranked list rather than being thrown away.
        if (candidates.Any(candidate => candidate.SourceScore > 0))
        {
            rankedLists.Add(candidates
                .OrderByDescending(candidate => candidate.SourceScore)
                .ThenBy(candidate => candidate.SourceId, StringComparer.Ordinal)
                .Select(candidate => candidate.SourceId)
                .ToArray());
        }

        var fused = ReciprocalRankFusion.Fuse(rankedLists, options.FusionConstant);
        var byId = candidates
            .GroupBy(candidate => candidate.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var shortlist = fused
            .Take(Math.Max(limit, options.RerankDepth))
            .Where(item => byId.ContainsKey(item.SourceId))
            .Select(item => byId[item.SourceId])
            .ToArray();

        var reranked = options.UseReranking
            ? reranker.Rerank(redactedQuestion, shortlist, sparseIndex.TermWeights(redactedQuestion))
            : shortlist.Select(candidate => new RerankedCandidate(candidate, 0)).ToArray();

        // Fusion scores sit in a narrow band and reranker scores in a wide one, so blending them
        // raw would let the reranker decide everything. Both are scaled across the shortlist
        // first, which keeps agreement between searches (fusion) and specific term-level
        // evidence (reranking) as comparable votes.
        var fusionById = fused.ToDictionary(item => item.SourceId, StringComparer.Ordinal);
        var fusionScale = Scale(reranked.Select(item => fusionById[item.Candidate.SourceId].Score));
        var rerankScale = Scale(reranked.Select(item => item.Score));
        var ordered = reranked
            .Select(item => new
            {
                item.Candidate,
                RerankScore = item.Score,
                Fusion = fusionById[item.Candidate.SourceId],
                FinalScore = options.UseReranking
                    ? (options.RerankWeight * rerankScale(item.Score))
                        + ((1 - options.RerankWeight) * fusionScale(fusionById[item.Candidate.SourceId].Score))
                    : fusionScale(fusionById[item.Candidate.SourceId].Score)
            })
            .OrderByDescending(item => item.FinalScore)
            .ThenBy(item => item.Fusion.Rank)
            .ThenBy(item => item.Candidate.SourceId, StringComparer.Ordinal)
            .ToArray();

        var selected = ordered.Take(limit).ToArray();
        var finalRanks = selected
            .Select((item, index) => (item.Candidate.SourceId, Rank: index + 1))
            .ToDictionary(item => item.SourceId, item => item.Rank, StringComparer.Ordinal);

        var ranking = ordered
            .Select(item => new RetrievalStageScore(
                item.Candidate.SourceId,
                denseRanks.TryGetValue(item.Candidate.SourceId, out var dense) ? dense.Rank : 0,
                Math.Round(dense.Score, 6),
                sparseRanks.TryGetValue(item.Candidate.SourceId, out var sparse) ? sparse.Rank : 0,
                Math.Round(sparse.Score, 6),
                item.Fusion.Rank,
                Math.Round(item.Fusion.Score, 6),
                Math.Round(item.RerankScore, 6),
                finalRanks.GetValueOrDefault(item.Candidate.SourceId)))
            .ToArray();

        var hits = selected
            .Select(item => new PolicyHit(
                item.Candidate.SourceId,
                item.Candidate.DocumentTitle,
                item.Candidate.DocumentVersion,
                item.Candidate.SectionLabel,
                item.Candidate.Content,
                Math.Round(item.FinalScore, 6)))
            .ToArray();

        stopwatch.Stop();
        return new PolicyRetrievalResult(
            hits,
            Diagnostics(queries, candidates.Count, ranking, stopwatch.ElapsedMilliseconds));
    }

    private async Task<IReadOnlyList<string>> BuildQueriesAsync(
        string programCode,
        string redactedQuestion,
        CancellationToken cancellationToken)
    {
        var queries = new List<string> { redactedQuestion.Trim() };
        if (!options.UseQueryExpansion || options.QueryVariants <= 0) return queries;

        var variants = await queryExpander.ExpandAsync(programCode, redactedQuestion, options.QueryVariants, cancellationToken);
        foreach (var variant in variants)
        {
            var trimmed = variant?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (queries.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) continue;
            queries.Add(trimmed);
            if (queries.Count > options.QueryVariants) break;
        }
        return queries;
    }

    private async Task<(IReadOnlyList<float[]> QueryVectors, Dictionary<string, float[]> Vectors)> EmbedCandidatesAsync(
        IReadOnlyList<PolicyCandidate> candidates,
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken)
    {
        var cachedModel = embeddings.ModelName;
        var cached = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var vector = vectorIndex.Get(cachedModel, candidate.SourceId, candidate.SearchableText);
            if (vector is not null) cached[candidate.SourceId] = vector;
        }

        var pending = candidates.Where(candidate => !cached.ContainsKey(candidate.SourceId)).ToArray();
        var inputs = new List<string>(queries.Count + pending.Length);
        inputs.AddRange(queries);
        inputs.AddRange(pending.Select(candidate => candidate.SearchableText));

        var embedded = await embeddings.EmbedAsync(inputs, cancellationToken);
        if (embedded.Count != inputs.Count)
        {
            throw new InvalidOperationException("The embedding provider returned a different number of vectors than inputs.");
        }

        var servedModel = embeddings.ModelName;
        var dimensions = embedded[0].Length;
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        for (var index = 0; index < pending.Length; index++)
        {
            var vector = embedded[queries.Count + index];
            vectors[pending[index].SourceId] = vector;
            vectorIndex.Set(servedModel, pending[index].SourceId, pending[index].SearchableText, vector);
        }

        // A cached vector of a different width means the embedding model changed underneath
        // us; those sections are embedded again rather than compared across models.
        var stale = candidates
            .Where(candidate => !vectors.ContainsKey(candidate.SourceId))
            .Where(candidate => cached[candidate.SourceId].Length != dimensions)
            .ToArray();
        foreach (var candidate in candidates)
        {
            if (vectors.ContainsKey(candidate.SourceId)) continue;
            var vector = cached[candidate.SourceId];
            if (vector.Length == dimensions) vectors[candidate.SourceId] = vector;
        }

        if (stale.Length > 0)
        {
            var refreshed = await embeddings.EmbedAsync(
                stale.Select(candidate => candidate.SearchableText).ToArray(),
                cancellationToken);
            for (var index = 0; index < stale.Length && index < refreshed.Count; index++)
            {
                vectors[stale[index].SourceId] = refreshed[index];
                vectorIndex.Set(embeddings.ModelName, stale[index].SourceId, stale[index].SearchableText, refreshed[index]);
            }
        }

        return (embedded.Take(queries.Count).ToArray(), vectors);
    }

    /// <summary>Min-max scaling of one stage's scores onto 0..1 across the shortlist.</summary>
    private static Func<double, double> Scale(IEnumerable<double> values)
    {
        var materialized = values.ToArray();
        if (materialized.Length == 0) return _ => 0;
        var minimum = materialized.Min();
        var maximum = materialized.Max();
        var range = maximum - minimum;
        return range <= 1e-12 ? _ => 1 : value => (value - minimum) / range;
    }

    /// <summary>Records where each section landed for the caseworker's own wording.</summary>
    private static void RecordRanks(
        Dictionary<string, (int Rank, double Score)> ranks,
        IReadOnlyList<ScoredSource> ranking)
    {
        for (var index = 0; index < ranking.Count; index++)
        {
            ranks[ranking[index].SourceId] = (index + 1, ranking[index].Score);
        }
    }

    private RetrievalDiagnostics Diagnostics(
        IReadOnlyList<string> queries,
        int candidateCount,
        IReadOnlyList<RetrievalStageScore> ranking,
        long elapsedMilliseconds) =>
        new(
            "multi-query-hybrid-rrf-rerank-v1",
            candidateSource.Name,
            queries,
            candidateCount,
            embeddings.ModelName,
            embeddings.Dimensions,
            embeddings.IsLive,
            Bm25SparseEncoder.ModelName,
            options.UseReranking ? reranker.ModelName : "disabled",
            queryExpander.Name,
            queryExpander.IsLive,
            options.FusionConstant,
            options.RerankDepth,
            ranking,
            elapsedMilliseconds);
}
