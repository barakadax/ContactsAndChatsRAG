namespace ContactsRag;

public sealed class RagOrchestrator(
    IHyDeGenerator hydeGenerator,
    IEmbeddingGenerator embeddingGenerator)
{
    internal async Task<ReadOnlyMemory<float>> EmbedHyDeQueryAsync(string question, CancellationToken ct)
    {
        var hydeText = await hydeGenerator.GenerateAsync(question, ct: ct);
        return await embeddingGenerator.GenerateAsync(hydeText, ct);
    }

    private const string UnknownFile = "?";

    internal static IReadOnlyList<string> ExtractSourceFiles(IReadOnlyList<VectorSearchResult> sources)
        => [.. sources
            .Select(static r => r.Metadata.TryGetValue(MetadataKeys.ChatFile, out var f) ? f : null)
            .Where(static f => !string.IsNullOrWhiteSpace(f) && f != UnknownFile)
            .Select(static f => f!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}
