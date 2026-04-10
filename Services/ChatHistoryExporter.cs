using System.IO.Abstractions;
using System.Text.Encodings.Web;
using System.Text.Json;
using Spectre.Console;

namespace ContactsRag;

public sealed class ChatHistoryExporter(IFileSystem? fileSystem = null)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? new FileSystem();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<string> SaveAsync(IReadOnlyList<ConversationTurn> history)
    {
        var fileName = $"{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.json";
        var json = JsonSerializer.Serialize(history, SerializerOptions);
        await _fileSystem.File.WriteAllTextAsync(fileName, json);
        AnsiConsole.MarkupLine($"[green]Chat history saved to: [bold]{fileName}[/][/]");
        return fileName;
    }
}
