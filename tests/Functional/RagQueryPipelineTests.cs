using System.IO.Abstractions.TestingHelpers;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NSubstitute;
using NUnit.Framework;
using Polly;
using ChatMessage = Microsoft.SemanticKernel.ChatMessageContent;

namespace ContactsRag.Tests.Functional;

[TestFixture]
public class RagQueryPipelineTests
{
    private MockFileSystem _fs = null!;
    private IEmbeddingGenerator _embeddingGenerator = null!;
    private InMemoryVectorDatabase _vectorDb = null!;
    private EmbeddingCache _embeddingCache = null!;
    private DomainRagOrchestrator _ragOrchestrator = null!;
    private const string RootDir = @"C:\project";
    private const string CacheDir = @"C:\project\.embedding_cache";

    [SetUp]
    public async Task SetUp()
    {
        _fs = new MockFileSystem();
        _fs.AddDirectory(CacheDir);

        _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
        _embeddingGenerator.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var text = callInfo.Arg<string>();
                var hash = text.GetHashCode();
                var vec = new float[8];
                for (var i = 0; i < vec.Length; i++)
                    vec[i] = ((hash >> i) & 1) == 1 ? 1f : -1f;
                var mag = MathF.Sqrt(vec.Sum(v => v * v));
                return new ReadOnlyMemory<float>([.. vec.Select(v => v / mag)]);
            });
        _embeddingGenerator.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var texts = callInfo.Arg<IEnumerable<string>>().ToList();
                var results = new List<ReadOnlyMemory<float>>();
                foreach (var t in texts)
                    results.Add(await _embeddingGenerator.GenerateAsync(t, CancellationToken.None));
                return results as IReadOnlyList<ReadOnlyMemory<float>>;
            });

        _vectorDb = new InMemoryVectorDatabase();
        _embeddingCache = new EmbeddingCache(_fs, CacheDir);

        TestDataBuilder.SetupMockFileSystem(_fs, RootDir);

        var chatDataGenerator = new ChatDataGenerator(_fs);
        await chatDataGenerator.GenerateAsync(RootDir, CancellationToken.None);

        var ingestionService = new ChatIngestionService(
            _embeddingGenerator, _vectorDb, _embeddingCache, _fs, RootDir);
        await ingestionService.IngestAsync(CancellationToken.None);

        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
            Arg.Any<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "hypothetical answer about phone data")));

        var ragEngineOptions = Options.Create(new RagEngineOptions
        {
            SystemPromptTemplate = "You are a forensic analyst."
        });

        var hydeGenerator = new HyDeGenerator(chatClient, ragEngineOptions, ResiliencePipeline.Empty);
        var innerOrchestrator = new RagOrchestrator(hydeGenerator, _embeddingGenerator);

        var chatCompletionService = Substitute.For<IChatCompletionService>();
        chatCompletionService.GetStreamingChatMessageContentsAsync(
            Arg.Any<Microsoft.SemanticKernel.ChatCompletion.ChatHistory>(),
            Arg.Any<PromptExecutionSettings?>(),
            Arg.Any<Kernel?>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateAsyncEnumerable(new ChatMessage(AuthorRole.Assistant, "The owner of this phone book is Lee.")));

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton(chatCompletionService);
        var kernel = kernelBuilder.Build();

        _ragOrchestrator = new DomainRagOrchestrator(
            innerOrchestrator,
            _embeddingGenerator,
            _vectorDb,
            kernel,
            ragEngineOptions,
            ResiliencePipeline.Empty,
            _fs,
            RootDir);
    }

    private static async IAsyncEnumerable<StreamingChatMessageContent> CreateAsyncEnumerable(
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield return new StreamingChatMessageContent(message.Role, message.Content);
    }

    [Test]
    public async Task QueryAfterIngestionReturnsAnswerWithSources()
    {
        var result = await _ragOrchestrator.AskStreamingAsync(
            "Who is the owner?", [], null, CancellationToken.None);

        var answer = "";
        await foreach (var token in result.AnswerStream)
            answer += token;

        Assert.That(answer, Is.Not.Empty);
        Assert.That(result.SourceFiles, Is.Not.Empty);
    }

    [Test]
    public async Task QueryForPhoneBookReturnsResults()
    {
        var result = await _ragOrchestrator.AskStreamingAsync(
            "Name me all the chat file ids", [], null, CancellationToken.None);

        var answer = "";
        await foreach (var token in result.AnswerStream)
            answer += token;

        Assert.That(answer, Is.Not.Empty);
        Assert.That(result.SourceFiles, Does.Contain("SYSTEM_PROMPT"));
    }

    [Test]
    public async Task QueryWithHistoryIncludesHistoryInContext()
    {
        var history = new List<ConversationTurn>
        {
            new("Previous question", "Previous answer", ["file1"])
        };

        var result = await _ragOrchestrator.AskStreamingAsync(
            "Follow up question", history, null, CancellationToken.None);

        var answer = "";
        await foreach (var token in result.AnswerStream)
            answer += token;

        Assert.That(answer, Is.Not.Empty);
    }
}
