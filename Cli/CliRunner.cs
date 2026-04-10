using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.Text;

namespace ContactsRag;

internal static class CliRunner
{
    internal static async Task RunAsync(IServiceProvider services)
    {
        var lastRenderWidth = 0;

        AnsiConsole.Write(new FigletText("ContactsRag RAG").LeftJustified().Color(Color.Cyan1));
        AnsiConsole.Write(new Rule("[dim]Interactive Chat-Log RAG · .NET 10 · Semantic Kernel · OpenAI[/]")
            .RuleStyle("grey").LeftJustified());
        AnsiConsole.WriteLine();

        var contentRoot = ContentRootResolver.Resolve(Directory.GetCurrentDirectory());
        var generator = services.GetRequiredService<IChatDataGenerator>();
        var ingestion = services.GetRequiredService<IChatIngestionService>();

        var structured = 0;
        var ingestionResult = (Read: 0, Embedded: 0, Stored: 0, CacheHits: 0);

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("[cyan]Processing data ingestion...[/]", async _ =>
            {
                var (Read, Structured) = await generator.GenerateAsync(contentRoot, CancellationToken.None);
                structured = Structured;
                ingestionResult = await ingestion.IngestAsync(CancellationToken.None);
            });

        RenderIngestionMetrics(ingestionResult.Read, structured, ingestionResult.Embedded, ingestionResult.Stored, ingestionResult.CacheHits);
        AnsiConsole.WriteLine();

        var helpTable = BuildHelpTable();
        AnsiConsole.Write(helpTable);
        AnsiConsole.WriteLine();

        var rag = services.GetRequiredService<IRagOrchestrator>();
        var conversationHistory = new List<ConversationTurn>();
        var commandHistory = new List<string>();
        var historyDrafts = new Dictionary<int, string>();

        while (true)
        {
            string question;
            if (Console.IsInputRedirected)
            {
                AnsiConsole.Markup("[cyan]You>[/] ");
                question = Console.ReadLine() ?? "exit";
            }
            else
            {
                question = ConsoleLineReader.ReadLine(commandHistory, historyDrafts, ref lastRenderWidth);
            }

            if (!string.IsNullOrWhiteSpace(question) && (commandHistory.Count == 0 || commandHistory[^1] != question))
                commandHistory.Add(question);

            if (string.IsNullOrWhiteSpace(question))
                continue;

            if (question.Equals("exit", StringComparison.OrdinalIgnoreCase)
                || question.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                return;
            }

            if (question.Equals("exit with save", StringComparison.OrdinalIgnoreCase))
            {
                await new ChatHistoryExporter().SaveAsync(conversationHistory);
                AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                return;
            }

            if (question.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                conversationHistory.Clear();
                AnsiConsole.MarkupLine("[dim]Conversation context cleared.[/]");
                AnsiConsole.WriteLine();
                continue;
            }

            if (question.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.Write(helpTable);
                AnsiConsole.WriteLine();
                continue;
            }

            await ExecuteQueryAsync(rag, question, conversationHistory);
            AnsiConsole.WriteLine();
        }
    }

    private static async Task ExecuteQueryAsync(
        IRagOrchestrator rag,
        string question,
        List<ConversationTurn> conversationHistory)
    {
        using var cts = new CancellationTokenSource();
        void cancelHandler(object? s, ConsoleCancelEventArgs e) { e.Cancel = true; cts.Cancel(); }
        Console.CancelKeyPress += cancelHandler;

        try
        {
            var sb = new StringBuilder();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Star)
                .SpinnerStyle(Style.Parse("yellow"))
                .StartAsync("[yellow]Thinking...[/]", async _ =>
                {
                    var result = await rag.AskStreamingAsync(question, conversationHistory, options: null, cts.Token);
                    await foreach (var token in result.AnswerStream)
                        sb.Append(token);

                    conversationHistory.Add(new ConversationTurn(question, sb.ToString(), result.SourceFiles));
                });

            var answer = conversationHistory[^1].AssistantAnswer;
            AnsiConsole.Write(new Panel(new Markup(Markup.Escape(answer)))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = Style.Parse("green"),
                Header = new PanelHeader("[bold green]Assistant[/]"),
                Padding = new Padding(1, 1, 1, 1)
            });
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]⚡ Canceled.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void RenderIngestionMetrics(int read, int structured, int embedded, int stored, int cacheHits)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(Style.Parse("cyan"))
            .Title("[bold cyan]Ingestion Summary[/]")
            .AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Count[/]").RightAligned());

        if (embedded > 0)
        {
            table.AddRow("Documents Read", $"[bold]{read}[/]");
            table.AddRow("Documents Structured", $"[bold]{structured}[/]");
            table.AddRow("[yellow]Documents Embedded[/]", $"[bold yellow]{embedded}[/]");
            table.AddRow("Documents Stored", $"[bold]{stored}[/]");
        }
        else
        {
            table.AddRow("Documents Read", $"[bold]{read}[/]");
            table.AddRow("Documents Stored", $"[bold]{stored}[/]");
        }

        if (cacheHits > 0)
        {
            table.AddRow("[green]Cache Hits[/]", $"[bold green]{cacheHits}[/]");
        }

        AnsiConsole.Write(table);
    }

    private static Table BuildHelpTable() =>
        new Table()
            .Border(TableBorder.SimpleHeavy)
            .BorderStyle(Style.Parse("dim"))
            .AddColumn(new TableColumn("[bold]Command[/]").Centered())
            .AddColumn(new TableColumn("[bold]Description[/]"))
            .AddRow("[cyan]exit[/] / [cyan]quit[/]", "Close the application")
            .AddRow("[cyan]exit with save[/]", "Save history as JSON and exit")
            .AddRow("[cyan]/clear[/]", "Reset conversation context")
            .AddRow("[cyan]/help[/]", "Show this help table")
            .AddRow("[cyan]Ctrl+C[/]", "Cancel the current query");
}
