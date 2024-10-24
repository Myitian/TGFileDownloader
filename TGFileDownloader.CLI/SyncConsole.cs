namespace TGFileDownloader.CLI;

public static class SyncConsole
{
    public static readonly object Lock = new();

    public static StreamWriter? LogFile { get; set; } = null;
    public static LogLevel MinFileLogLevel { get; set; } = LogLevel.INFO;
    public static LogLevel MinConsoleLogLevel { get; set; } = LogLevel.WARN;

    public static void Log(int level, string msg)
    {
        Log((LogLevel)level, msg);
    }
    public static void Log(LogLevel level, string msg)
    {
        lock (Lock)
        {
            if (level >= MinFileLogLevel)
            {
                LogFile?.WriteLine($"[{DateTime.Now:O}] [{level}] {msg}");
                LogFile?.Flush();
            }
            if (level >= MinConsoleLogLevel)
            {
                switch (level)
                {
                    case LogLevel.TRACE:
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.WriteLine(msg);
                        break;
                    case LogLevel.DEBUG:
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.WriteLine(msg);
                        break;
                    case LogLevel.INFO:
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.WriteLine(msg);
                        break;
                    case LogLevel.WARN:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.WriteLine(msg);
                        break;
                    case LogLevel.ERROR:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.WriteLine(msg);
                        break;
                    case LogLevel.FATAL:
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.BackgroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine(msg);
                        break;
                }
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.BackgroundColor = ConsoleColor.Black;
            }
        }
    }
    public static void Write(string msg)
    {
        lock (Lock)
            Console.Write(msg);
    }
    public static void WriteLine(params ReadOnlySpan<string> msgs)
    {
        lock (Lock)
            foreach (string msg in msgs)
                Console.WriteLine(msg);
    }
    public static void WriteLine(string msg, long num)
    {
        lock (Lock)
        {
            Console.WriteLine(msg);
            Console.WriteLine(num);
        }
    }
    public static void WriteLine()
    {
        lock (Lock)
            Console.WriteLine();
    }
}
