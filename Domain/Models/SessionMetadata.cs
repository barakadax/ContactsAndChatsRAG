using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed record SessionMetadata(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("participants")] IReadOnlyList<string> Participants,
    [property: JsonPropertyName("start_time")] string? StartTime,
    [property: JsonPropertyName("end_time")] string? EndTime,
    [property: JsonPropertyName("message_count")] int MessageCount,
    [property: JsonPropertyName("messages")] IReadOnlyList<RawChatMessage> Messages);
