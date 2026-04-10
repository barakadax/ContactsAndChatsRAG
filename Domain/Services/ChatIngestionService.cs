using System.IO.Abstractions;
using System.Text;
using System.Text.Json;

namespace ContactsRag;

public sealed class ChatIngestionService(
    IEmbeddingGenerator embeddingGenerator,
    IVectorDatabase vectorDatabase,
    IEmbeddingCache embeddingCache,
    IFileSystem fileSystem,
    string contentRoot) : IChatIngestionService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<(int Read, int Embedded, int Stored, int CacheHits)> IngestAsync(CancellationToken cancellationToken)
    {
        var phoneBookPath = fileSystem.Path.Combine(contentRoot, "phone_book.json");
        var structuredChatsDir = fileSystem.Path.Combine(contentRoot, "structured_chats");
        var systemPromptPath = fileSystem.Path.Combine(contentRoot, "SYSTEM_PROMPT");

        var pbContainer = await LoadPhoneBookAsync(phoneBookPath, cancellationToken);
        var contacts = pbContainer?.Contacts ?? new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);

        var chatFiles = fileSystem.Directory.EnumerateFiles(structuredChatsDir)
            .Where(p => !fileSystem.Path.GetFileName(p).StartsWith("structured_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(fileSystem.Path.GetFileName)
            .ToArray();

        var readCount = 0;
        var embeddedCount = 0;
        var storedCount = 0;
        var cacheHits = 0;

        if (fileSystem.File.Exists(systemPromptPath))
        {
            readCount++;
            var cacheHit = await IngestFileWithCacheAsync(systemPromptPath,
                () => IngestSystemPromptAsync(systemPromptPath, cancellationToken), cancellationToken);
            if (cacheHit) cacheHits++; else embeddedCount++;
            storedCount++;
        }

        if (fileSystem.File.Exists(phoneBookPath))
        {
            readCount++;
            var cacheHit = await IngestFileWithCacheAsync(phoneBookPath,
                () => IngestPhoneBookAsync(pbContainer, cancellationToken), cancellationToken);
            if (cacheHit) cacheHits++; else embeddedCount++;
            storedCount++;
        }

        foreach (var file in chatFiles)
        {
            readCount++;
            var cacheHit = await IngestFileWithCacheAsync(file,
                () => IngestChatFileAsync(file, contacts, cancellationToken), cancellationToken);
            if (cacheHit) cacheHits++; else embeddedCount++;
            storedCount++;
        }

        var (aggEmbedded, aggCacheHit) = await IngestAggregateStatsAsync(chatFiles, contacts, cancellationToken);
        storedCount++;
        if (aggCacheHit) cacheHits++;
        else embeddedCount += aggEmbedded;

        if (pbContainer is not null)
        {
            var (profEmbedded, profCacheHit) = await IngestDatasetProfileAsync(pbContainer, contacts, cancellationToken);
            storedCount++;
            if (profCacheHit) cacheHits++;
            else embeddedCount += profEmbedded;
        }

        return (readCount, embeddedCount, storedCount, cacheHits);
    }


    private async Task<bool> IngestFileWithCacheAsync(
        string filePath,
        Func<Task<IReadOnlyList<VectorDocument>>> embedFunc,
        CancellationToken cancellationToken)
    {
        var hash = embeddingCache.ComputeHash(filePath);
        var cached = await embeddingCache.TryLoadAsync(hash, cancellationToken);
        if (cached is not null)
        {
            await vectorDatabase.UpsertAsync(cached, cancellationToken);
            return true;
        }

        var records = await embedFunc();
        await embeddingCache.SaveAsync(hash, records, cancellationToken);
        return false;
    }

    private async Task<IReadOnlyList<VectorDocument>> IngestSystemPromptAsync(string path, CancellationToken cancellationToken)
    {
        var content = await fileSystem.File.ReadAllTextAsync(path, cancellationToken);
        var vector = await embeddingGenerator.GenerateAsync(content, cancellationToken);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetadataKeys.ChatFile] = MetadataKeys.ChatFileSystemPrompt,
            [MetadataKeys.Type] = MetadataKeys.TypeSystemPrompt
        };

        var records = new[] { new VectorDocument("system_prompt", vector, content, metadata) };
        await vectorDatabase.UpsertAsync(records, cancellationToken);
        return records;
    }

    private async Task<IReadOnlyList<VectorDocument>> IngestPhoneBookAsync(PhoneBookContainer? phoneBook, CancellationToken cancellationToken)
    {
        var contacts = phoneBook?.Contacts ?? new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);

        var texts = new List<string>(contacts.Count + 1);
        var ids = new List<string>(contacts.Count + 1);
        var metadataList = new List<Dictionary<string, string>>(contacts.Count + 1);

        if (phoneBook is not null)
        {
            var indexText = CreatePhoneBookIndexText(phoneBook);
            texts.Add(indexText);
            ids.Add("phonebook:index");
            metadataList.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.ChatFile] = MetadataKeys.ChatFilePhoneBook,
                [MetadataKeys.Type] = MetadataKeys.TypePhoneBookIndex
            });
        }

        foreach (var (id, contact) in contacts)
        {
            var phones = contact.PhoneNumbers.Count > 0
                ? string.Join(", ", contact.PhoneNumbers)
                : "none";

            var text = $"Contact: {contact.FullName} (ID: {id})\nFirst Name: {contact.FirstName}\nLast Name: {contact.LastName}\nPhone Numbers: {phones}\nAdded: {contact.Time}";

            texts.Add(text);
            ids.Add($"phonebook:{id}");
            metadataList.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.ChatFile] = MetadataKeys.ChatFilePhoneBook,
                [MetadataKeys.Type] = MetadataKeys.TypeContact,
                [MetadataKeys.UserId] = id,
                [MetadataKeys.FirstName] = contact.FirstName,
                [MetadataKeys.UserName] = contact.FullName,
                [MetadataKeys.Participants] = contact.FullName
            });
        }

        var embeddings = await embeddingGenerator.GenerateBatchAsync(texts, cancellationToken);

        var records = texts.Select((text, i) =>
            new VectorDocument(ids[i], embeddings[i], text, metadataList[i])).ToList();

        await vectorDatabase.UpsertAsync(records, cancellationToken);
        return records;
    }

    private static string CreatePhoneBookIndexText(PhoneBookContainer phoneBook)
    {
        static string FormatChatFileIds(IReadOnlyList<ChatFileIndexEntry> entries)
        {
            if (entries.Count == 0) return "(none)";
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                sb.Append("- ").Append(e.Id).Append(" - ").Append(e.Type).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        var firstChat = phoneBook.FirstChat is not null
            ? $"{phoneBook.FirstChat.ChatFileId} at {phoneBook.FirstChat.Timestamp}"
            : "unknown";
        var lastChat = phoneBook.LastChat is not null
            ? $"{phoneBook.LastChat.ChatFileId} at {phoneBook.LastChat.Timestamp}"
            : "unknown";

        return $$"""
        PHONE_BOOK INDEX
        count: {{phoneBook.Count}}
        NOTE: When listing all contact names, you MUST list exactly {{phoneBook.Count}} names — verify your list matches this count before answering.
        chat_count: {{phoneBook.ChatCount}}
        first_chat: {{firstChat}}
        last_chat: {{lastChat}}
        chat_file_ids:
        {{FormatChatFileIds(phoneBook.ChatFileIds)}}

        chats_summary:
        {{phoneBook.ChatsSummary}}
        """;
    }

    private async Task<IReadOnlyList<VectorDocument>> IngestChatFileAsync(
        string file,
        Dictionary<string, Contact> contacts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var chat = await LoadStructuredChatAsync(file, cancellationToken);
        var records = await CreateVectorDocumentsAsync(chat, contacts, cancellationToken);
        await vectorDatabase.UpsertAsync(records, cancellationToken);
        return records;
    }

    private async Task<PhoneBookContainer?> LoadPhoneBookAsync(string path, CancellationToken cancellationToken)
    {
        await using var fs = fileSystem.File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PhoneBookContainer>(fs, JsonOptions, cancellationToken);
    }

    private async Task<StructuredChat> LoadStructuredChatAsync(string path, CancellationToken cancellationToken)
    {
        await using var fs = fileSystem.File.OpenRead(path);
        var chat = await JsonSerializer.DeserializeAsync<StructuredChat>(fs, JsonOptions, cancellationToken);
        return chat ?? throw new InvalidDataException($"Unable to parse structured chat JSON: {path}");
    }

    private async Task<IReadOnlyList<VectorDocument>> CreateVectorDocumentsAsync(
        StructuredChat chat,
        Dictionary<string, Contact> contacts,
        CancellationToken cancellationToken)
    {
        var chunks = chat.Sessions.Select(s => EnrichSession(chat, s, contacts)).ToArray();
        var texts = chunks.Select(c => c.Text).ToArray();

        var embeddings = await embeddingGenerator.GenerateBatchAsync(texts, cancellationToken);

        var list = new List<VectorDocument>(chunks.Length);
        for (var i = 0; i < chunks.Length; i++)
        {
            var chunk = chunks[i];
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataKeys.ChatFile] = chunk.ChatFile,
                [MetadataKeys.SessionId] = chunk.SessionId,
                [MetadataKeys.ChatType] = chat.ChatType,
                [MetadataKeys.Participants] = string.Join("|", chunk.Participants),
                [MetadataKeys.UserIds] = string.Join("|", chunk.MessageUserIds),
                [MetadataKeys.UserNames] = string.Join("|", chunk.MessageUserNames),
                [MetadataKeys.MessageCount] = chunk.MessageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };

            if (chunk.Start.HasValue)
            {
                var start = chunk.Start.Value;
                metadata[MetadataKeys.SessionStart] = start.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                metadata[MetadataKeys.SessionDate] = start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                metadata[MetadataKeys.SessionYearMonth] = start.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (chunk.End.HasValue)
            {
                metadata[MetadataKeys.SessionEnd] = chunk.End.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            }

            list.Add(new VectorDocument(chunk.Id, embeddings[i], chunk.Text, metadata));
        }

        return list;
    }

    private static RagChunk EnrichSession(
        StructuredChat chat,
        StructuredSession session,
        Dictionary<string, Contact> contacts)
    {
        static DateTimeOffset? TryParseIso(string value)
            => DateTimeOffset.TryParse(value, out var dto) ? dto : null;

        var start = TryParseIso(session.Metadata.StartTime ?? "");
        var end = TryParseIso(session.Metadata.EndTime ?? "");

        var lines = session.Text.Split('\n');
        var sb = new StringBuilder(session.Text.Length + 64);

        foreach (var line in lines)
        {
            if (line.StartsWith("**", StringComparison.Ordinal) && line.Length >= 6)
            {
                var idStart = 2;
                var idEnd = line.IndexOf("**", idStart, StringComparison.Ordinal);
                if (idEnd > idStart)
                {
                    var id = line.AsSpan(idStart, idEnd - idStart).ToString();
                    var name = ResolveName(id, contacts);

                    sb.Append("**").Append(id).Append("** **").Append(name).Append("**");

                    var afterSecondBold = line.IndexOf("**", idEnd + 2, StringComparison.Ordinal);
                    afterSecondBold = afterSecondBold < 0 ? -1 : line.IndexOf("**", afterSecondBold + 2, StringComparison.Ordinal);

                    if (afterSecondBold >= 0)
                    {
                        var idx = afterSecondBold + 2;
                        if (idx < line.Length)
                        {
                            sb.Append(line.AsSpan(idx));
                        }
                    }
                    else
                    {
                        sb.Append(line.AsSpan(idEnd + 2));
                    }

                    sb.Append('\n');
                    continue;
                }
            }

            sb.Append(line).Append('\n');
        }

        var enrichedText = sb.ToString().TrimEnd('\n');

        var msgUserIds = session.Metadata.Messages
            .Select(m => m.UserId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var msgUserNames = msgUserIds.Select(id => ResolveName(id, contacts)).ToArray();

        var participants = msgUserNames.Length == 0
            ? [.. session.Metadata.Participants]
            : msgUserNames;

        return new RagChunk(
            Id: $"{chat.FileName}:{session.Metadata.SessionId}",
            ChatFile: chat.FileName,
            SessionId: session.Metadata.SessionId,
            Text: enrichedText,
            Participants: participants,
            MessageUserIds: msgUserIds,
            MessageUserNames: msgUserNames,
            Start: start,
            End: end,
            MessageCount: session.Metadata.MessageCount);
    }

    private static string ResolveName(string userId, Dictionary<string, Contact> contacts)
    {
        if (string.Equals(userId, "0000", StringComparison.OrdinalIgnoreCase))
        {
            return "User";
        }

        if (contacts.TryGetValue(userId, out var c))
        {
            return string.IsNullOrWhiteSpace(c.FullName) ? userId : c.FullName;
        }

        return userId;
    }

    private async Task<(int Embedded, bool CacheHit)> IngestAggregateStatsAsync(
        string[] chatFiles,
        Dictionary<string, Contact> contacts,
        CancellationToken cancellationToken)
    {
        var chats = new List<StructuredChat>(chatFiles.Length);
        foreach (var file in chatFiles)
        {
            var chat = await LoadStructuredChatAsync(file, cancellationToken);
            chats.Add(chat);
        }

        var statsText = BuildAggregateStatsText(chats, contacts);

        var hash = embeddingCache.ComputeHashFromContent("aggregate:stats", statsText);
        var cached = await embeddingCache.TryLoadAsync(hash, cancellationToken);
        if (cached is not null)
        {
            await vectorDatabase.UpsertAsync(cached, cancellationToken);
            return (0, true);
        }

        var vector = await embeddingGenerator.GenerateAsync(statsText, cancellationToken);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetadataKeys.ChatFile] = MetadataKeys.ChatFileAggregate,
            [MetadataKeys.Type] = MetadataKeys.TypeAggregateStats
        };
        var records = new[] { new VectorDocument("aggregate:stats", vector, statsText, metadata) };
        await embeddingCache.SaveAsync(hash, records, cancellationToken);
        await vectorDatabase.UpsertAsync(records, cancellationToken);
        return (1, false);
    }

    private static string BuildAggregateStatsText(
        IReadOnlyList<StructuredChat> chats,
        Dictionary<string, Contact> contacts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("AGGREGATE FORENSIC STATS");
        sb.AppendLine();

        AppendPerChatMessageTotals(sb, chats, contacts);

        var dateToFiles = BuildDateToFilesMap(chats);

        AppendMultiContactDays(sb, dateToFiles, contacts);
        AppendMonthlyCoverage(sb, dateToFiles);

        return sb.ToString().TrimEnd();
    }

    private static void AppendPerChatMessageTotals(
        StringBuilder sb,
        IReadOnlyList<StructuredChat> chats,
        Dictionary<string, Contact> contacts)
    {
        sb.AppendLine("== Total Messages per Chat File ==");
        foreach (var chat in chats.OrderBy(c => c.FileName, StringComparer.Ordinal))
        {
            var totalMessages = chat.Sessions.Sum(s => s.Metadata.MessageCount);
            var totalSessions = chat.Sessions.Count;

            var contactName = chat.Participants.Keys
                .Where(id => !string.Equals(id, "0000", StringComparison.OrdinalIgnoreCase))
                .Select(id => contacts.TryGetValue(id, out var c) ? c.FullName : id)
                .FirstOrDefault(chat.FileName);

            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"Contact: {contactName} | File: {chat.FileName} | Total messages: {totalMessages} | Sessions: {totalSessions}");
        }

        sb.AppendLine();
    }

    private static Dictionary<string, HashSet<string>> BuildDateToFilesMap(IReadOnlyList<StructuredChat> chats)
    {
        var dateToFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var chat in chats)
        {
            foreach (var session in chat.Sessions)
            {
                if (string.IsNullOrEmpty(session.Metadata.StartTime)) continue;
                if (!DateTimeOffset.TryParse(session.Metadata.StartTime, out var dto)) continue;
                var date = dto.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                if (!dateToFiles.TryGetValue(date, out var files))
                {
                    files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    dateToFiles[date] = files;
                }
                files.Add(chat.FileName);
            }
        }
        return dateToFiles;
    }

    private static void AppendMultiContactDays(
        StringBuilder sb,
        Dictionary<string, HashSet<string>> dateToFiles,
        Dictionary<string, Contact> contacts)
    {
        var multiContactDays = dateToFiles
            .Where(kv => kv.Value.Count >= 3)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"== Days with 3+ Different Contacts ({multiContactDays.Count} total) ==");
        foreach (var (date, files) in multiContactDays)
        {
            var names = files
                .Select(fid => contacts.TryGetValue(fid, out var c) ? c.FullName : fid)
                .OrderBy(n => n, StringComparer.Ordinal);
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"{date}: {string.Join(", ", names)}");
        }

        sb.AppendLine();
        if (multiContactDays.Count > 0)
        {
            var firstMulti = multiContactDays[0].Key[..7];
            var lastMulti = multiContactDays[^1].Key[..7];
            var periodDesc = firstMulti == lastMulti ? firstMulti : $"{firstMulti} through {lastMulti}";
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Yes — there were {multiContactDays.Count} days with 3 or more different contacts, concentrated in the {periodDesc} period.");
        }
        else
        {
            sb.AppendLine("No days found with 3 or more different contacts.");
        }

        sb.AppendLine();
    }

    private static void AppendMonthlyCoverage(
        StringBuilder sb,
        Dictionary<string, HashSet<string>> dateToFiles)
    {
        var monthsWithActivity = dateToFiles.Keys
            .Select(d => d[..7])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        sb.AppendLine("== Monthly Activity Coverage ==");

        var years = dateToFiles.Keys
            .Select(d => d[..4])
            .Distinct()
            .OrderBy(y => y, StringComparer.Ordinal)
            .ToList();

        var allExpectedMonths = years
            .SelectMany(y => Enumerable.Range(1, 12).Select(m => $"{y}-{m:D2}"))
            .ToList();

        var coverage = allExpectedMonths.Select(m => $"{m}: {(monthsWithActivity.Contains(m) ? "\u2713" : "\u2717")}");
        sb.AppendLine(string.Join("  ", coverage));
        var missing = allExpectedMonths.Where(m => !monthsWithActivity.Contains(m)).ToList();
        var yearRange = years.Count == 0 ? "N/A" : years.Count == 1 ? years[0] : $"{years[0]}\u2013{years[^1]}";
        sb.AppendLine(missing.Count == 0
            ? $"All {allExpectedMonths.Count} months of {yearRange} (January through December) have chat activity."
            : $"Months with no activity in {yearRange}: {string.Join(", ", missing)}");
    }

    private async Task<(int Embedded, bool CacheHit)> IngestDatasetProfileAsync(
        PhoneBookContainer phoneBook,
        Dictionary<string, Contact> contacts,
        CancellationToken cancellationToken)
    {
        var profileText = BuildDatasetProfileText(phoneBook, contacts);

        var hash = embeddingCache.ComputeHashFromContent("dataset:profile", profileText);
        var cached = await embeddingCache.TryLoadAsync(hash, cancellationToken);
        if (cached is not null)
        {
            await vectorDatabase.UpsertAsync(cached, cancellationToken);
            return (0, true);
        }

        var vector = await embeddingGenerator.GenerateAsync(profileText, cancellationToken);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MetadataKeys.ChatFile] = MetadataKeys.ChatFileDatasetProfile,
            [MetadataKeys.Type] = MetadataKeys.TypeDatasetProfile
        };
        var records = new[] { new VectorDocument("dataset:profile", vector, profileText, metadata) };
        await embeddingCache.SaveAsync(hash, records, cancellationToken);
        await vectorDatabase.UpsertAsync(records, cancellationToken);
        return (1, false);
    }

    private static string BuildDatasetProfileText(
        PhoneBookContainer phoneBook,
        Dictionary<string, Contact> contacts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DATASET PROFILE \u2014 Authoritative metadata for this forensic dataset.");
        sb.AppendLine("Use these values directly for owner name, contact counts, chat counts, date range, and chat type questions.");
        sb.AppendLine();

        var ownerName = phoneBook.OwnerName ?? "Unknown";
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"Owner: {ownerName} (ID: 0000)");

        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"Total contacts in phone book: {phoneBook.Count}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"Total chats: {phoneBook.ChatCount}");

        var typeCounts = phoneBook.ChatFileIds
            .GroupBy(e => e.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var privateCount = typeCounts.GetValueOrDefault("Private");
        var groupCount = typeCounts.GetValueOrDefault("Group");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"Chat types: {privateCount} Private, {groupCount} Group");

        var firstTs = phoneBook.FirstChat?.Timestamp ?? "unknown";
        var lastTs = phoneBook.LastChat?.Timestamp ?? "unknown";
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"Date range: {firstTs} to {lastTs}");

        if (phoneBook.FirstChat is not null)
        {
            var firstFileId = phoneBook.FirstChat.ChatFileId;
            var firstName = contacts.TryGetValue(firstFileId, out var fc) ? fc.FullName : firstFileId;
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"First chat: {firstName} (file {firstFileId}) at {phoneBook.FirstChat.Timestamp}");
        }

        if (phoneBook.LastChat is not null)
        {
            var lastFileId = phoneBook.LastChat.ChatFileId;
            var lastName = contacts.TryGetValue(lastFileId, out var lc) ? lc.FullName : lastFileId;
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"Last chat: {lastName} (file {lastFileId}) at {phoneBook.LastChat.Timestamp}");
        }

        sb.AppendLine();

        sb.AppendLine("Chat file IDs:");
        foreach (var entry in phoneBook.ChatFileIds)
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"  - {entry.Id} ({entry.Type})");
        }

        sb.AppendLine();

        var activeContacts = contacts
            .Where(kv => kv.Value.ChatFileIds is { Count: > 0 })
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"Active contacts (with chats): {activeContacts.Count}");
        foreach (var (id, contact) in activeContacts)
        {
            var files = string.Join(", ", contact.ChatFileIds ?? []);
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"  - {contact.FullName} (ID: {id}) \u2014 chats: {files}");
        }

        var inactiveCount = contacts.Count(kv => kv.Value.ChatFileIds is not { Count: > 0 });
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"Inactive contacts (no chats): {inactiveCount}");

        return sb.ToString().TrimEnd();
    }
}
