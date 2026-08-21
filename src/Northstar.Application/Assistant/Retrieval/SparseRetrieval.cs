namespace Northstar.Application.Assistant.Retrieval;

public sealed record ScoredSource(string SourceId, double Score);

/// <summary>
/// A sparse embedding: term to weight. Only the terms present in the text carry a value,
/// which is what makes it sparse, and scoring is a dot product against a query vector.
/// </summary>
public sealed record SparseVector(IReadOnlyDictionary<string, double> Weights)
{
    public static SparseVector Empty { get; } = new(new Dictionary<string, double>(StringComparer.Ordinal));

    public int NonZeroCount => Weights.Count;

    public double Dot(SparseVector other)
    {
        var (smaller, larger) = Weights.Count <= other.Weights.Count ? (Weights, other.Weights) : (other.Weights, Weights);
        double sum = 0;
        foreach (var pair in smaller)
        {
            if (larger.TryGetValue(pair.Key, out var weight)) sum += pair.Value * weight;
        }
        return sum;
    }
}

/// <summary>
/// Builds BM25-weighted sparse vectors for the candidate pool. BM25 supplies the exact-term
/// half of hybrid search: policy questions turn on specific nouns ("disconnection notice",
/// "separation of duties") that a dense vector will happily blur into a neighbour.
/// </summary>
public sealed class Bm25SparseEncoder
{
    public const string ModelName = "northstar-bm25-sparse-v1";
    private const double K1 = 1.4;
    private const double B = 0.75;

    public SparseVectorIndex Build(IReadOnlyList<PolicyCandidate> candidates)
    {
        var documents = candidates
            .Select(candidate => new
            {
                candidate.SourceId,
                Terms = TextAnalysis.ContentTerms(candidate.SearchableText)
            })
            .ToArray();
        var documentCount = Math.Max(documents.Length, 1);
        var averageLength = documents.Length == 0 ? 1d : Math.Max(documents.Average(item => item.Terms.Count), 1d);

        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var term in document.Terms.Distinct(StringComparer.Ordinal))
            {
                documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
            }
        }

        var inverseDocumentFrequency = documentFrequency.ToDictionary(
            pair => pair.Key,
            pair => Math.Log(1 + ((documentCount - pair.Value + 0.5) / (pair.Value + 0.5))),
            StringComparer.Ordinal);

        var encoded = new List<(string SourceId, SparseVector Vector)>(documents.Length);
        foreach (var document in documents)
        {
            var weights = new Dictionary<string, double>(StringComparer.Ordinal);
            var lengthNormalizer = K1 * (1 - B + (B * document.Terms.Count / averageLength));
            foreach (var group in document.Terms.GroupBy(term => term, StringComparer.Ordinal))
            {
                double termFrequency = group.Count();
                var saturated = termFrequency * (K1 + 1) / (termFrequency + lengthNormalizer);
                weights[group.Key] = inverseDocumentFrequency.GetValueOrDefault(group.Key) * saturated;
            }
            encoded.Add((document.SourceId, new SparseVector(weights)));
        }

        return new SparseVectorIndex(encoded, inverseDocumentFrequency);
    }
}

public sealed class SparseVectorIndex(
    IReadOnlyList<(string SourceId, SparseVector Vector)> documents,
    IReadOnlyDictionary<string, double> inverseDocumentFrequency)
{
    public int DocumentCount => documents.Count;

    public SparseVector EncodeQuery(string query)
    {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var term in TextAnalysis.ContentTerms(query))
        {
            weights[term] = weights.GetValueOrDefault(term) + 1;
        }
        return weights.Count == 0 ? SparseVector.Empty : new SparseVector(weights);
    }

    public IReadOnlyList<ScoredSource> Rank(string query)
    {
        var queryVector = EncodeQuery(query);
        return documents
            .Select(item => new ScoredSource(item.SourceId, item.Vector.Dot(queryVector)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal)
            .ToArray();
    }

    public double InverseDocumentFrequency(string term) => inverseDocumentFrequency.GetValueOrDefault(term);

    /// <summary>Inverse document frequency for each term in a query, for weighted reranking.</summary>
    public IReadOnlyDictionary<string, double> TermWeights(string query)
    {
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var term in TextAnalysis.ContentTerms(query))
        {
            weights[term] = inverseDocumentFrequency.GetValueOrDefault(term, DefaultUnseenTermWeight);
        }
        return weights;
    }

    /// <summary>A term absent from the corpus is rare by definition, so it is weighted highly.</summary>
    private const double DefaultUnseenTermWeight = 2.0;
}
