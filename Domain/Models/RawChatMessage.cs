using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed record RawChatMessage(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string UserName,
    [property: JsonPropertyName("sent_by")] string Timestamp,
    [property: JsonPropertyName("message")] string Message);
