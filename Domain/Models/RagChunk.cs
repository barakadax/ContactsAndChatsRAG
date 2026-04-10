namespace ContactsRag;

public sealed record RagChunk(
    string Id,
    string ChatFile,
    string SessionId,
    string Text,
    IReadOnlyList<string> Participants,
    IReadOnlyList<string> MessageUserIds,
    IReadOnlyList<string> MessageUserNames,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    int MessageCount);
