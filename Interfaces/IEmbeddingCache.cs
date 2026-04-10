namespace ContactsRag;

public interface IEmbeddingCache
{
    string ComputeHash(string filePath);
    string ComputeHashFromContent(string logicalName, string content);
    Task<IReadOnlyList<VectorDocument>?> TryLoadAsync(string hash, CancellationToken ct);
    Task SaveAsync(string hash, IReadOnlyList<VectorDocument> documents, CancellationToken ct);
}
