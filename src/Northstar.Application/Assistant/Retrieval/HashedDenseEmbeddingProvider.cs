namespace Northstar.Application.Assistant.Retrieval;

/// <summary>
/// The dense half of hybrid search when no hosted embedding service is configured. Runs
/// locally, costs nothing, and returns the same vector for the same text every time, which
/// keeps the offline demonstration and the evaluation runs reproducible.
/// </summary>
public sealed class HashedDenseEmbeddingProvider : IEmbeddingProvider
{
    public const int VectorSize = 256;

    public string ModelName => "northstar-hashed-dense-v1";

    public int Dimensions => VectorSize;

    public bool IsLive => false;

    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<float[]> vectors = inputs
            .Select(input => HashingVectorizer.Document(input, VectorSize))
            .ToArray();
        return Task.FromResult(vectors);
    }
}
