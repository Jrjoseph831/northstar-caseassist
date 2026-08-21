namespace Northstar.Application.Assistant.Retrieval;

public sealed record FusedResult(string SourceId, double Score, int Rank);

/// <summary>
/// Reciprocal Rank Fusion. Each ranked list contributes 1 / (k + rank) to every source it
/// contains, so a section that several independent searches agree on rises above one that a
/// single search loved. Rank position is all that is used, which is the point: dense cosine
/// scores and BM25 scores are on different scales and cannot be added together directly.
/// </summary>
public static class ReciprocalRankFusion
{
    public const int DefaultConstant = 60;

    public static IReadOnlyList<FusedResult> Fuse(
        IEnumerable<IReadOnlyList<string>> rankedLists,
        int constant = DefaultConstant)
    {
        var safeConstant = Math.Max(constant, 1);
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var list in rankedLists)
        {
            for (var index = 0; index < list.Count; index++)
            {
                var sourceId = list[index];
                scores[sourceId] = scores.GetValueOrDefault(sourceId) + (1.0 / (safeConstant + index + 1));
            }
        }

        return scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select((pair, index) => new FusedResult(pair.Key, pair.Value, index + 1))
            .ToArray();
    }
}
