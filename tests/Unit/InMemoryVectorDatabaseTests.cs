using NUnit.Framework;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class InMemoryVectorDatabaseTests
{
    private InMemoryVectorDatabase _db = null!;

    [SetUp]
    public void SetUp()
    {
        _db = new InMemoryVectorDatabase();
    }

    private static VectorDocument CreateDoc(string id, float[] embedding, string text, Dictionary<string, string>? metadata = null)
        => new(id, embedding, text, metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static float[] NormalizedVector(params float[] values)
    {
        var magnitude = MathF.Sqrt(values.Sum(v => v * v));
        return magnitude > 0 ? [.. values.Select(v => v / magnitude)] : values;
    }

    [Test]
    public async Task UpsertAsyncThenSearchAsyncReturnsDocuments()
    {
        var vec = NormalizedVector(1f, 0f, 0f);
        var doc = CreateDoc("doc1", vec, "hello world");
        await _db.UpsertAsync([doc]);

        var results = await _db.SearchAsync(vec, topK: 5);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Id, Is.EqualTo("doc1"));
        Assert.That(results[0].Score, Is.GreaterThan(0.99));
    }

    [Test]
    public async Task SearchAsyncReturnsTopKResultsSortedByScore()
    {
        var query = NormalizedVector(1f, 0f, 0f);
        var close = NormalizedVector(0.9f, 0.1f, 0f);
        var far = NormalizedVector(0f, 1f, 0f);

        await _db.UpsertAsync([
            CreateDoc("close", close, "close"),
            CreateDoc("far", far, "far")
        ]);

        var results = await _db.SearchAsync(query, topK: 1);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Id, Is.EqualTo("close"));
    }

    [Test]
    public async Task SearchAsyncWithMetadataFilterFiltersCorrectly()
    {
        var vec = NormalizedVector(1f, 0f, 0f);
        var meta1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["type"] = "contact" };
        var meta2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["type"] = "session" };

        await _db.UpsertAsync([
            CreateDoc("a", vec, "contact doc", meta1),
            CreateDoc("b", vec, "session doc", meta2)
        ]);

        var filter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["type"] = "contact" };
        var results = await _db.SearchAsync(vec, topK: 10, filter);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Id, Is.EqualTo("a"));
    }

    [Test]
    public async Task SearchAsyncWithPipeSeparatedMetadataFilterMatches()
    {
        var vec = NormalizedVector(1f, 0f, 0f);
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["participants"] = "Alice|Bob|Charlie" };
        await _db.UpsertAsync([CreateDoc("doc1", vec, "text", meta)]);

        var filter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["participants"] = "Bob" };
        var results = await _db.SearchAsync(vec, topK: 10, filter);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SearchAsyncOnEmptyDatabaseReturnsEmpty()
    {
        var results = await _db.SearchAsync(NormalizedVector(1f, 0f, 0f), topK: 10);
        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task UpsertAsyncWithSameIdOverwritesExisting()
    {
        var vec = NormalizedVector(1f, 0f, 0f);
        await _db.UpsertAsync([CreateDoc("doc1", vec, "original")]);
        await _db.UpsertAsync([CreateDoc("doc1", vec, "updated")]);

        var results = await _db.SearchAsync(vec, topK: 10);
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Text, Is.EqualTo("updated"));
    }

    [Test]
    public void SearchAsyncWithCancelledTokenThrows()
    {
        var vec = NormalizedVector(1f, 0f, 0f);
        _db.UpsertAsync([CreateDoc("doc1", vec, "text")]).Wait();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => _db.SearchAsync(vec, topK: 10, ct: cts.Token));
    }

    [Test]
    public async Task SearchAsyncWithNoFilterReturnsAllDocuments()
    {
        var vec = NormalizedVector(1f, 0f, 0f);
        await _db.UpsertAsync([
            CreateDoc("a", vec, "one"),
            CreateDoc("b", vec, "two"),
            CreateDoc("c", vec, "three")
        ]);

        var results = await _db.SearchAsync(vec, topK: 10, metadataFilter: null);
        Assert.That(results, Has.Count.EqualTo(3));
    }
}
