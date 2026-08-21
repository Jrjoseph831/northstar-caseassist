namespace Northstar.Application.Assistant.Retrieval;

/// <summary>
/// Late-interaction (MaxSim) reranking of the fused shortlist. Instead of comparing one
/// vector for the whole question against one vector for the whole section, every question
/// token is matched against its best-matching section token and those matches are averaged.
/// A single-vector score can be dominated by a long section's overall topic; MaxSim rewards
/// the section that actually contains the specific things that were asked about.
/// </summary>
public sealed class LateInteractionReranker : ILateInteractionReranker
{
    public const int TokenDimensions = 128;
    private const int MaximumQueryTokens = 48;
    private const int MaximumDocumentTokens = 256;

    /// <summary>Similarity below which a token match is treated as noise.</summary>
    private const double MatchFloor = 0.30;

    public string ModelName => "northstar-late-interaction-maxsim-v1";

    public IReadOnlyList<RerankedCandidate> Rerank(
        string question,
        IReadOnlyList<PolicyCandidate> candidates,
        IReadOnlyDictionary<string, double>? queryTermWeights = null)
    {
        var queryTokens = Encode(question, MaximumQueryTokens);
        if (queryTokens.Count == 0)
        {
            return candidates.Select(candidate => new RerankedCandidate(candidate, 0)).ToArray();
        }

        var weights = queryTokens
            .Select(token => queryTermWeights is null
                ? 1.0
                : Math.Max(queryTermWeights.GetValueOrDefault(TextAnalysis.Stem(token.Term), 1.0), 0.05))
            .ToArray();

        return candidates
            .Select(candidate => new RerankedCandidate(
                candidate,
                MaxSim(
                    queryTokens.Select(token => token.Vector).ToArray(),
                    Encode(candidate.SearchableText, MaximumDocumentTokens).Select(token => token.Vector).ToArray(),
                    weights)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.SourceId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Mean over query tokens of the best similarity against any section token. Similarities
    /// below <see cref="MatchFloor"/> are discarded rather than accumulated: unrelated tokens
    /// still register a small similarity, and a long section has more tokens to collect that
    /// noise with, which would otherwise let length beat relevance.
    /// </summary>
    public static double MaxSim(
        IReadOnlyList<float[]> queryTokens,
        IReadOnlyList<float[]> documentTokens,
        IReadOnlyList<double>? queryTokenWeights = null)
    {
        if (queryTokens.Count == 0 || documentTokens.Count == 0) return 0;
        double total = 0;
        double weightTotal = 0;
        for (var index = 0; index < queryTokens.Count; index++)
        {
            var weight = queryTokenWeights is null || index >= queryTokenWeights.Count ? 1 : queryTokenWeights[index];
            weightTotal += weight;
            double best = 0;
            foreach (var documentToken in documentTokens)
            {
                var similarity = HashingVectorizer.Dot(queryTokens[index], documentToken);
                if (similarity > best) best = similarity;
            }
            if (best > MatchFloor) total += weight * (best - MatchFloor) / (1 - MatchFloor);
        }
        return weightTotal <= 0 ? 0 : total / weightTotal;
    }

    private static IReadOnlyList<(string Term, float[] Vector)> Encode(string text, int limit)
    {
        var vectors = new List<(string Term, float[] Vector)>();
        foreach (var token in TextAnalysis.Tokenize(text))
        {
            if (TextAnalysis.IsStopWord(token)) continue;
            vectors.Add((token, HashingVectorizer.Token(token, TokenDimensions)));
            if (vectors.Count >= limit) break;
        }
        return vectors;
    }
}
