using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed record PhoneBookContainer(
    [property: JsonPropertyName("phone book")] string PhoneBookString,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("chat_count")] int ChatCount,
    [property: JsonPropertyName("first_chat")] ChatEdge? FirstChat,
    [property: JsonPropertyName("last_chat")] ChatEdge? LastChat,
    [property: JsonPropertyName("chat_file_ids")] IReadOnlyList<ChatFileIndexEntry> ChatFileIds,
    [property: JsonPropertyName("chats_summary")] string ChatsSummary,
    [property: JsonPropertyName("owner_name")] string? OwnerName,
    [property: JsonPropertyName("contacts")] Dictionary<string, Contact> Contacts);
