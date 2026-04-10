namespace ContactsRag;

public sealed record RagQueryOptions(
    int TopK = 5,
    string? ParticipantName = null,
    string? ChatFile = null);
