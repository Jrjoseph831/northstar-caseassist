using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Northstar.Application.Assistant.Retrieval;

/// <summary>
/// Keeps the dense vector for each approved policy section so the corpus is embedded once
/// rather than on every question. The key carries the embedding model and a hash of the
/// section text, so a re-published section or a change of embedding model produces a new
/// entry instead of silently reusing a vector that no longer describes the text.
/// </summary>
public sealed class PolicyVectorIndex
{
    private readonly ConcurrentDictionary<string, float[]> _vectors = new(StringComparer.Ordinal);

    public int Count => _vectors.Count;

    public float[]? Get(string model, string sourceId, string content) =>
        _vectors.TryGetValue(Key(model, sourceId, content), out var vector) ? vector : null;

    public void Set(string model, string sourceId, string content, float[] vector) =>
        _vectors[Key(model, sourceId, content)] = vector;

    private static string Key(string model, string sourceId, string content)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return $"{model}|{sourceId}|{Convert.ToHexString(digest)}";
    }
}
