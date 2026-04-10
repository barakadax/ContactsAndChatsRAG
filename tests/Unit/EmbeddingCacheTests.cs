using System.IO.Abstractions.TestingHelpers;
using NUnit.Framework;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class EmbeddingCacheTests
{
    private static readonly float[] SampleVector = [1.0f, 2.0f, 3.0f];
    private static readonly float[] SingleFloat = [1f];
    private static readonly float[] VectorA = [1f, 2f];
    private static readonly float[] VectorB = [3f, 4f];

    private MockFileSystem _fs = null!;
    private EmbeddingCache _cache = null!;
    private const string CacheDir = @"C:\cache";

    [SetUp]
    public void SetUp()
    {
        _fs = new MockFileSystem();
        _fs.AddDirectory(CacheDir);
        _cache = new EmbeddingCache(_fs, CacheDir);
    }

    [Test]
    public void ComputeHashFromContentWithSameInputReturnsSameHash()
    {
        var hash1 = _cache.ComputeHashFromContent("file.txt", "content");
        var hash2 = _cache.ComputeHashFromContent("file.txt", "content");

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void ComputeHashFromContentWithDifferentInputReturnsDifferentHash()
    {
        var hash1 = _cache.ComputeHashFromContent("file.txt", "content1");
        var hash2 = _cache.ComputeHashFromContent("file.txt", "content2");

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void ComputeHashFromContentWithDifferentNameReturnsDifferentHash()
    {
        var hash1 = _cache.ComputeHashFromContent("a.txt", "content");
        var hash2 = _cache.ComputeHashFromContent("b.txt", "content");

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public async Task TryLoadAsyncWhenFileDoesNotExistReturnsNull()
    {
        var result = await _cache.TryLoadAsync("nonexistent", CancellationToken.None);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task SaveAsyncThenTryLoadAsyncRoundTrips()
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["key"] = "value" };
        var docs = new List<VectorDocument>
        {
            new("doc1", SampleVector, "test text", metadata)
        };

        var hash = _cache.ComputeHashFromContent("test", "data");
        await _cache.SaveAsync(hash, docs, CancellationToken.None);

        var loaded = await _cache.TryLoadAsync(hash, CancellationToken.None);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!, Has.Count.EqualTo(1));
        Assert.That(loaded![0].Id, Is.EqualTo("doc1"));
        Assert.That(loaded[0].Text, Is.EqualTo("test text"));
        Assert.That(loaded[0].Embedding.ToArray(), Is.EqualTo(SampleVector));
        Assert.That(loaded[0].Metadata["key"], Is.EqualTo("value"));
    }

    [Test]
    public async Task SaveAsyncWritesToCorrectPath()
    {
        var docs = new List<VectorDocument>
        {
            new("id", SingleFloat, "text", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        };

        var hash = "abc123";
        await _cache.SaveAsync(hash, docs, CancellationToken.None);

        var expectedPath = _fs.Path.Combine(CacheDir, "abc123.json");
        Assert.That(_fs.File.Exists(expectedPath), Is.True);
    }

    [Test]
    public void ComputeHashReadsFileAndCombinesWithFileName()
    {
        var filePath = @"C:\cache\testfile.txt";
        _fs.AddFile(filePath, new MockFileData("file content here"));

        var hash = _cache.ComputeHash(filePath);
        var expectedHash = _cache.ComputeHashFromContent("testfile.txt", "file content here");

        Assert.That(hash, Is.EqualTo(expectedHash));
    }

    [Test]
    public async Task SaveAsyncWithMultipleDocumentsAllRoundTrip()
    {
        var docs = new List<VectorDocument>
        {
            new("doc1", VectorA, "text1", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            new("doc2", VectorB, "text2", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["meta"] = "val" })
        };

        var hash = "multi";
        await _cache.SaveAsync(hash, docs, CancellationToken.None);
        var loaded = await _cache.TryLoadAsync(hash, CancellationToken.None);

        Assert.That(loaded, Has.Count.EqualTo(2));
        Assert.That(loaded![0].Id, Is.EqualTo("doc1"));
        Assert.That(loaded[1].Id, Is.EqualTo("doc2"));
    }
}
