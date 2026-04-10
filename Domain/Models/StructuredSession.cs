using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed record StructuredSession(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("metadata")] SessionMetadata Metadata);
