using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace ContactsRag;

public static class ValidationTest
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var generator = services.GetRequiredService<IChatDataGenerator>();
        var ingestion = services.GetRequiredService<IChatIngestionService>();
        var rag = services.GetRequiredService<IRagOrchestrator>();

        AnsiConsole.Write(new FigletText("RAG VALIDATION").Centered().Color(Color.Yellow));
        AnsiConsole.Write(new Rule("[yellow]Running Automated Test Suite[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("[cyan]Indexing data...[/]", async _ =>
            {
                var cwd = Directory.GetCurrentDirectory();
                await generator.GenerateAsync(cwd, CancellationToken.None);
                await ingestion.IngestAsync(CancellationToken.None);
            });

        AnsiConsole.WriteLine();

        var testCases = new[]
        {
            new TestCase(1, "What is the name of the owner of this phone book?", "Lee"),
            new TestCase(2, "Give me all of the names in the phone book",
                "1. Michael Jackson\n2. Flora Roberta Ela Cortez\n3. Gloria Villa\n4. Clayton Gregory\n5. Moore\n6. Lori Green\n7. Anisa Wanda Velez\n8. Fred Sampson\n9. Rueben Hess\n10. Bella Winter\n11. Safia Ramirez\n12. Tony Riddle\n13. Malik Carter\n14. Jade Thompson\n15. Harvey Ward\n16. Aaron Patel\n17. Sarah Krueger\n18. Katy Nadia Lea Vaughan\n19. Kirsty Pineda\n20. Dylan Duffy\n21. Grace Jones\n22. Zahir Rahman\n23. Amara Daniels\n24. Mia Carter\n25. Mrs. Eleanor Carter\n26. Mr. Samuel Harper"),
            new TestCase(3, "Name me all the chat files ids", "19B8, 2A48, 2EF1, 5426, 6277, AB9C, D67B, E3B7, EEA5"),
            new TestCase(4, "What is the phone number of Moore?", "0693216727"),
            new TestCase(5, "What is the phone number of Malik Carter?", "0955752319"),
            new TestCase(6, "What were the topics discussed with Aaron?", "Any 3–5 of: Feltham, turning life around, memoir, social life (football/gym/FIFA/park/food), creative work (street photography/spoken word/public speaking), community work and mentoring, family, store."),
            new TestCase(7, "Who was raised in Tower Hamlets?", "Zahir Rahman (ID: 2A48)"),
            new TestCase(8, "Who is the first person to ever talk with Lee and who Lee last messaged with?", "First: Aaron Patel (2EF1). Last: don't expect any specific name as long as the date is 2025-01-01."),
            new TestCase(9, "On what date did Lee had the most messages and with who?", "2024-07-17 (Overall busiest) with 53 messages, same day (Single chat busiest) with Jade Thompson (17 messages)."),
            new TestCase(10, "What is Zahir Rahman phone numbers", "0674546791, 017041640 and 078075596"),
            new TestCase(11, "How many contacts are in the phone book", "26"),
            new TestCase(12, "How many chats does Lee has?", "9"),
            new TestCase(13, "Which contacts mentioned 'Jake' and what was the nature of their relationship with him?",
                "1. Lee (device owner) — described Jake as his brother, protector, and partner in past offending and recovery\n2. Mia Carter — recalled seeing Lee and Jake together\n3. Mrs. Eleanor Carter — remembered Lee and Jake from childhood\n4. Jade Thompson — referenced Jake as Lee's older brother and his influence\n5. Mr. Samuel Harper — referenced Jake's influence on Lee\n6. Aaron Patel — referenced Jake based on Lee's disclosures about dealing\n7. Malik Carter — referenced Jake as Lee's protector and negative influence\n8. Grace Jones — referenced Jake in relation to Lee's hardship"),
            new TestCase(14, "Identify all phone numbers associated with the last name 'Carter' across both the phone book and chat transcripts.", "Eleanor Carter (0188577983), Mia Carter (0907216160), Malik Carter (0955752319)."),
            new TestCase(15, "I want all the names of contacts Lee talked with in 2024 March",
                "1. Jade Thompson\n2. Zahir Rahman\n3. Aaron Patel\n4. Mia Carter\n5. Mr. Samuel Harper\n6. Amara Daniels\n7. Mrs. Eleanor Carter\n8. Malik Carter\n9. Grace Jones"),
            new TestCase(16, "I want all the names of contacts Lee talked with in 2024 January",
                "1. Jade Thompson\n2. Zahir Rahman\n3. Aaron Patel"),
            new TestCase(17, "I want all the names of contacts Lee talked with in 2026 January", "There are no chats in 2026."),
            new TestCase(18, "How many chats are private chats", "9"),
            new TestCase(19, "How many chats are group chats", "0"),
            new TestCase(20, "Did Aaron ever talked about his experience in Feltham?", "No — it was Lee (device owner, ID: 0000) who was at Feltham, not Aaron. Aaron was asking Lee about his experience and outreach talks there."),
            new TestCase(21, "In a single chat who did Lee had the most messages in a day?", "2024-05-28 with Mrs. Eleanor Carter (31 messages)."),
            new TestCase(22, "How many total messages did Lee exchange with Jade Thompson?", "371 messages across 139 sessions in chat file 19B8."),
            new TestCase(23, "Which contacts in the phone book did Lee NOT have any chats with?",
                "17 contacts:\n1. Michael Jackson\n2. Flora Roberta Ela Cortez\n3. Gloria Villa\n4. Clayton Gregory\n5. Moore\n6. Lori Green\n7. Anisa Wanda Velez\n8. Fred Sampson\n9. Rueben Hess\n10. Bella Winter\n11. Safia Ramirez\n12. Tony Riddle\n13. Harvey Ward\n14. Sarah Krueger\n15. Katy Nadia Lea Vaughan\n16. Kirsty Pineda\n17. Dylan Duffy"),
            new TestCase(24, "Were there any months in 2024 where Lee had no chat activity at all?", "No — all 12 months (January through December 2024) have chat activity."),
            new TestCase(25, "Compare Aaron Patel and Jade Thompson — who did Lee exchange more messages with?", "Aaron Patel (459 messages in file 2EF1) vs Jade Thompson (371 messages in file 19B8). Aaron Patel has more."),
            new TestCase(26, "Were there any days where Lee chatted with more than 3 different contacts?", "Yes — many days, particularly from March through September 2024, had sessions with 3 or more different contacts."),
            new TestCase(27, "What is the earliest and latest message timestamp across all chats?", "Earliest: 2024-01-01. Latest: 2025-01-01."),
            new TestCase(28, "Explain to me what is DNS", "Unrelated question, RAG should block this question from an answer"),
        };

        var history = new List<ConversationTurn>();

        foreach (var test in testCases)
        {
            AnsiConsole.Write(new Rule($"[bold cyan]TEST CASE #{test.ID}[/]").LeftJustified());
            AnsiConsole.MarkupLine($"[bold yellow]Q:[/] {test.Question}");
            AnsiConsole.MarkupLine($"[dim grey]Expected:[/] {test.GroundTruth}");
            AnsiConsole.WriteLine();

            var sb = new StringBuilder();
            IReadOnlyList<string>? sourceFiles = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("[yellow]Querying RAG...[/]", async _ =>
                {
                    var result = await rag.AskStreamingAsync(test.Question, [], null, CancellationToken.None);
                    await foreach (var token in result.AnswerStream)
                    {
                        sb.Append(token);
                    }

                    sourceFiles = result.SourceFiles;
                });

            var answer = sb.ToString();
            history.Add(new ConversationTurn(test.Question, answer, sourceFiles));

            AnsiConsole.Write(new Panel(new Markup(Markup.Escape(answer)))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = Style.Parse("green"),
                Header = new PanelHeader("[bold green]RAG Response[/]"),
                Padding = new Padding(1, 1, 1, 1)
            });
            AnsiConsole.WriteLine();
        }

        AnsiConsole.Write(new Rule("[yellow]Validation Complete[/]").RuleStyle("grey"));
        var outputPath = await new ChatHistoryExporter().SaveAsync(history);
        await InjectExpectedAnswersAsync(outputPath, testCases);
    }

    private static async Task InjectExpectedAnswersAsync(string outputPath, IReadOnlyList<TestCase> testCases)
    {
        var json = await File.ReadAllTextAsync(outputPath);
        if (JsonNode.Parse(json) is not JsonArray root || root.Count == 0)
        {
            return;
        }

        var expectedByQuestion = testCases.ToDictionary(t => t.Question, t => t.GroundTruth, StringComparer.Ordinal);

        foreach (var node in root)
        {
            if (node is not JsonObject obj)
            {
                continue;
            }

            var question = obj["user prompt"]?.GetValue<string>();
            if (question is null)
            {
                continue;
            }

            if (expectedByQuestion.TryGetValue(question, out var expected))
            {
                obj["ExpectedAnswer"] = expected;
            }
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        await File.WriteAllTextAsync(outputPath, root.ToJsonString(options));
    }

    private sealed record TestCase(int ID, string Question, string GroundTruth);
}
