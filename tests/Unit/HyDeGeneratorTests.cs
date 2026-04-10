using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using Polly;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class HyDeGeneratorTests
{
    private IChatClient _chatClient = null!;
    private IOptions<RagEngineOptions> _options = null!;
    private HyDeGenerator _generator = null!;

    [SetUp]
    public void SetUp()
    {
        _chatClient = Substitute.For<IChatClient>();
        _options = Options.Create(new RagEngineOptions { SystemPromptTemplate = "You are a forensic analyst." });
        _generator = new HyDeGenerator(_chatClient, _options, ResiliencePipeline.Empty);
    }

    [Test]
    public async Task GenerateAsyncReturnsResponseText()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "hypothetical answer"));
        _chatClient.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _generator.GenerateAsync("What is the owner's name?");

        Assert.That(result, Is.EqualTo("hypothetical answer"));
    }

    [Test]
    public async Task GenerateAsyncWithEmptyResponseTextReturnsEmptyString()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ""));
        _chatClient.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _generator.GenerateAsync("original query");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GenerateAsyncUsesSystemPromptFromOptions()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"));
        IEnumerable<ChatMessage>? capturedMessages = null;
        _chatClient.GetResponseAsync(
            Arg.Do<IEnumerable<ChatMessage>>(msgs => capturedMessages = [.. msgs]),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);

        await _generator.GenerateAsync("test query");

        Assert.That(capturedMessages, Is.Not.Null);
        var userMsg = capturedMessages!.Last();
        Assert.That(userMsg.Text, Does.Contain("You are a forensic analyst."));
    }

    [Test]
    public async Task GenerateAsyncWithNoSystemPromptUsesQueryOnly()
    {
        var emptyOptions = Options.Create(new RagEngineOptions { SystemPromptTemplate = "" });
        var gen = new HyDeGenerator(_chatClient, emptyOptions, ResiliencePipeline.Empty);

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"));
        IEnumerable<ChatMessage>? capturedMessages = null;
        _chatClient.GetResponseAsync(
            Arg.Do<IEnumerable<ChatMessage>>(msgs => capturedMessages = [.. msgs]),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(response);

        await gen.GenerateAsync("just a question");

        var userMsg = capturedMessages!.Last();
        Assert.That(userMsg.Text, Is.EqualTo("just a question"));
    }
}
