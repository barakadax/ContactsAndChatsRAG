namespace ContactsRag;

public record RagResult(IAsyncEnumerable<string> AnswerStream, IReadOnlyList<string> SourceFiles);
