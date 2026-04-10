using NSubstitute;
using NUnit.Framework;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class RagOrchestratorTests
{
    private static readonly float[] SampleVector = [1f, 2f, 3f];

    private IHyDeGenerator _hydeGenerator = null!;
    private IEmbeddingGenerator _embeddingGenerator = null!;
    private RagOrchestrator _orchestrator = null!;

    [SetUp]
    public void SetUp()
    {
        _hydeGenerator = Substitute.For<IHyDeGenerator>();
        _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
        _orchestrator = new RagOrchestrator(_hydeGenerator, _embeddingGenerator);
    }

    [Test]
    public async Task EmbedHyDeQueryAsyncCallsHyDeGeneratorThenEmbeds()
    {
        _hydeGenerator.GenerateAsync("question", Arg.Any<HyDeOptions?>(), Arg.Any<CancellationToken>())
            .Returns("hypothetical answer");
        _embeddingGenerator.GenerateAsync("hypothetical answer", Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(SampleVector));

        var result = await _orchestrator.EmbedHyDeQueryAsync("question", CancellationToken.None);

        Assert.That(result.ToArray(), Is.EqualTo(SampleVector));
        await _hydeGenerator.Received(1).GenerateAsync("question", Arg.Any<HyDeOptions?>(), Arg.Any<CancellationToken>());
        await _embeddingGenerator.Received(1).GenerateAsync("hypothetical answer", Arg.Any<CancellationToken>());
    }

    [Test]
    public void ExtractSourceFilesExtractsDistinctChatFiles()
    {
        var results = new List<VectorSearchResult>
        {
            new("1", 0.9, "text", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [MetadataKeys.ChatFile] = "file1" }),
            new("2", 0.8, "text", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [MetadataKeys.ChatFile] = "file2" }),
            new("3", 0.7, "text", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [MetadataKeys.ChatFile] = "file1" }),
        };

        var sources = RagOrchestrator.ExtractSourceFiles(results);

        Assert.That(sources, Has.Count.EqualTo(2));
        Assert.That(sources, Does.Contain("file1"));
        Assert.That(sources, Does.Contain("file2"));
    }

    [Test]
    public void ExtractSourceFilesSkipsNullAndQuestionMark()
    {
        var results = new List<VectorSearchResult>
        {
            new("1", 0.9, "text", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [MetadataKeys.ChatFile] = "?" }),
            new("2", 0.8, "text", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new("3", 0.7, "text", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [MetadataKeys.ChatFile] = "valid" }),
        };

        var sources = RagOrchestrator.ExtractSourceFiles(results);

        Assert.That(sources, Has.Count.EqualTo(1));
        Assert.That(sources[0], Is.EqualTo("valid"));
    }

    [Test]
    public void ExtractSourceFilesWithEmptyInputReturnsEmpty()
    {
        var sources = RagOrchestrator.ExtractSourceFiles([]);
        Assert.That(sources, Is.Empty);
    }
}
