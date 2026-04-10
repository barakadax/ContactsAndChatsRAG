using OpenAI;
using OpenAI.Embeddings;
using Polly;
using Spectre.Console;

namespace ContactsRag;

public sealed class OpenAiEmbeddingAdapter(OpenAIClient client, string model, ResiliencePipeline resiliencePipeline) : IEmbeddingGenerator
{
    private const int EmbeddingDimension = 3072;
    private readonly EmbeddingClient _embeddingClient = client.GetEmbeddingClient(model);

    public async Task<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[EmbeddingDimension];
        }

        try
        {
            var embedding = await resiliencePipeline.ExecuteAsync(
                async token => await _embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: token),
                ct);
            return embedding.Value.ToFloats().ToArray();
        }
        catch (Exception)
        {
            AnsiConsole.MarkupLine(
                $"[red][ERROR][/] Embedding failed for string of length {text.Length}. " +
                $"Snippet: {Markup.Escape(text[..Math.Min(100, text.Length)])}");
            throw;
        }
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IEnumerable<string> texts,
        CancellationToken ct = default)
    {
        var items = texts.Select(v => string.IsNullOrWhiteSpace(v) ? " " : v).ToList();

        if (items.Count == 0)
        {
            return [];
        }

        try
        {
            var result = await resiliencePipeline.ExecuteAsync(
                async token => await _embeddingClient.GenerateEmbeddingsAsync(items, cancellationToken: token),
                ct);
            return [.. result.Value.Select(static e => (ReadOnlyMemory<float>)e.ToFloats().ToArray())];
        }
        catch (Exception)
        {
            AnsiConsole.MarkupLine($"[red][ERROR][/] Batch embedding failed for {items.Count} items. Falling back to one-by-one.");

            var list = new List<ReadOnlyMemory<float>>(items.Count);
            foreach (var item in items)
            {
                list.Add(await GenerateAsync(item, ct));
            }

            return list;
        }
    }
}
