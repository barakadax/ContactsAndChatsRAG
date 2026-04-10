using NUnit.Framework;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class OpenAiEmbeddingAdapterTests
{
    [Test]
    public async Task GenerateAsyncWithEmptyTextReturnsZeroVector()
    {
        var adapter = TestHelper.CreateEmbeddingAdapterForGuardTests();

        var result = await adapter.GenerateAsync("", CancellationToken.None);

        Assert.That(result.Length, Is.EqualTo(3072));
        Assert.That(result.ToArray().All(f => f == 0f), Is.True);
    }

    [Test]
    public async Task GenerateAsyncWithWhitespaceTextReturnsZeroVector()
    {
        var adapter = TestHelper.CreateEmbeddingAdapterForGuardTests();

        var result = await adapter.GenerateAsync("   ", CancellationToken.None);

        Assert.That(result.Length, Is.EqualTo(3072));
    }

    [Test]
    public async Task GenerateBatchAsyncWithEmptyListReturnsEmpty()
    {
        var adapter = TestHelper.CreateEmbeddingAdapterForGuardTests();

        var result = await adapter.GenerateBatchAsync([], CancellationToken.None);

        Assert.That(result, Is.Empty);
    }
}

internal static class TestHelper
{
    internal static OpenAiEmbeddingAdapter CreateEmbeddingAdapterForGuardTests()
    {
        var fakeClient = new OpenAI.OpenAIClient("sk-fake-key-for-testing");
        return new OpenAiEmbeddingAdapter(fakeClient, "text-embedding-3-large", Polly.ResiliencePipeline.Empty);
    }
}
