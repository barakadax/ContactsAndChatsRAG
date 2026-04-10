using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using NSubstitute;
using NUnit.Framework;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class ChatIngestionServiceTests
{
    private static readonly float[] DefaultVector = [1f, 0f, 0f];
    private static readonly float[] SingleFloat = [1f];
    private static readonly JsonSerializerOptions PhoneBookJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private IEmbeddingGenerator _embeddingGenerator = null!;
    private IVectorDatabase _vectorDatabase = null!;
    private IEmbeddingCache _embeddingCache = null!;
    private MockFileSystem _fs = null!;
    private const string ContentRoot = @"C:\project";

    [SetUp]
    public void SetUp()
    {
        _embeddingGenerator = Substitute.For<IEmbeddingGenerator>();
        _vectorDatabase = Substitute.For<IVectorDatabase>();
        _embeddingCache = Substitute.For<IEmbeddingCache>();
        _fs = new MockFileSystem();

        _embeddingGenerator.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(DefaultVector));
        _embeddingGenerator.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.Arg<IEnumerable<string>>().ToList();
                return texts.Select(_ => new ReadOnlyMemory<float>(DefaultVector)).ToList() as IReadOnlyList<ReadOnlyMemory<float>>;
            });

        _embeddingCache.ComputeHash(Arg.Any<string>()).Returns("default_hash");
        _embeddingCache.ComputeHashFromContent(Arg.Any<string>(), Arg.Any<string>()).Returns("default_content_hash");
        _embeddingCache.TryLoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<VectorDocument>?)null);
    }

    private ChatIngestionService CreateService() =>
        new(_embeddingGenerator, _vectorDatabase, _embeddingCache, _fs, ContentRoot);

    private static PhoneBookContainer CreatePhoneBookContainer()
    {
        return new PhoneBookContainer(
            "PHONE BOOK\n",
            1,
            0,
            null,
            null,
            [],
            "CHATS INDEX\nTotal chats: 0",
            "Lee",
            new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase)
            {
                ["A001"] = new("John", "Doe", "John Doe", ["0123456789"], "2024-01-01")
            });
    }

    private static string CreatePhoneBookJson()
    {
        return JsonSerializer.Serialize(CreatePhoneBookContainer(), PhoneBookJsonOptions);
    }

    private void SetupMinimalFileSystem()
    {
        _fs.AddFile($@"{ContentRoot}\phone_book.json", new MockFileData(CreatePhoneBookJson()));
        _fs.AddFile($@"{ContentRoot}\SYSTEM_PROMPT", new MockFileData("You are a forensic analyst."));
        _fs.AddDirectory($@"{ContentRoot}\structured_chats");
    }

    [Test]
    public async Task IngestAsyncWhenSystemPromptExistsEmbedsAndStores()
    {
        SetupMinimalFileSystem();
        var service = CreateService();

        var (read, embedded, stored, _) = await service.IngestAsync(CancellationToken.None);

        Assert.That(read, Is.GreaterThanOrEqualTo(1));
        Assert.That(embedded, Is.GreaterThan(0));
        Assert.That(stored, Is.GreaterThan(0));
    }

    [Test]
    public async Task IngestAsyncWhenSystemPromptCachedLoadsFromCache()
    {
        SetupMinimalFileSystem();
        var cachedDocs = new List<VectorDocument>
        {
            new("system_prompt", SingleFloat, "cached prompt", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        };

        _embeddingCache.TryLoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(cachedDocs);

        var service = CreateService();
        var (_, _, _, cacheHits) = await service.IngestAsync(CancellationToken.None);

        Assert.That(cacheHits, Is.GreaterThan(0));
    }

    [Test]
    public async Task IngestAsyncWithNoChatFilesHandlesGracefully()
    {
        SetupMinimalFileSystem();
        var service = CreateService();

        var (read, _, stored, _) = await service.IngestAsync(CancellationToken.None);

        Assert.That(read, Is.GreaterThanOrEqualTo(2));
        Assert.That(stored, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task IngestAsyncWhenAllCachedReturnsCorrectCacheHitCount()
    {
        SetupMinimalFileSystem();
        var cachedDocs = new List<VectorDocument>
        {
            new("doc", SingleFloat, "text", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        };

        _embeddingCache.TryLoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(cachedDocs);

        var service = CreateService();
        var (_, embedded, _, cacheHits) = await service.IngestAsync(CancellationToken.None);

        Assert.That(cacheHits, Is.GreaterThanOrEqualTo(2));
        Assert.That(embedded, Is.EqualTo(0));
    }

    [Test]
    public async Task IngestAsyncReturnsReadCountIncludingSystemPromptAndPhoneBook()
    {
        SetupMinimalFileSystem();
        var service = CreateService();

        var (read, _, _, _) = await service.IngestAsync(CancellationToken.None);

        Assert.That(read, Is.EqualTo(2));
    }
}
