namespace Northstar.Application.Documents;

public interface ICaseDocumentStore
{
    Task StoreAsync(
        string storageKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);
}
