using System.Collections.Frozen;
using System.Globalization;
using System.IO.Abstractions;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace ContactsRag;

public sealed class ChatDataGenerator(IFileSystem fileSystem) : IChatDataGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<(int Read, int Structured)> GenerateAsync(string rootDir, CancellationToken cancellationToken)
    {
        var phonebookTxt = fileSystem.Path.Combine(rootDir, "phonebook.txt");
        var chatsDir = fileSystem.Path.Combine(rootDir, "chats");
        var structuredDir = fileSystem.Path.Combine(rootDir, "structured_chats");
        var phoneBookJson = fileSystem.Path.Combine(rootDir, "phone_book.json");

        if (!fileSystem.File.Exists(phonebookTxt))
        {
            AnsiConsole.MarkupLine("[yellow]phonebook.txt not found. Skipping data generation.[/]");
            return (0, 0);
        }

        if (!fileSystem.Directory.Exists(chatsDir))
        {
            AnsiConsole.MarkupLine("[yellow]chats directory not found. Skipping data generation.[/]");
            return (0, 0);
        }

        if (fileSystem.Directory.Exists(structuredDir))
        {
            foreach (var file in fileSystem.Directory.EnumerateFiles(structuredDir))
            {
                fileSystem.File.Delete(file);
            }
        }
        else
        {
            fileSystem.Directory.CreateDirectory(structuredDir);
        }

        var phoneBook = ParsePhoneBook(phonebookTxt);
        var contactChatFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var allChatFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ChatEdge? firstChat = null;
        ChatEdge? lastChat = null;

        var readCount = 0;
        var structuredCount = 0;
        readCount++;

        foreach (var chatPath in fileSystem.Directory.EnumerateFiles(chatsDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            readCount++;

            var fileName = fileSystem.Path.GetFileName(chatPath);
            var result = ProcessChatFile(chatPath, fileName, phoneBook, contactChatFiles, allChatFiles);
            var structured = result.Chat;
            UpdateChatEdges(result, fileName, ref firstChat, ref lastChat);

            var outPath = fileSystem.Path.Combine(structuredDir, fileName);
            await WriteJsonAsync(outPath, structured, cancellationToken);
            structuredCount++;
        }

        phoneBook = phoneBook.ToDictionary(
            kvp => kvp.Key,
            kvp => contactChatFiles.TryGetValue(kvp.Key, out var files)
                ? kvp.Value with { ChatFileIds = [.. files] }
                : kvp.Value with { ChatFileIds = [] },
            StringComparer.OrdinalIgnoreCase);

        var container = BuildPhoneBookContainer(phoneBook, allChatFiles, structuredCount, firstChat, lastChat, chatsDir);
        await WriteJsonAsync(phoneBookJson, container, cancellationToken);
        structuredCount++;

        return (readCount, structuredCount);
    }

    private async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var fs = fileSystem.File.Create(path);
        await JsonSerializer.SerializeAsync(fs, value, JsonOptions, cancellationToken);
    }

    private readonly record struct ChatFileResult(
        StructuredChat Chat,
        string[] ParticipantIds,
        DateTimeOffset? StartTimestamp,
        DateTimeOffset? EndTimestamp);

    private ChatFileResult ProcessChatFile(
        string chatPath,
        string fileName,
        Dictionary<string, Contact> phoneBook,
        Dictionary<string, HashSet<string>> contactChatFiles,
        Dictionary<string, string> allChatFiles)
    {
        var (structured, chatParticipantIds) = ParseChat(chatPath, fileName, phoneBook);
        allChatFiles[fileName] = structured.ChatType;

        var hasStart = TryParseChatTimestamp(structured.Start, out var startTs);
        var hasEnd = TryParseChatTimestamp(structured.End, out var endTs);

        foreach (var pid in chatParticipantIds)
        {
            if (!contactChatFiles.TryGetValue(pid, out var files))
            {
                files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                contactChatFiles[pid] = files;
            }

            files.Add(fileName);
        }

        return new ChatFileResult(
            structured,
            chatParticipantIds,
            hasStart ? startTs : null,
            hasEnd ? endTs : null);
    }

    private static void UpdateChatEdges(ChatFileResult result, string fileName, ref ChatEdge? firstChat, ref ChatEdge? lastChat)
    {
        if (result.StartTimestamp is { } startTs)
        {
            if (firstChat is null || startTs < DateTimeOffset.Parse(firstChat.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal))
                firstChat = new ChatEdge(fileName, startTs.ToString("O", CultureInfo.InvariantCulture));
        }

        if (result.EndTimestamp is { } endTs)
        {
            if (lastChat is null || endTs > DateTimeOffset.Parse(lastChat.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal))
                lastChat = new ChatEdge(fileName, endTs.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    private PhoneBookContainer BuildPhoneBookContainer(
        Dictionary<string, Contact> phoneBook,
        Dictionary<string, string> allChatFiles,
        int structuredCount,
        ChatEdge? firstChat,
        ChatEdge? lastChat,
        string chatsDir)
    {
        var indexed = allChatFiles
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => new ChatFileIndexEntry(kvp.Key, kvp.Value))
            .ToArray();

        return new PhoneBookContainer(
            BuildPhoneBookText(phoneBook),
            phoneBook.Count,
            structuredCount,
            firstChat,
            lastChat,
            indexed,
            BuildChatsIndexText(indexed),
            ExtractOwnerName(chatsDir, phoneBook),
            phoneBook);
    }

    private static string BuildPhoneBookText(Dictionary<string, Contact> phoneBook)
    {
        var sb = new StringBuilder();
        sb.Append("PHONE BOOK\n");
        foreach (var (id, contact) in phoneBook)
        {
            var phones = contact.PhoneNumbers.Count > 0
                ? string.Join(", ", contact.PhoneNumbers)
                : "None";
            sb.Append(CultureInfo.InvariantCulture, $"{id}|{contact.FullName}|{phones}|{contact.Time}\n");
        }

        return sb.ToString();
    }

    private static string BuildChatsIndexText(ChatFileIndexEntry[] indexed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CHATS INDEX");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Total chats: {indexed.Length}");
        sb.AppendLine("Entries (Id - Type):");
        foreach (var e in indexed)
            sb.Append("- ").Append(e.Id).Append(" - ").Append(e.Type).AppendLine();

        return sb.ToString().TrimEnd();
    }

    private static bool TryParseChatTimestamp(string value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value) || value == "N/A") return false;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            timestamp = dto;
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            timestamp = new DateTimeOffset(dt);
            return true;
        }

        return false;
    }

    private string? ExtractOwnerName(string chatsDir, Dictionary<string, Contact> phoneBook)
    {
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var contactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in phoneBook.Values)
        {
            if (!string.IsNullOrWhiteSpace(c.FirstName)) contactNames.Add(c.FirstName);
            if (!string.IsNullOrWhiteSpace(c.LastName)) contactNames.Add(c.LastName);
            if (!string.IsNullOrWhiteSpace(c.FullName)) contactNames.Add(c.FullName);
        }

        var addressPattern = new Regex(
            @"(?:Hey|Hi|Yo|Oh|Listen|Oi|Ay|Hello|Hiya|Sup|Alright|Morning|Evening|Afternoon)\s+([A-Z][a-z]{1,15})(?=[,!.\s?])",
            RegexOptions.Compiled);

        foreach (var chatPath in fileSystem.Directory.EnumerateFiles(chatsDir, "*", SearchOption.AllDirectories))
        {
            var contentStr = fileSystem.File.ReadAllText(chatPath, Encoding.UTF8).Replace("\r\n", "\n");
            var runes = contentStr.EnumerateRunes().ToArray();
            var ptr = 0;

            while (ptr + 8 <= runes.Length)
            {
                try
                {
                    var userId = RunesToString(runes, ptr, 4);
                    var tsLen = ReadHexValue(runes, ptr + 4, 2);
                    ptr += 8 + tsLen;

                    var msgLen = ReadHexValue(runes, ptr, 2);
                    ptr += 4;

                    var msg = RunesToString(runes, ptr, msgLen);
                    ptr += msgLen;

                    if (string.Equals(userId, "0000", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (Match m in addressPattern.Matches(msg))
                    {
                        var candidate = m.Groups[1].Value;
                        if (contactNames.Contains(candidate)) continue;
                        if (IsCommonWord(candidate)) continue;

                        nameCounts.TryGetValue(candidate, out var count);
                        nameCounts[candidate] = count + 1;
                    }
                }
                catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
                {
                    break;
                }
            }
        }

        if (nameCounts.Count == 0) return null;

        return nameCounts.MaxBy(kv => kv.Value).Key;
    }

    private static string RunesToString(ReadOnlySpan<Rune> runes, int start, int length)
    {
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(runes[start + i]);
        return sb.ToString();
    }

    private static int ReadHexValue(ReadOnlySpan<Rune> runes, int start, int length) =>
        int.Parse(RunesToString(runes, start, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static readonly FrozenSet<string> CommonWords = FrozenSet.ToFrozenSet(
    [
        "The", "This", "That", "Yeah", "Yes", "Nah", "But", "And", "For", "Not",
        "Man", "Bro", "Like", "Just", "What", "How", "Why", "Who", "Well", "Sure",
        "Wow", "God", "Damn", "Still", "Thanks", "Thank", "Sorry", "Please", "Really",
        "True", "Right", "Good", "Great", "Nice", "Hey", "Hi", "Yo", "Oh", "Oi",
        "Ay", "Listen", "Look", "See", "Okay", "Ok", "So", "Now", "Then", "Here",
        "There", "Mate", "Dude", "Fam", "Bruv", "Sis", "Honestly", "Literally",
        "Basically", "Actually", "Omg", "Lol", "Haha", "Nope", "Yep",
        "Mr", "Mrs", "Ms", "Dr", "Sir"
    ], StringComparer.OrdinalIgnoreCase);

    private static bool IsCommonWord(string word) => CommonWords.Contains(word);

    private Dictionary<string, Contact> ParsePhoneBook(string path)
    {
        static (string UniqueId, string Value, string Remainder) GetRowValues(string value)
        {
            var length = int.Parse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var uniqueId = value[..4];
            var content = value.Substring(8, length);
            var rest = value[(8 + length)..];
            return (uniqueId, content, rest);
        }

        var first = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var last = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        var phones = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var time = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lineRaw in fileSystem.File.ReadLines(path, Encoding.UTF8))
        {
            var line = lineRaw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var rowType = line[..2];
            var content = line[2..];

            while (!string.IsNullOrEmpty(content))
            {
                try
                {
                    var (uniqueId, value, remainder) = GetRowValues(content);
                    content = remainder;

                    switch (rowType)
                    {
                        case "31":
                            if (long.TryParse(value, out var seconds))
                            {
                                var dto = DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(TimeSpan.Zero);
                                time[uniqueId] = dto.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture);
                            }
                            break;
                        case "33":
                            if (!first.TryGetValue(uniqueId, out var sbf))
                            {
                                sbf = new StringBuilder();
                                first[uniqueId] = sbf;
                            }
                            sbf.Append(value.Replace('\u00A0', ' ')).Append(' ');
                            break;
                        case "34":
                            if (!last.TryGetValue(uniqueId, out var sbl))
                            {
                                sbl = new StringBuilder();
                                last[uniqueId] = sbl;
                            }
                            sbl.Append(value.Replace('\u00A0', ' ')).Append(' ');
                            break;
                        case "37":
                            if (!phones.TryGetValue(uniqueId, out var list))
                            {
                                list = [];
                                phones[uniqueId] = list;
                            }
                            list.Add(value);
                            break;
                    }
                }
                catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
                {
                    break;
                }
            }
        }

        var allIds = new HashSet<string>(first.Keys, StringComparer.OrdinalIgnoreCase);
        allIds.UnionWith(last.Keys);
        allIds.UnionWith(phones.Keys);
        allIds.UnionWith(time.Keys);

        var result = new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in allIds)
        {
            var fn = first.TryGetValue(id, out var sbf) ? sbf.ToString() : string.Empty;
            var ln = last.TryGetValue(id, out var sbl) ? sbl.ToString() : string.Empty;
            fn = string.Join(' ', fn.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            ln = string.Join(' ', ln.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var full = string.Join(' ', (fn + " " + ln).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

            result[id] = new Contact(
                FirstName: fn,
                LastName: ln,
                FullName: full,
                PhoneNumbers: phones.TryGetValue(id, out var p) ? p : [],
                Time: time.TryGetValue(id, out var t) ? t : string.Empty);
        }

        return result;
    }

    private (StructuredChat Chat, string[] ParticipantIds) ParseChat(
        string chatPath,
        string fileName,
        Dictionary<string, Contact> phoneBook)
    {
        var raw = new List<RawChatMessage>();
        var participantIds = new List<string>();

        var contentStr = fileSystem.File.ReadAllText(chatPath, Encoding.UTF8).Replace("\r\n", "\n");
        var runes = contentStr.EnumerateRunes().ToArray();
        var ptr = 0;

        while (ptr + 8 <= runes.Length)
        {
            try
            {
                var userId = RunesToString(runes, ptr, 4);
                participantIds.Add(userId);

                var tsLen = ReadHexValue(runes, ptr + 4, 2);
                ptr += 8;

                var timestamp = RunesToString(runes, ptr, tsLen);
                ptr += tsLen;

                var msgLen = ReadHexValue(runes, ptr, 2);
                ptr += 4;

                var msg = RunesToString(runes, ptr, msgLen);
                ptr += msgLen;

                phoneBook.TryGetValue(userId, out var contact);
                var userName = contact?.FullName
                    ?? (userId == "0000" ? "User of the chat app, refered in start of chats" : "Unknown User");

                raw.Add(new RawChatMessage(userId, userName, timestamp, msg));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
            {
                break;
            }
        }

        raw = [.. raw.OrderBy(m => m.Timestamp)];
        var start = raw.Count > 0 ? raw[0].Timestamp : "N/A";
        var end = raw.Count > 0 ? raw[^1].Timestamp : "N/A";

        var uniq = participantIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var chatType = uniq.Length > 2 ? "Group" : "Private";

        var participants = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var pid in uniq)
        {
            participants[pid] = phoneBook.TryGetValue(pid, out var c)
                ? (object)c
                : "User not in the phone book";
        }

        var sessions = BuildSessions(raw, fileName);

        return (new StructuredChat(
            FileName: fileName,
            ChatType: chatType,
            Start: start,
            End: end,
            Participants: participants,
            Sessions: sessions), uniq);
    }

    private static List<StructuredSession> BuildSessions(List<RawChatMessage> raw, string fileName)
    {
        var sessions = new List<StructuredSession>();
        if (raw.Count == 0) return sessions;

        var currentMessages = new List<RawChatMessage>();
        var currentParticipants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? sessionStart = null;
        string? sessionEnd = null;
        var sessionCounter = 1;

        void Flush()
        {
            if (currentMessages.Count == 0 || sessionStart is null) return;

            var llmTranscript = new StringBuilder();
            foreach (var m in currentMessages)
            {
                var formatted = m.Timestamp;
                if (DateTime.TryParse(m.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    formatted = dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                }

                llmTranscript.Append("**").Append(m.UserId).Append("** **").Append(m.UserName)
                    .Append("** (").Append(formatted).Append("): ").Append(m.Message).Append('\n');
            }

            var text = new StringBuilder();
            text.Append("CONVERSATION LOG\n");
            text.Append(CultureInfo.InvariantCulture, $"Participants: {string.Join(", ", currentParticipants)}\n");
            text.Append(CultureInfo.InvariantCulture, $"Timeframe: {sessionStart} to {sessionEnd}\n");
            text.Append(new string('-', 20)).Append('\n');
            text.Append(llmTranscript.ToString().TrimEnd());

            var metadata = new SessionMetadata(
                SessionId: $"{fileName}_session_{sessionCounter:00}",
                Participants: [.. currentParticipants],
                StartTime: sessionStart,
                EndTime: sessionEnd,
                MessageCount: currentMessages.Count,
                Messages: [.. currentMessages]);

            sessions.Add(new StructuredSession(text.ToString(), metadata));
            sessionCounter++;

            currentMessages.Clear();
            currentParticipants.Clear();
            sessionStart = null;
            sessionEnd = null;
        }

        string? lastDate = null;
        foreach (var msg in raw)
        {
            var msgDate = "N/A";
            if (DateTime.TryParse(msg.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                msgDate = dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            if (lastDate != null && msgDate != "N/A" && msgDate != lastDate)
            {
                Flush();
            }

            lastDate = msgDate == "N/A" ? lastDate : msgDate;

            currentMessages.Add(msg);
            currentParticipants.Add(msg.UserName);
            sessionStart ??= msg.Timestamp;
            sessionEnd = msg.Timestamp;
        }

        Flush();
        return sessions;
    }
}
