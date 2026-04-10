using System.IO.Abstractions.TestingHelpers;
using NUnit.Framework;

namespace ContactsRag.Tests.Unit;

[TestFixture]
public class ContentRootResolverTests
{
    [Test]
    public void ResolveWithStructuredChatsDirReturnsParent()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\project\structured_chats");

        var result = ContentRootResolver.Resolve(@"C:\project\sub", fs);

        Assert.That(result, Is.EqualTo(@"C:\project"));
    }

    [Test]
    public void ResolveWithChatsDirReturnsParent()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\project\chats");

        var result = ContentRootResolver.Resolve(@"C:\project\sub", fs);

        Assert.That(result, Is.EqualTo(@"C:\project"));
    }

    [Test]
    public void ResolveWithSystemPromptFileReturnsParent()
    {
        var fs = new MockFileSystem();
        fs.AddFile(@"C:\project\SYSTEM_PROMPT", new MockFileData("prompt content"));

        var result = ContentRootResolver.Resolve(@"C:\project\sub", fs);

        Assert.That(result, Is.EqualTo(@"C:\project"));
    }

    [Test]
    public void ResolveWithNoMarkerReturnsStartDirectory()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\empty\sub");

        var result = ContentRootResolver.Resolve(@"C:\empty\sub", fs);

        Assert.That(result, Is.EqualTo(@"C:\empty\sub"));
    }

    [Test]
    public void ResolveFromNestedSubdirectoryWalksUpToFindMarker()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\root\structured_chats");
        fs.AddDirectory(@"C:\root\a\b\c");

        var result = ContentRootResolver.Resolve(@"C:\root\a\b\c", fs);

        Assert.That(result, Is.EqualTo(@"C:\root"));
    }

    [Test]
    public void ResolveWhenStartDirectoryIsRootReturnsRoot()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(@"C:\project\chats");

        var result = ContentRootResolver.Resolve(@"C:\project", fs);

        Assert.That(result, Is.EqualTo(@"C:\project"));
    }
}
