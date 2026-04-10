using System.IO.Abstractions.TestingHelpers;
using NUnit.Framework;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class ChatDataGeneratorTests
{
    private MockFileSystem _fs = null!;
    private ChatDataGenerator _generator = null!;
    private const string RootDir = @"C:\project";

    [SetUp]
    public void SetUp()
    {
        _fs = new MockFileSystem();
        _generator = new ChatDataGenerator(_fs);
    }

    [Test]
    public async Task GenerateAsyncWhenPhonebookNotFoundReturnsZero()
    {
        _fs.AddDirectory(RootDir);

        var (read, structured) = await _generator.GenerateAsync(RootDir, CancellationToken.None);

        Assert.That(read, Is.EqualTo(0));
        Assert.That(structured, Is.EqualTo(0));
    }

    [Test]
    public async Task GenerateAsyncWhenChatsDirNotFoundReturnsZero()
    {
        _fs.AddDirectory(RootDir);
        _fs.AddFile($@"{RootDir}\phonebook.txt", new MockFileData(""));

        var (read, structured) = await _generator.GenerateAsync(RootDir, CancellationToken.None);

        Assert.That(read, Is.EqualTo(0));
        Assert.That(structured, Is.EqualTo(0));
    }

    [Test]
    public async Task GenerateAsyncWithEmptyPhonebookAndChatsCreatesPhoneBookJson()
    {
        _fs.AddFile($@"{RootDir}\phonebook.txt", new MockFileData(""));
        _fs.AddDirectory($@"{RootDir}\chats");

        var (read, structured) = await _generator.GenerateAsync(RootDir, CancellationToken.None);

        Assert.That(read, Is.GreaterThanOrEqualTo(1));
        Assert.That(structured, Is.GreaterThanOrEqualTo(1));
        Assert.That(_fs.File.Exists($@"{RootDir}\phone_book.json"), Is.True);
    }

    [Test]
    public async Task GenerateAsyncCleansExistingStructuredChats()
    {
        _fs.AddFile($@"{RootDir}\phonebook.txt", new MockFileData(""));
        _fs.AddDirectory($@"{RootDir}\chats");
        _fs.AddDirectory($@"{RootDir}\structured_chats");
        _fs.AddFile($@"{RootDir}\structured_chats\old_file.json", new MockFileData("{}"));

        await _generator.GenerateAsync(RootDir, CancellationToken.None);

        Assert.That(_fs.File.Exists($@"{RootDir}\structured_chats\old_file.json"), Is.False);
    }

    [Test]
    public async Task GenerateAsyncCreatesStructuredChatsDirectoryWhenNotExists()
    {
        _fs.AddFile($@"{RootDir}\phonebook.txt", new MockFileData(""));
        _fs.AddDirectory($@"{RootDir}\chats");

        await _generator.GenerateAsync(RootDir, CancellationToken.None);

        Assert.That(_fs.Directory.Exists($@"{RootDir}\structured_chats"), Is.True);
    }
}
