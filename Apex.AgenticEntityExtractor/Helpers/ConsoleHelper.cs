namespace Apex.AgenticEntityExtractor.Helpers;

public static class ConsoleHelper
{
    private static readonly Lock _consoleLock = new();

    public static void PrintColoredLine(string text, ConsoleColor color)
    {
        lock (_consoleLock)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }
    }

    public static void PrintColored(string text, ConsoleColor color)
    {
        lock (_consoleLock)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
        }
    }
}