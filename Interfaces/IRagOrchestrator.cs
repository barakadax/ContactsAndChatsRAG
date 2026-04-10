namespace ContactsRag;

public interface IRagOrchestrator
{
    Task<RagResult> AskStreamingAsync(
        string userQuestion,
        IReadOnlyList<ConversationTurn>? history,
        RagQueryOptions? options,
        CancellationToken cancellationToken);
}
