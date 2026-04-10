namespace ContactsRag;

public sealed class RagEngineOptions
{
    public string SystemPromptTemplate { get; set; } =
        "Answer using only the provided excerpts. If unsupported, say you don't know.";
}
