using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed record ChatEdge(
    [property: JsonPropertyName("chat_file_id")] string ChatFileId,
    [property: JsonPropertyName("timestamp")] string Timestamp);
