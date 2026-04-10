namespace ContactsRag;

public sealed record VectorSearchResult(
    string Id,
    double Score,
    string Text,
    IReadOnlyDictionary<string, string> Metadata);
