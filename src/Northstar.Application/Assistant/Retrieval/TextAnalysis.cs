using System.Text.RegularExpressions;

namespace Northstar.Application.Assistant.Retrieval;

/// <summary>
/// Shared text handling for every retrieval stage. Tokenization, stop-word removal and
/// stemming must be identical for indexing and querying, otherwise the sparse and dense
/// representations of the same wording stop lining up.
/// </summary>
public static partial class TextAnalysis
{
    private static readonly HashSet<string> StopWordSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "was", "were", "been", "being", "this", "that", "these", "those",
        "its", "with", "from", "can", "could", "should", "would", "does", "did", "you", "your",
        "they", "them", "their", "our", "what", "which", "who", "whom", "when", "where", "how", "why",
        "into", "over", "under", "any", "all", "not", "please", "there", "here", "about", "have",
        "has", "had", "will", "shall", "may", "might", "must", "but", "than", "then", "also", "such"
    };

    /// <summary>Lower-cased alphanumeric tokens of two characters or more.</summary>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var tokens = new List<string>();
        foreach (Match match in TokenPattern().Matches(text))
        {
            var token = match.Value.ToLowerInvariant();
            if (token.Length >= 2) tokens.Add(token);
        }
        return tokens;
    }

    /// <summary>Stemmed, stop-word-free terms — the vocabulary the retrieval stages index on.</summary>
    public static IReadOnlyList<string> ContentTerms(string? text)
    {
        var terms = new List<string>();
        foreach (var token in Tokenize(text))
        {
            if (StopWordSet.Contains(token)) continue;
            terms.Add(Stem(token));
        }
        return terms;
    }

    public static bool IsStopWord(string token) => StopWordSet.Contains(token);

    /// <summary>
    /// Deliberately light suffix stripping. It is not a linguistic stemmer; it exists so that
    /// "documents"/"document", "requires"/"required" and "policy"/"policies" match each other
    /// in a way that is stable and easy to reason about in an audited system.
    /// </summary>
    public static string Stem(string token)
    {
        var value = token.ToLowerInvariant();
        if (value.Length <= 4) return value;
        foreach (var suffix in Suffixes)
        {
            if (value.Length - suffix.Length >= 4 && value.EndsWith(suffix, StringComparison.Ordinal))
            {
                value = value[..^suffix.Length];
                break;
            }
        }
        if (value.Length > 4 && value.EndsWith('e')) value = value[..^1];
        if (value.Length > 4 && (value.EndsWith('y') || value.EndsWith('i'))) value = value[..^1];
        return value;
    }

    /// <summary>Character trigrams, used as sub-word features so near-miss wording still matches.</summary>
    public static IEnumerable<string> Trigrams(string token)
    {
        if (token.Length < 3)
        {
            yield return token;
            yield break;
        }
        for (var index = 0; index + 3 <= token.Length; index++)
        {
            yield return token.Substring(index, 3);
        }
    }

    private static readonly string[] Suffixes = ["ments", "ment", "ations", "ation", "ings", "ing", "ies", "es", "ed", "s"];

    [GeneratedRegex(@"[A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
