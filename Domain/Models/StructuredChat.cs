using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed record StructuredChat(
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("chat_type")] string ChatType,
    [property: JsonPropertyName("start")] string Start,
    [property: JsonPropertyName("end")] string End,
    [property: JsonPropertyName("participants")] Dictionary<string, object> Participants,
    [property: JsonPropertyName("sessions")] IReadOnlyList<StructuredSession> Sessions);
