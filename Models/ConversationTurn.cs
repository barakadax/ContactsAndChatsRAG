using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed record ConversationTurn(
    [property: JsonPropertyName("user prompt")] string UserQuestion,
    [property: JsonPropertyName("AI answer")] string AssistantAnswer,
    [property: JsonPropertyName("files used by the RAG")] IReadOnlyList<string>? Files = null);
