namespace ContactsRag;

public interface IVectorDatabase
{
    Task UpsertAsync(IEnumerable<VectorDocument> documents, CancellationToken ct = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        ReadOnlyMemory<float> query,
        int topK,
        IReadOnlyDictionary<string, string>? metadataFilter = null,
        CancellationToken ct = default);
}
