using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Polly;

namespace ContactsRag;

public sealed class HyDeGenerator(IChatClient chatClient, IOptions<RagEngineOptions> ragOptions, ResiliencePipeline resiliencePipeline) : IHyDeGenerator
{
    public async Task<string> GenerateAsync(string query, HyDeOptions? options = null, CancellationToken ct = default)
    {
        var temperature = options?.Temperature ?? 0.2f;

        var systemContext = ragOptions.Value.SystemPromptTemplate;

        var prompt = string.IsNullOrWhiteSpace(systemContext)
            ? query
            : $"Context about the data:\n{systemContext}\n\nQuestion:\n{query}";

        var response = await resiliencePipeline.ExecuteAsync(async token =>
            await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, "You write focused, information-dense draft answers for semantic retrieval. Do not mention that this is hypothetical."),
                    new ChatMessage(ChatRole.User, prompt)
                ],
                new ChatOptions { Temperature = temperature },
                token),
            ct);

        return response.Text ?? query;
    }
}
