using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Northstar.Application.Documents;

namespace Northstar.Infrastructure;

public sealed class LocalCaseDocumentStore(string rootDirectory) : ICaseDocumentStore
{
    public async Task StoreAsync(
        string storageKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoot = Path.GetFullPath(rootDirectory);
        var target = Path.GetFullPath(Path.Combine(normalizedRoot, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Document storage key escaped the configured root.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(output, cancellationToken);
    }
}

public sealed class AzureBlobCaseDocumentStore(
    Uri containerUri,
    TokenCredential credential) : ICaseDocumentStore
{
    private readonly BlobContainerClient _container = new(containerUri, credential);

    public async Task StoreAsync(
        string storageKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(storageKey);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
    }
}
