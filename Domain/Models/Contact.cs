using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed record Contact(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("phone_numbers")] IReadOnlyList<string> PhoneNumbers,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("chat_file_ids")] IReadOnlyList<string>? ChatFileIds = null);
