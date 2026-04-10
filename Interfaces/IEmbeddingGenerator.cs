namespace ContactsRag;

public interface IEmbeddingGenerator
{
    Task<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken ct = default);

    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default);
}
