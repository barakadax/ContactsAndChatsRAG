using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed record ChatFileIndexEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type);
