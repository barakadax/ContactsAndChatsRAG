using System.Globalization;
using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace ContactsRag.Tests.Functional;

internal static class TestDataBuilder
{
    internal static void SetupMockFileSystem(MockFileSystem fs, string rootDir)
    {
        fs.AddDirectory($@"{rootDir}\chats");
        fs.AddFile($@"{rootDir}\phonebook.txt", new MockFileData(BuildPhonebookTxt()));
        fs.AddFile($@"{rootDir}\chats\AAAA", new MockFileData(BuildChatFile("AAAA")));
        fs.AddFile($@"{rootDir}\SYSTEM_PROMPT", new MockFileData("You are a forensic analyst examining phone data."));
    }

    internal static string BuildPhonebookTxt()
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildPhonebookRow("33", "A001", "Alice"));
        sb.AppendLine(BuildPhonebookRow("34", "A001", "Smith"));
        sb.AppendLine(BuildPhonebookRow("37", "A001", "0123456789"));
        sb.AppendLine(BuildPhonebookRow("31", "A001", "1704067200"));
        sb.AppendLine(BuildPhonebookRow("33", "B002", "Bob"));
        sb.AppendLine(BuildPhonebookRow("34", "B002", "Jones"));
        return sb.ToString();
    }

    private static string BuildPhonebookRow(string rowType, string uniqueId, string value)
    {
        var lengthHex = value.Length.ToString("X2", CultureInfo.InvariantCulture);
        var padding = "00";
        return $"{rowType}{uniqueId}{lengthHex}{padding}{value}";
    }

    internal static string BuildChatFile(string _)
    {
        var sb = new StringBuilder();
        AppendMessage(sb, "0000", "2024-03-15 10:00:00", "Hey Alice, how are you?");
        AppendMessage(sb, "A001", "2024-03-15 10:01:00", "Hey Lee! I am doing great, thanks!");
        AppendMessage(sb, "0000", "2024-03-15 10:02:00", "Good to hear! Want to meet up later?");
        AppendMessage(sb, "A001", "2024-03-15 10:03:00", "Sure, let's go to the park at 3pm.");
        return sb.ToString();
    }

    private static void AppendMessage(StringBuilder sb, string userId, string timestamp, string message)
    {
        var tsLen = timestamp.Length.ToString("X2", CultureInfo.InvariantCulture);
        var msgLen = message.Length.ToString("X2", CultureInfo.InvariantCulture);
        var padding = "00";
        sb.Append(userId);
        sb.Append(tsLen);
        sb.Append(padding);
        sb.Append(timestamp);
        sb.Append(msgLen);
        sb.Append(padding);
        sb.Append(message);
    }
}
