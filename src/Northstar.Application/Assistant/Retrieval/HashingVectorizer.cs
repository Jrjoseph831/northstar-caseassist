namespace Northstar.Application.Assistant.Retrieval;

/// <summary>
/// Deterministic local embedding used when no hosted embedding service is configured.
/// Features (stemmed terms plus character trigrams) are hashed into a fixed number of
/// dimensions with a signed contribution, then L2-normalized, so cosine similarity behaves
/// the way it does for a hosted embedding model. It is reproducible, needs no network call,
/// and gives the offline demonstration the same retrieval pipeline the hosted path runs.
/// </summary>
public static class HashingVectorizer
{
    private const double TermWeight = 1.0;
    private const double TrigramWeight = 0.35;

    /// <summary>Vector for a whole passage or query.</summary>
    public static float[] Document(string? text, int dimensions)
    {
        var vector = new float[dimensions];
        foreach (var token in TextAnalysis.Tokenize(text))
        {
            if (TextAnalysis.IsStopWord(token)) continue;
            Accumulate(vector, TextAnalysis.Stem(token), TermWeight);
            foreach (var trigram in TextAnalysis.Trigrams(token))
            {
                Accumulate(vector, trigram, TrigramWeight);
            }
        }
        Normalize(vector);
        return vector;
    }

    /// <summary>Vector for a single token, used by the late-interaction reranker.</summary>
    public static float[] Token(string token, int dimensions)
    {
        var vector = new float[dimensions];
        Accumulate(vector, TextAnalysis.Stem(token), TermWeight);
        foreach (var trigram in TextAnalysis.Trigrams(token))
        {
            Accumulate(vector, trigram, 0.6);
        }
        Normalize(vector);
        return vector;
    }

    public static void Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector) sum += value * value;
        if (sum <= 0) return;
        var length = Math.Sqrt(sum);
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / length);
        }
    }

    /// <summary>Cosine similarity for vectors that are already unit length.</summary>
    public static double Dot(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count) return 0;
        double sum = 0;
        for (var index = 0; index < left.Count; index++)
        {
            sum += left[index] * right[index];
        }
        return sum;
    }

    private static void Accumulate(float[] vector, string feature, double weight)
    {
        var hash = Fnv1a(feature);
        var index = (int)((hash >> 1) % (uint)vector.Length);
        var sign = (hash & 1u) == 0 ? 1.0 : -1.0;
        vector[index] += (float)(sign * weight);
    }

    private static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619u;
        }
        return hash;
    }
}
