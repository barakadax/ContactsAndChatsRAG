using System.IO.Abstractions.TestingHelpers;
using NUnit.Framework;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class ChatHistoryExporterTests
{
    [Test]
    public async Task SaveAsyncWritesJsonToFile()
    {
        var fs = new MockFileSystem();
        var exporter = new ChatHistoryExporter(fs);
        var history = new List<ConversationTurn>
        {
            new("What is the owner?", "Lee", ["phone_book"])
        };

        var fileName = await exporter.SaveAsync(history);

        Assert.That(fs.File.Exists(fileName), Is.True);
        var content = fs.File.ReadAllText(fileName);
        Assert.That(content, Does.Contain("What is the owner?"));
        Assert.That(content, Does.Contain("Lee"));
    }

    [Test]
    public async Task SaveAsyncReturnsJsonFileName()
    {
        var fs = new MockFileSystem();
        var exporter = new ChatHistoryExporter(fs);

        var fileName = await exporter.SaveAsync([]);

        Assert.That(fileName, Does.EndWith(".json"));
    }

    [Test]
    public async Task SaveAsyncWithEmptyHistoryWritesEmptyArray()
    {
        var fs = new MockFileSystem();
        var exporter = new ChatHistoryExporter(fs);

        var fileName = await exporter.SaveAsync([]);

        var content = fs.File.ReadAllText(fileName);
        Assert.That(content.Trim(), Is.EqualTo("[]"));
    }

    [Test]
    public async Task SaveAsyncWithMultipleEntriesAllSerialized()
    {
        var fs = new MockFileSystem();
        var exporter = new ChatHistoryExporter(fs);
        var history = new List<ConversationTurn>
        {
            new("Q1", "A1", ["file1"]),
            new("Q2", "A2", ["file2"])
        };

        var fileName = await exporter.SaveAsync(history);

        var content = fs.File.ReadAllText(fileName);
        Assert.That(content, Does.Contain("Q1"));
        Assert.That(content, Does.Contain("Q2"));
    }
}
