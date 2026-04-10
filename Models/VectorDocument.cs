namespace ContactsRag;

public sealed record VectorDocument(
    string Id,
    ReadOnlyMemory<float> Embedding,
    string Text,
    IReadOnlyDictionary<string, string> Metadata);
