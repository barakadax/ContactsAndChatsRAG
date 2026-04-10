using System.IO.Abstractions.TestingHelpers;
using NSubstitute;
using NUnit.Framework;

namespace ContactsRag.Tests.Functional;

[TestFixture]
public class IngestionPipelineTests
{
    private MockFileSystem _fs = null!;
    private IEmbeddingGenerator _embeddingGenerator = null!;
    private InMemoryVectorDatabase _vectorDb = null!;
    private EmbeddingCache _embeddingCache = null!;
    private ChatDataGenerator _chatDataGenerator = null!;
    private ChatIngestionService _ingestionService = null!;
    private const string RootDir = @"C:\project";
    private const string CacheDir = @"C:\project\.embedding_cache";

    [SetUp]
    public void SetUp()
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
        _chatDataGenerator = new ChatDataGenerator(_fs);
        _ingestionService = new ChatIngestionService(
            _embeddingGenerator, _vectorDb, _embeddingCache, _fs, RootDir);

        TestDataBuilder.SetupMockFileSystem(_fs, RootDir);
    }

    [Test]
    public async Task FullIngestionStructuresAndEmbedsAllFiles()
    {
        var (read, structured) = await _chatDataGenerator.GenerateAsync(RootDir, CancellationToken.None);

        Assert.That(read, Is.GreaterThan(0));
        Assert.That(structured, Is.GreaterThan(0));
        Assert.That(_fs.File.Exists($@"{RootDir}\phone_book.json"), Is.True);

        var (ingRead, embedded, stored, _) = await _ingestionService.IngestAsync(CancellationToken.None);

        Assert.That(ingRead, Is.GreaterThan(0));
        Assert.That(embedded, Is.GreaterThan(0));
        Assert.That(stored, Is.GreaterThan(0));

        var searchVec = await _embeddingGenerator.GenerateAsync("test query", CancellationToken.None);
        var results = await _vectorDb.SearchAsync(searchVec, topK: 100);
        Assert.That(results, Is.Not.Empty);
    }

    [Test]
    public async Task FullIngestionOnSecondRunGetsAllCacheHits()
    {
        await _chatDataGenerator.GenerateAsync(RootDir, CancellationToken.None);
        await _ingestionService.IngestAsync(CancellationToken.None);

        var db2 = new InMemoryVectorDatabase();
        var service2 = new ChatIngestionService(
            _embeddingGenerator, db2, _embeddingCache, _fs, RootDir);

        var (_, embedded, _, cacheHits) = await service2.IngestAsync(CancellationToken.None);

        Assert.That(cacheHits, Is.GreaterThan(0));
        Assert.That(embedded, Is.EqualTo(0));
    }

    [Test]
    public async Task FullIngestionStoresContactsInVectorDb()
    {
        await _chatDataGenerator.GenerateAsync(RootDir, CancellationToken.None);
        await _ingestionService.IngestAsync(CancellationToken.None);

        var contactVec = await _embeddingGenerator.GenerateAsync("phone book contact", CancellationToken.None);
        var filter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetadataKeys.Type] = MetadataKeys.TypeContact
        };
        var results = await _vectorDb.SearchAsync(contactVec, topK: 50, filter);

        Assert.That(results, Is.Not.Empty);
    }

    [Test]
    public async Task FullIngestionStoresSystemPrompt()
    {
        await _chatDataGenerator.GenerateAsync(RootDir, CancellationToken.None);
        await _ingestionService.IngestAsync(CancellationToken.None);

        var sysVec = await _embeddingGenerator.GenerateAsync("SYSTEM_PROMPT", CancellationToken.None);
        var filter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetadataKeys.Type] = MetadataKeys.TypeSystemPrompt
        };
        var results = await _vectorDb.SearchAsync(sysVec, topK: 1, filter);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Text, Does.Contain("forensic analyst"));
    }

    [Test]
    public async Task FullIngestionCreatesCacheFiles()
    {
        await _chatDataGenerator.GenerateAsync(RootDir, CancellationToken.None);
        await _ingestionService.IngestAsync(CancellationToken.None);

        var cacheFiles = _fs.Directory.EnumerateFiles(CacheDir, "*.json").ToList();
        Assert.That(cacheFiles, Is.Not.Empty);
    }
}
