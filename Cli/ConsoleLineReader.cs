using System.Text;
using Spectre.Console;

namespace ContactsRag;

internal static class ConsoleLineReader
{
    internal static string ReadLine(
        IList<string> commandHistory,
        Dictionary<int, string> drafts,
        ref int lastRenderWidth)
    {
        const int promptWidth = 5;

        var line = new StringBuilder();
        var pos = 0;
        var historyIndex = commandHistory.Count;
        drafts.Clear();
        drafts[historyIndex] = "";

        AnsiConsole.Markup("[cyan]You>[/] ");

        while (true)
        {
            var key = Console.ReadKey(true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    AnsiConsole.WriteLine();
                    return line.ToString();

                case ConsoleKey.Backspace when pos > 0:
                    line.Remove(pos - 1, 1);
                    pos--;
                    Refresh(line.ToString(), pos, promptWidth, ref lastRenderWidth);
                    break;

                case ConsoleKey.Delete when pos < line.Length:
                    line.Remove(pos, 1);
                    Refresh(line.ToString(), pos, promptWidth, ref lastRenderWidth);
                    break;

                case ConsoleKey.LeftArrow when pos > 0:
                    pos--;
                    Console.SetCursorPosition(Console.CursorLeft - 1, Console.CursorTop);
                    break;

                case ConsoleKey.RightArrow when pos < line.Length:
                    pos++;
                    Console.SetCursorPosition(Console.CursorLeft + 1, Console.CursorTop);
                    break;

                case ConsoleKey.UpArrow when historyIndex > 0:
                    drafts[historyIndex] = line.ToString();
                    historyIndex--;
                    line.Clear().Append(ResolveHistoryEntry(commandHistory, drafts, historyIndex));
                    pos = line.Length;
                    Refresh(line.ToString(), pos, promptWidth, ref lastRenderWidth);
                    break;

                case ConsoleKey.DownArrow when historyIndex < commandHistory.Count:
                    drafts[historyIndex] = line.ToString();
                    historyIndex++;
                    line.Clear().Append(ResolveHistoryEntry(commandHistory, drafts, historyIndex));
                    pos = line.Length;
                    Refresh(line.ToString(), pos, promptWidth, ref lastRenderWidth);
                    break;

                default:
                    if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                    {
                        line.Insert(pos, key.KeyChar);
                        pos++;
                        Refresh(line.ToString(), pos, promptWidth, ref lastRenderWidth);
                    }
                    break;
            }
        }
    }

    private static string ResolveHistoryEntry(
        IList<string> commandHistory,
        Dictionary<int, string> drafts,
        int index) =>
        drafts.TryGetValue(index, out var draft)
            ? draft
            : index < commandHistory.Count ? commandHistory[index] : "";

    private static void Refresh(string line, int pos, int promptWidth, ref int lastRenderWidth)
    {
        if (Console.IsInputRedirected) return;

        var currentTop = Console.CursorTop;

        int bufferWidth;
        try { bufferWidth = Console.BufferWidth; }
        catch { bufferWidth = 0; }

        var available = bufferWidth > 0 ? Math.Max(0, bufferWidth - promptWidth) : 0;
        var visible = available > 0 && line.Length > available ? line[..available] : line;

        Console.SetCursorPosition(promptWidth, currentTop);
        AnsiConsole.Write(visible);

        var leftover = lastRenderWidth - visible.Length;
        if (leftover > 0)
            AnsiConsole.Write(new string(' ', leftover));

        lastRenderWidth = visible.Length;
        var clampedPos = available > 0 ? Math.Min(pos, Math.Min(line.Length, available)) : pos;
        Console.SetCursorPosition(promptWidth + clampedPos, currentTop);
    }
}
