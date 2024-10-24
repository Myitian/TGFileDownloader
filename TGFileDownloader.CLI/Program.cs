using System.Buffers;
using System.Text;

namespace TGFileDownloader.CLI;

public class Program
{
    public const string DefaultCacheDir = "./cache";
    public const string DefaultSaveDir = "./save";
    public const string DefaultConfigFile = "config.json";

    public static readonly Config Config = Config.Load(DefaultConfigFile);
    public static readonly UTF8Encoding UTF8_NoBOM = new(false, false);
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = UTF8_NoBOM;
        Console.InputEncoding = UTF8_NoBOM;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.BackgroundColor = ConsoleColor.Black;

        for (int i = 0; i < args.Length; i++)
        {
            ReadOnlySpan<char> rawArg = args[i];
            char[] buffer = ArrayPool<char>.Shared.Rent(rawArg.Length);
            Span<char> arg = buffer.AsSpan(0, rawArg.Length);
            rawArg.CopyTo(arg);
            try
            {
                switch (ASCIIToLower(arg.Trim()))
                {
                    case "-flushsql":
                        FileManager fm = new(Config.SQLite.UseMemoryDB, Config.SQLite.Pragma);
                        fm.Dispose();
                        break;
                    case "-completeconfig":
                        Config.Save(DefaultConfigFile);
                        SyncConsole.WriteLine("配置已保存");
                        break;
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        await Downloader.DownloaderMain();
        SyncConsole.LogFile?.Dispose();
    }

    static Span<char> ASCIIToLower(Span<char> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            char c = buffer[i];
            if (c >= 'A' && c <= 'Z')
                buffer[i] = (char)(c | ('A' ^ 'a'));
        }
        return buffer;
    }
}