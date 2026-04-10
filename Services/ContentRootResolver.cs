using System.IO.Abstractions;

namespace ContactsRag;

internal static class ContentRootResolver
{
    internal static string Resolve(string startDirectory, IFileSystem? fileSystem = null)
    {
        var fs = fileSystem ?? new FileSystem();
        var dir = fs.DirectoryInfo.New(startDirectory);
        string[] directoryMarkers = ["structured_chats", "chats"];
        while (dir is not null)
        {
            if (directoryMarkers.Any(m => fs.Directory.Exists(fs.Path.Combine(dir.FullName, m)))
                || fs.File.Exists(fs.Path.Combine(dir.FullName, "SYSTEM_PROMPT")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return startDirectory;
    }
}
