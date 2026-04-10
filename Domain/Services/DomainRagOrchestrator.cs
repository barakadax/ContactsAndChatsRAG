using System.Globalization;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Polly;
using Spectre.Console;

namespace ContactsRag;

public sealed partial class DomainRagOrchestrator(
    RagOrchestrator inner,
    IEmbeddingGenerator embeddingGenerator,
    IVectorDatabase vectorDatabase,
    Kernel kernel,
    IOptions<RagEngineOptions> options,
    ResiliencePipeline resiliencePipeline,
    IFileSystem fileSystem,
    string contentRoot) : IRagOrchestrator
{
    private string? _systemPromptText;

    private static readonly HashSet<string> NameLinkingWords =
        new(StringComparer.OrdinalIgnoreCase) { "with", "about", "for", "from", "to", "by", "between" };

    private static readonly HashSet<string> SearchExcludedTerms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "what", "who", "when", "where", "why", "how", "did", "does", "do", "is", "are",
            "was", "were", "have", "has", "had", "the", "a", "an", "and", "or", "but", "in",
            "on", "at", "of", "to", "for", "with", "about", "from", "by", "that", "this",
            "his", "her", "their", "its", "our", "your", "my", "he", "she", "they", "we",
            "all", "any", "some", "many", "most", "not", "no", "yes", "can", "could",
            "would", "should", "will", "may", "might", "shall", "name", "tell", "give",
            "list", "find", "show", "get", "let", "ever", "been", "also", "just",
        };

    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    public async Task<RagResult> AskStreamingAsync(
        string userQuestion,
        IReadOnlyList<ConversationTurn>? history,
        RagQueryOptions? options,
        CancellationToken cancellationToken)
    {
        var opts = options ?? new RagQueryOptions();
        opts = await ApplyParticipantTopicConstraintsAsync(userQuestion, opts, cancellationToken);

        if (IsPhoneBookIndexQuestion(userQuestion))
        {
            return await HandlePhoneBookIndexQueryAsync(userQuestion, history, cancellationToken);
        }

        var singleFileTopicQuery = IsTopicListQuestion(userQuestion) && !string.IsNullOrWhiteSpace(opts.ChatFile);
        opts = IsPersonMentionQuery(userQuestion)
            ? opts with { TopK = Math.Max(opts.TopK, 500) }
            : (IsAggregateQuery(userQuestion) || IsTopicListQuestion(userQuestion))
                ? opts with { TopK = Math.Max(opts.TopK, singleFileTopicQuery ? 1000 : 150) }
                : opts with { TopK = Math.Max(opts.TopK, 20) };

        var systemPrompt = await LoadSystemPromptAsync();

        AnsiConsole.MarkupLine("[grey]Generating HyDE draft...[/]");
        var hydeText = await inner.EmbedHyDeQueryAsync(
            BuildHyDePrompt(userQuestion, systemPrompt), cancellationToken);

        AnsiConsole.MarkupLine("[grey]Embedding HyDE draft for retrieval...[/]");

        var filter = BuildMetadataFilter(opts);
        var yearMonth = ParseYearMonth(userQuestion);
        if (yearMonth is not null)
        {
            filter ??= new(StringComparer.OrdinalIgnoreCase);
            filter[MetadataKeys.SessionYearMonth] = yearMonth;
        }

        var retrieved = await vectorDatabase.SearchAsync(hydeText, opts.TopK, filter, cancellationToken);

        var merged = retrieved.ToDictionary(r => r.Id, StringComparer.Ordinal);

        await MergeKeyTermFallbackAsync(userQuestion, retrieved, merged, cancellationToken);
        await MergePhoneBookContactsAsync(userQuestion, merged, cancellationToken);
        await MergeAggregateStatsAsync(userQuestion, merged, cancellationToken);
        await RetrieveSystemPromptAndProfileAsync(merged, cancellationToken);

        retrieved = [.. merged.Values.OrderByDescending(r => r.Score).Take(opts.TopK + 50)];

        if (retrieved.Count == 0 && filter is not null)
        {
            AnsiConsole.MarkupLine("[grey]No results with metadata filter; retrying without filter...[/]");
            retrieved = await vectorDatabase.SearchAsync(hydeText, opts.TopK, null, cancellationToken);
        }

        retrieved = await BackfillTemporalGapsAsync(userQuestion, yearMonth, retrieved, cancellationToken);

        var sourceFiles = RagOrchestrator.ExtractSourceFiles(retrieved);

        string? preComputedAggregate = null;
        if (IsBusiestDayQuery(userQuestion))
        {
            AnsiConsole.MarkupLine("[grey]Computing busiest day aggregation...[/]");
            preComputedAggregate = await ComputeBusiestDayAsync(IsSingleChatMaxQuery(userQuestion), cancellationToken);
        }

        var context = BuildGroundedContext(userQuestion, systemPrompt, retrieved, preComputedAggregate);
        var chatHistory = BuildChatHistory(context, history, userQuestion);

        AnsiConsole.MarkupLine("[grey]Generating grounded answer...[/]");
        var stream = StreamFromKernelAsync(chatHistory, cancellationToken);
        return new RagResult(stream, sourceFiles);
    }

    private async Task MergeKeyTermFallbackAsync(
        string userQuestion,
        IReadOnlyList<VectorSearchResult> retrieved,
        Dictionary<string, VectorSearchResult> merged,
        CancellationToken cancellationToken)
    {
        var keyTerms = ExtractKeyTerms(userQuestion);
        if (keyTerms.Count == 0) return;

        var anyFound = retrieved.Any(r =>
            keyTerms.Any(t => r.Text.Contains(t, StringComparison.OrdinalIgnoreCase)));

        if (anyFound) return;

        AnsiConsole.MarkupLine("[grey]Key terms not found in results; running keyword fallback...[/]");
        var rawVector = await embeddingGenerator.GenerateAsync(userQuestion, cancellationToken);
        var fallback = await vectorDatabase.SearchAsync(rawVector, 200, null, cancellationToken);
        foreach (var r in fallback.Where(r =>
            keyTerms.Any(t => r.Text.Contains(t, StringComparison.OrdinalIgnoreCase))))
        {
            merged.TryAdd(r.Id, r);
        }
    }

    private async Task MergePhoneBookContactsAsync(
        string userQuestion,
        Dictionary<string, VectorSearchResult> merged,
        CancellationToken cancellationToken)
    {
        if (!NeedsPhoneBookContacts(userQuestion)) return;

        var pbVector = await embeddingGenerator.GenerateAsync(
            "PHONE BOOK contacts list and phone numbers", cancellationToken);

        var pbHits = await vectorDatabase.SearchAsync(
            pbVector, topK: 50,
            CreateFilter(MetadataKeys.Type, MetadataKeys.TypeContact),
            cancellationToken);

        foreach (var r in pbHits) merged.TryAdd(r.Id, r);
    }

    private async Task MergeAggregateStatsAsync(
        string userQuestion,
        Dictionary<string, VectorSearchResult> merged,
        CancellationToken cancellationToken)
    {
        if (!IsTotalMessageCountQuery(userQuestion) && !IsDailyMultiContactQuery(userQuestion) && !IsMonthlyActivityQuery(userQuestion))
            return;

        var aggVector = await embeddingGenerator.GenerateAsync(
            "aggregate forensic stats total messages sessions contacts daily activity monthly coverage", cancellationToken);
        var aggHits = await vectorDatabase.SearchAsync(
            aggVector, topK: 1,
            CreateFilter(MetadataKeys.Type, MetadataKeys.TypeAggregateStats),
            cancellationToken);
        foreach (var r in aggHits) merged.TryAdd(r.Id, r);
    }

    private async Task RetrieveSystemPromptAndProfileAsync(
        Dictionary<string, VectorSearchResult> merged,
        CancellationToken cancellationToken)
    {
        var sysVector = await embeddingGenerator.GenerateAsync("SYSTEM_PROMPT", cancellationToken);
        var sysHits = await vectorDatabase.SearchAsync(
            sysVector, topK: 1,
            CreateFilter(MetadataKeys.Type, MetadataKeys.TypeSystemPrompt),
            cancellationToken);

        foreach (var r in sysHits) merged.TryAdd(r.Id, r);

        var profileVector = await embeddingGenerator.GenerateAsync("dataset profile owner contacts chats date range", cancellationToken);
        var profileHits = await vectorDatabase.SearchAsync(
            profileVector, topK: 1,
            CreateFilter(MetadataKeys.Type, MetadataKeys.TypeDatasetProfile),
            cancellationToken);

        foreach (var r in profileHits) merged.TryAdd(r.Id, r);
    }

    private async Task<IReadOnlyList<VectorSearchResult>> BackfillTemporalGapsAsync(
        string userQuestion,
        string? yearMonth,
        IReadOnlyList<VectorSearchResult> retrieved,
        CancellationToken cancellationToken)
    {
        if (!IsAggregateQuery(userQuestion) || yearMonth is null || retrieved.Count == 0)
            return retrieved;

        var coveredFiles = new HashSet<string>(
            retrieved
                .Select(r => r.Metadata.GetValueOrDefault(MetadataKeys.ChatFile, ""))
                .Where(f => !string.IsNullOrEmpty(f)),
            StringComparer.OrdinalIgnoreCase);

        var broadVector = await embeddingGenerator.GenerateAsync("chat session conversation messages", cancellationToken);
        var allTemporalHits = await vectorDatabase.SearchAsync(
            broadVector, topK: 10_000,
            CreateFilter(MetadataKeys.SessionYearMonth, yearMonth),
            cancellationToken);

        var allFilesInPeriod = allTemporalHits
            .Select(r => r.Metadata.GetValueOrDefault(MetadataKeys.ChatFile, ""))
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missingFiles = allFilesInPeriod.Where(f => !coveredFiles.Contains(f)).ToList();

        if (missingFiles.Count == 0)
            return retrieved;

        AnsiConsole.MarkupLine($"[grey]Coverage gap: backfilling {missingFiles.Count} missing chat file(s)...[/]");
        var backfill = allTemporalHits
            .Where(r => missingFiles.Contains(r.Metadata.GetValueOrDefault(MetadataKeys.ChatFile, ""), StringComparer.OrdinalIgnoreCase))
            .GroupBy(r => r.Metadata.GetValueOrDefault(MetadataKeys.ChatFile, ""), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.Score).First());

        var mutableRetrieved = retrieved.ToList();
        foreach (var r in backfill)
        {
            mutableRetrieved.Add(r);
        }
        return mutableRetrieved;
    }

    private async Task<RagResult> HandlePhoneBookIndexQueryAsync(
        string userQuestion,
        IReadOnlyList<ConversationTurn>? history,
        CancellationToken cancellationToken)
    {
        var systemPromptText = await LoadSystemPromptAsync();

        var pbSeedVector = await embeddingGenerator.GenerateAsync(
            "chat_file_ids chats_summary chat_count first_chat last_chat from phone_book.json", cancellationToken);

        var pbHits = await vectorDatabase.SearchAsync(
            pbSeedVector, topK: 5,
            CreateFilter(MetadataKeys.Type, MetadataKeys.TypePhoneBookIndex,
                         MetadataKeys.ChatFile, MetadataKeys.ChatFilePhoneBook),
            cancellationToken);

        var sysHits = Array.Empty<VectorSearchResult>();
        if (!string.IsNullOrWhiteSpace(systemPromptText))
        {
            var sysVector = await embeddingGenerator.GenerateAsync("SYSTEM_PROMPT", cancellationToken);
            sysHits = [.. (await vectorDatabase.SearchAsync(
                sysVector, topK: 1,
                CreateFilter(MetadataKeys.Type, MetadataKeys.TypeSystemPrompt),
                cancellationToken))];
        }

        var profileVector = await embeddingGenerator.GenerateAsync("dataset profile owner contacts chats date range", cancellationToken);
        var profileHits = await vectorDatabase.SearchAsync(
            profileVector, topK: 1,
            CreateFilter(MetadataKeys.Type, MetadataKeys.TypeDatasetProfile),
            cancellationToken);

        var ctx = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemPromptText))
        {
            ctx.AppendLine(systemPromptText);
            ctx.AppendLine();
        }

        var datasetProfile = profileHits.Count > 0 ? profileHits[0] : null;
        if (datasetProfile is not null)
        {
            ctx.AppendLine("<DATASET_PROFILE>");
            ctx.AppendLine(datasetProfile.Text);
            ctx.AppendLine("</DATASET_PROFILE>");
            ctx.AppendLine();
        }

        ctx.AppendLine("You are answering questions using ONLY the provided phone_book.json excerpts and the DATASET PROFILE.");
        ctx.AppendLine("If the answer is not supported by the excerpts, say you don't know.");
        ctx.AppendLine();
        ctx.AppendLine("<EXCERPTS>");

        foreach (var r in pbHits.Concat(sysHits))
        {
            ctx.AppendLine(CultureInfo.InvariantCulture, $"--- id={r.Id} score={r.Score:F3} chat={r.Metadata.GetValueOrDefault(MetadataKeys.ChatFile, "?")} session={r.Metadata.GetValueOrDefault(MetadataKeys.SessionId, "?")} ---");
            ctx.AppendLine(r.Text);
            ctx.AppendLine();
        }

        ctx.AppendLine("</EXCERPTS>");

        var chatHistory = BuildChatHistory(ctx.ToString(), history, userQuestion);
        var stream = StreamFromKernelAsync(chatHistory, cancellationToken);
        return new RagResult(stream, ["SYSTEM_PROMPT", "phone_book"]);
    }

    private async Task<RagQueryOptions> ApplyParticipantTopicConstraintsAsync(
        string userQuestion,
        RagQueryOptions opts,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userQuestion)) return opts;
        if (!string.IsNullOrWhiteSpace(opts.ChatFile)) return opts;
        if (!IsTopicListQuestion(userQuestion)) return opts;

        var firstName = ExtractSingleNameCandidate(userQuestion);
        if (firstName is null) return opts;

        var seed = await embeddingGenerator.GenerateAsync($"phone book contact {firstName}", cancellationToken);

        var hits = await vectorDatabase.SearchAsync(
            seed, topK: 25,
            CreateFilter(MetadataKeys.Type, MetadataKeys.TypeContact),
            cancellationToken);

        var matches = hits
            .Where(h => h.Metadata.TryGetValue(MetadataKeys.FirstName, out var fn)
                        && fn.Equals(firstName, StringComparison.OrdinalIgnoreCase))
            .Select(h => (h.Metadata.TryGetValue("contact_id", out var cid) ? cid : null)
                         ?? InferContactIdFromVectorId(h.Id))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count == 1 ? opts with { ChatFile = matches[0] } : opts;
    }

    private async Task<string> LoadSystemPromptAsync()
    {
        if (_systemPromptText is not null) return _systemPromptText;

        var path = fileSystem.Path.Combine(contentRoot, "SYSTEM_PROMPT");
        _systemPromptText = fileSystem.File.Exists(path)
            ? await fileSystem.File.ReadAllTextAsync(path)
            : options.Value.SystemPromptTemplate;
        return _systemPromptText;
    }

    internal static string BuildGroundedContext(
        string userQuestion,
        string systemPrompt,
        IReadOnlyList<VectorSearchResult> retrieved,
        string? preComputedAggregate = null)
    {
        var ctx = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            ctx.AppendLine(systemPrompt);
            ctx.AppendLine();
        }

        var datasetProfile = retrieved.FirstOrDefault(r =>
            r.Metadata.GetValueOrDefault(MetadataKeys.Type, "") == MetadataKeys.TypeDatasetProfile);
        if (datasetProfile is not null)
        {
            ctx.AppendLine("<DATASET_PROFILE>");
            ctx.AppendLine(datasetProfile.Text);
            ctx.AppendLine("</DATASET_PROFILE>");
            ctx.AppendLine();
        }

        if (preComputedAggregate is not null)
        {
            ctx.AppendLine("CRITICAL: The following PRE-COMPUTED FORENSIC FACT is the authoritative answer for this query.");
            ctx.AppendLine("You MUST use these exact pre-computed values. Do NOT search session excerpts for a different answer.");
            ctx.AppendLine("Do NOT override this with individual session message counts from the excerpts below.");
            ctx.AppendLine();
            ctx.AppendLine(preComputedAggregate);
            ctx.AppendLine();
        }

        ctx.AppendLine("Do NOT include contact phone numbers unless the user explicitly asks for phone numbers or contact details.");
        ctx.AppendLine();

        if (IsTopicListQuestion(userQuestion))
        {
            ctx.AppendLine("When answering, list topics using the exact, explicit wording present in the excerpts (do not sanitize or euphemize terms).");
            ctx.AppendLine("Do NOT add boilerplate disclaimers about completeness unless the user explicitly asks if anything else was discussed.");
            ctx.AppendLine();
        }

        if (IsAggregateQuery(userQuestion))
        {
            ctx.AppendLine("IMPORTANT: When listing contacts or people, you MUST include EVERY contact identified across ALL excerpts. Check each excerpt's chat file and participant metadata systematically. Do not omit any contact.");
            ctx.AppendLine();
        }

        if (IsTotalMessageCountQuery(userQuestion) || IsDailyMultiContactQuery(userQuestion) || IsMonthlyActivityQuery(userQuestion))
        {
            ctx.AppendLine("IMPORTANT: An AGGREGATE FORENSIC STATS excerpt is included in the context. Use the pre-computed totals from that excerpt as the authoritative answer. Do NOT attempt to manually count messages from individual session excerpts \u2014 the stats excerpt is complete and accurate.");
            ctx.AppendLine();
        }

        ctx.AppendLine("You are answering questions using the provided chat-log excerpts.");
        ctx.AppendLine("If the answer is not supported by the excerpts, say you don't know.");
        ctx.AppendLine("CRITICAL: Only attribute statements to the participant and chat file shown in each excerpt header. Do NOT cross-attribute content between different chat files or participants.");
        ctx.AppendLine();
        ctx.AppendLine("<EXCERPTS>");

        foreach (var r in retrieved)
        {
            var chatFile = r.Metadata.GetValueOrDefault(MetadataKeys.ChatFile, "?");
            var sessionId = r.Metadata.GetValueOrDefault(MetadataKeys.SessionId, "?");
            var sessionDate = r.Metadata.GetValueOrDefault(MetadataKeys.SessionDate, "");
            var msgCount = r.Metadata.GetValueOrDefault(MetadataKeys.MessageCount, "");
            var participants = r.Metadata.GetValueOrDefault(MetadataKeys.Participants, "");

            var header = new StringBuilder();
            header.Append(CultureInfo.InvariantCulture, $"--- chat={chatFile} session={sessionId}");
            if (!string.IsNullOrEmpty(sessionDate)) header.Append(CultureInfo.InvariantCulture, $" date={sessionDate}");
            if (!string.IsNullOrEmpty(msgCount)) header.Append(CultureInfo.InvariantCulture, $" messages={msgCount}");
            if (!string.IsNullOrEmpty(participants)) header.Append(CultureInfo.InvariantCulture, $" participants={participants}");
            header.Append(" ---");

            ctx.AppendLine(header.ToString());
            ctx.AppendLine(r.Text);
            ctx.AppendLine();
        }

        ctx.AppendLine("</EXCERPTS>");
        return ctx.ToString();
    }

    internal static ChatHistory BuildChatHistory(
        string context,
        IReadOnlyList<ConversationTurn>? history,
        string userQuestion)
    {
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(context);

        foreach (var turn in history ?? [])
        {
            chatHistory.AddUserMessage(turn.UserQuestion);
            chatHistory.AddAssistantMessage(turn.AssistantAnswer);
        }

        chatHistory.AddUserMessage(userQuestion);
        return chatHistory;
    }

    private async IAsyncEnumerable<string> StreamFromKernelAsync(
        ChatHistory chatHistory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chatService = kernel.GetRequiredService<IChatCompletionService>();
        var chunks = await resiliencePipeline.ExecuteAsync(async token =>
        {
            var results = new List<string>();
            await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                               chatHistory, cancellationToken: token))
            {
                var text = chunk.Content;
                if (!string.IsNullOrEmpty(text))
                    results.Add(text);
            }
            return results;
        }, cancellationToken);

        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    internal static string? ExtractSingleNameCandidate(string question)
    {
        var tokens = question.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            var token = tokens[i].Trim('?', '.', ',', '!');
            if (NameLinkingWords.Contains(token))
            {
                var candidate = tokens[i + 1].Trim('?', '.', ',', '!');
                if (candidate.Length > 0 && char.IsUpper(candidate[0]))
                    return candidate;
            }
        }

        return null;
    }

    internal static List<string> ExtractKeyTerms(string question)
    {
        var terms = new List<string>();
        var tokens = question.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 1; i < tokens.Length; i++)
        {
            var token = tokens[i].Trim('?', '.', ',', '!', '"', '\'');
            if (token.Length <= 3) continue;
            if (!char.IsUpper(token[0])) continue;
            if (SearchExcludedTerms.Contains(token)) continue;

            if (i + 1 < tokens.Length)
            {
                var next = tokens[i + 1].Trim('?', '.', ',', '!', '"', '\'');
                if (next.Length > 0 && char.IsUpper(next[0]) && !SearchExcludedTerms.Contains(next))
                {
                    terms.Add($"{token} {next}");
                    i++;
                    continue;
                }
            }

            terms.Add(token);
        }

        return terms;
    }

    internal static string? ParseYearMonth(string question) =>
        TryParseMonthYear(MonthThenYearRegex().Match(question))
        ?? TryParseMonthYear(YearThenMonthRegex().Match(question));

    private static string? TryParseMonthYear(Match match)
    {
        if (!match.Success) return null;
        var monthIndex = Array.FindIndex(MonthNames,
            mn => string.Equals(mn, match.Groups["month"].Value, StringComparison.OrdinalIgnoreCase)) + 1;
        return monthIndex > 0 ? $"{match.Groups["year"].Value}-{monthIndex:D2}" : null;
    }

    private static string? InferContactIdFromVectorId(string vectorId)
    {
        const string prefix = "phonebook:";
        return vectorId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? vectorId[prefix.Length..]
            : null;
    }

    internal static string BuildHyDePrompt(string userQuestion, string systemPrompt) =>
        $$"""
        You are generating a hypothetical answer that will be used ONLY for semantic search.
        Write a short, information-dense answer to the user question based on what a chat log might contain.
        Do not mention that this is hypothetical.

        {{(string.IsNullOrWhiteSpace(systemPrompt) ? "" : $"Context about the data:\n{systemPrompt}\n")}}
        Question:
        {{userQuestion}}
        """;

    private static Dictionary<string, string> CreateFilter(string key, string value) =>
        new(StringComparer.OrdinalIgnoreCase) { [key] = value };

    private static Dictionary<string, string> CreateFilter(string key1, string value1, string key2, string value2) =>
        new(StringComparer.OrdinalIgnoreCase) { [key1] = value1, [key2] = value2 };

    internal static Dictionary<string, string>? BuildMetadataFilter(RagQueryOptions opts)
    {
        Dictionary<string, string>? dict = null;

        void Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            dict ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            dict[key] = value;
        }

        Add(MetadataKeys.ChatFile, opts.ChatFile);
        Add(MetadataKeys.Participants, opts.ParticipantName);

        return dict;
    }

    private static bool ContainsAny(string question, params ReadOnlySpan<string> terms)
    {
        foreach (var term in terms)
        {
            if (question.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static bool IsTopicListQuestion(string question) =>
        !string.IsNullOrWhiteSpace(question) &&
        ContainsAny(question, "topic", "what did", "what were", "discussed");

    internal static bool IsPhoneBookIndexQuestion(string question) =>
        !string.IsNullOrWhiteSpace(question) &&
        ContainsAny(question, "chat file", "chat_file_ids", "chats_summary", "chat ids", "chat id");

    internal static bool NeedsPhoneBookContacts(string question) =>
        !string.IsNullOrWhiteSpace(question) &&
        ContainsAny(question, "phone", "number", "contact", "who is", "id");

    internal static bool IsPersonMentionQuery(string question) =>
        !string.IsNullOrWhiteSpace(question) &&
        ContainsAny(question, "who mentioned", "contacts mentioned", "which contacts mentioned");

    internal static bool IsAggregateQuery(string question) =>
        !string.IsNullOrWhiteSpace(question) && (
            ContainsAny(question, "all", "every", "busiest", "most messages", "total messages",
                "how many messages", "which contacts", "who mentioned", "contacts mentioned") ||
            IsTotalMessageCountQuery(question) ||
            IsDailyMultiContactQuery(question) ||
            IsMonthlyActivityQuery(question) ||
            IsPersonMentionQuery(question));

    internal static bool IsTotalMessageCountQuery(string question) =>
        !string.IsNullOrWhiteSpace(question) && (
            ContainsAny(question, "total messages", "how many messages", "how many total") ||
            (ContainsAny(question, "compare") && ContainsAny(question, "messages")));

    internal static bool IsDailyMultiContactQuery(string question) =>
        !string.IsNullOrWhiteSpace(question) &&
        ContainsAny(question, "days", "day") &&
        ContainsAny(question, "different contacts", "more than");

    internal static bool IsMonthlyActivityQuery(string question) =>
        !string.IsNullOrWhiteSpace(question) &&
        ContainsAny(question, "month") &&
        ContainsAny(question, "activity", "no chat");

    internal static bool IsBusiestDayQuery(string question) =>
        !string.IsNullOrWhiteSpace(question) &&
        ContainsAny(question, "busiest", "most messages", "most active");

    internal static bool IsSingleChatMaxQuery(string question) =>
        !string.IsNullOrWhiteSpace(question) &&
        ContainsAny(question, "single chat") &&
        ContainsAny(question, "most messages", "most active");

    private readonly record struct DayStats(int Total, int MaxSingle, string MaxChatFile, string MaxParticipants);

    private async Task<string?> ComputeBusiestDayAsync(bool singleChatMax, CancellationToken ct)
    {
        var broadVector = await embeddingGenerator.GenerateAsync("chat session conversation messages", ct);
        var allSessions = await vectorDatabase.SearchAsync(broadVector, topK: 10_000, null, ct);

        var byDate = AggregateDayStats(allSessions);
        if (byDate.Count == 0) return null;

        if (singleChatMax)
        {
            var (date, stats) = FindGlobalMaxSession(byDate);
            return $"""
            [PRE-COMPUTED FORENSIC FACT — use this as the authoritative answer for single-chat-max questions]
            The single chat session with the most messages on any given day: file {stats.MaxChatFile} on {date} with {stats.MaxSingle} messages (participants: {stats.MaxParticipants}).
            """;
        }

        var busiest = byDate.MaxBy(kv => kv.Value.Total);
        return $"""
        [PRE-COMPUTED FORENSIC FACT — use this as the authoritative answer for busiest-day questions]
        Busiest day overall (most total messages across all chats): {busiest.Key} — {busiest.Value.Total} messages.
        On {busiest.Key}, the most active single chat was file {busiest.Value.MaxChatFile} with {busiest.Value.MaxSingle} messages (participants: {busiest.Value.MaxParticipants}).
        """;
    }

    private static Dictionary<string, DayStats> AggregateDayStats(IReadOnlyList<VectorSearchResult> sessions)
    {
        var byDate = new Dictionary<string, DayStats>(StringComparer.Ordinal);

        foreach (var session in sessions)
        {
            if (!session.Metadata.TryGetValue(MetadataKeys.SessionDate, out var date) || string.IsNullOrEmpty(date)) continue;
            if (!session.Metadata.TryGetValue(MetadataKeys.MessageCount, out var mcStr) || !int.TryParse(mcStr, out var mc)) continue;

            var chatFile = session.Metadata.GetValueOrDefault(MetadataKeys.ChatFile, "?");
            var participants = session.Metadata.GetValueOrDefault(MetadataKeys.Participants, "?");

            if (!byDate.TryGetValue(date, out var entry))
            {
                byDate[date] = new DayStats(mc, mc, chatFile, participants);
            }
            else
            {
                var newMaxSingle = mc > entry.MaxSingle;
                byDate[date] = new DayStats(
                    entry.Total + mc,
                    newMaxSingle ? mc : entry.MaxSingle,
                    newMaxSingle ? chatFile : entry.MaxChatFile,
                    newMaxSingle ? participants : entry.MaxParticipants);
            }
        }

        return byDate;
    }

    private static KeyValuePair<string, DayStats> FindGlobalMaxSession(Dictionary<string, DayStats> byDate) =>
        byDate.MaxBy(kv => kv.Value.MaxSingle);

    [GeneratedRegex(@"\b(?<month>January|February|March|April|May|June|July|August|September|October|November|December)\s+(?<year>\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthThenYearRegex();

    [GeneratedRegex(@"\b(?<year>\d{4})\s+(?<month>January|February|March|April|May|June|July|August|September|October|November|December)\b", RegexOptions.IgnoreCase)]
    private static partial Regex YearThenMonthRegex();
}
