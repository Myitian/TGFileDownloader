using Starksoft.Net.Proxy;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using TL;
using WTelegram;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace TGFileDownloader;

internal partial class Program
{
    static readonly object lockObj = new();
    static readonly string[] levelNames = [
        "TRACE",
        "DEBUG",
        "INFO",
        "WARN",
        "ERROR",
        "FATAL",
        "NONE"
    ];

    const int LOG_TRACE = 0;
    const int LOG_DEBUG = 1;
    const int LOG_INFO = 2;
    const int LOG_WARN = 3;
    const int LOG_ERROR = 4;
    const int LOG_FATAL = 5;
    const int LOG_NONE = 6;

    static string CacheDir = "./cache";
    static string SaveDir = "./save";

    static void Log(int level, string msg)
    {
        lock (lockObj)
        {
            string line = $"[{DateTime.Now:O}] [{levelNames[level]}] {msg}{Environment.NewLine}";
            File.AppendAllText("TGFDL.log", line);
            switch (level)
            {
                // case 2: // INFO
                //     Console.ForegroundColor = ConsoleColor.Gray;
                //     Console.BackgroundColor = ConsoleColor.Black;
                //     Console.WriteLine(msg);
                //     break;
                case 3: // WARNING
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.WriteLine(msg);
                    break;
                case 4: // ERROR
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.WriteLine(msg);
                    break;
                case 5: // FATAL
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.BackgroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine(msg);
                    break;
            }
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.BackgroundColor = ConsoleColor.Black;
        }
    }
    static async Task Main()
    {
        CacheDir = Directory.CreateDirectory(CacheDir).FullName;
        SaveDir = Directory.CreateDirectory(SaveDir).FullName;

        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.BackgroundColor = ConsoleColor.Black;
        File.Open("TGFDL.log", FileMode.Create).Close();
        Helpers.Log = Log;
        using Client client = new(Config)
        {
            MaxAutoReconnects = int.MaxValue
        };
        SetProxy(client);
        User user = await client.LoginUserIfNeeded();
        Console.WriteLine($"以 {user}（ID {user.id}）身份登录");
        Messages_Chats chats = await client.Messages_GetAllChats();
        foreach ((long id, ChatBase chat) in chats.chats)
            switch (chat)
            {
                case Chat smallgroup when smallgroup.IsActive:
                    Console.WriteLine($"{id}\t: SmallGroup: {smallgroup.title} with {smallgroup.participants_count} members");
                    break;
                case Channel channel when channel.IsChannel:
                    Console.WriteLine($"{id}\t: Channel {channel.username}: {channel.title}");
                    break;
                case Channel group:
                    Console.WriteLine($"{id}\t: Group {group.username}: {group.title}");
                    break;
            }
        FileManager fileManager = new(client);
        Console.Write("输入聊天 ID 来获取数据：");
        Console.Write(">>> ");
        long chatId = long.Parse(Input(""));
        var target = chats.chats[chatId];
        Console.Write("输入起始消息 ID（留空即为从最新开始）：");
        Console.Write(">>> ");
        int offset_id = int.Parse(Input("0"));
        for (; ; )
        {
            var messages = await client.Messages_GetHistory(target, offset_id);
            if (messages.Messages.Length == 0) break;
            foreach (var msgBase in messages.Messages)
            {
                var from = messages.UserOrChat(msgBase.From ?? msgBase.Peer);
                if (msgBase is Message msg && msg.media is not null)
                {
                    Console.WriteLine($"[{from}/{msg.ID}/{msg.Date:O}]>>");
                    await ProcessMedia(fileManager, msg.media, msg.ID, msg.From.ID, chatId);
                }
            }
            offset_id = messages.Messages[^1].ID;
        }
        var a = client.Dispose;
        Console.WriteLine("Done!");
    }
    static void SetProxy(Client client)
    {
        while (true)
        {
            Console.WriteLine("代理设置：");
            Console.WriteLine("  0. 无代理");
            Console.WriteLine("  1. Socks4");
            Console.WriteLine("  2. Socks4a");
            Console.WriteLine("  3. Socks5");
            Console.WriteLine("  4. Http");
            Console.Write(">>> ");
            ReadOnlySpan<char> selectionSpan = Console.ReadLine().AsSpan().Trim();
            char selection = selectionSpan.Length > 0 ? selectionSpan[0] : '0';
            if (selection is '0')
                return;
            Console.WriteLine("主机名（留空为 localhost）：");
            string proxyHost = Input("localhost");
            Console.WriteLine("端口（留空为 1080）：");
            ushort proxyPort = ushort.Parse(Input("1080"));
            if (selection is '1' or '2')
            {
                Console.WriteLine("用户信息（可留空）：");
                string? user = Console.ReadLine();
                switch (selection)
                {
                    case '1':
                        client.TcpHandler = async (address, port) =>
                        {
                            var proxy = string.IsNullOrEmpty(user) ?
                                new Socks4ProxyClient(proxyHost, proxyPort) :
                                new Socks4ProxyClient(proxyHost, proxyPort, user);
                            return proxy.CreateConnection(address, port);
                        };
                        break;
                    case '2':
                        client.TcpHandler = async (address, port) =>
                        {
                            var proxy = string.IsNullOrEmpty(user) ?
                                new Socks4aProxyClient(proxyHost, proxyPort) :
                                new Socks4aProxyClient(proxyHost, proxyPort, user);
                            return proxy.CreateConnection(address, port);
                        };
                        break;
                }
            }
            else if (selection is '3')
            {
                Console.WriteLine("用户名（可留空）：");
                string? userName = Console.ReadLine();
                Console.WriteLine("用户密码（可留空）：");
                string? userPassword = Console.ReadLine();
                client.TcpHandler = async (address, port) =>
                {
                    var proxy = string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(userPassword) ?
                        new Socks5ProxyClient(proxyHost, proxyPort) :
                        new Socks5ProxyClient(proxyHost, proxyPort, userName, userPassword);
                    return proxy.CreateConnection(address, port);
                };
            }
            else if (selection is '4')
            {
                client.TcpHandler = async (address, port) =>
                {
                    var proxy = new HttpProxyClient(proxyHost, proxyPort);
                    return proxy.CreateConnection(address, port);
                };
            }
            else
                continue;
            break;
        }
    }
    static string? Config(string what)
    {
        switch (what)
        {
            case "verification_code":
                Console.WriteLine("验证码：");
                Console.Write(">>> ");
                return Input("");
            case "email_verification_code":
                Console.WriteLine("邮件验证码：");
                Console.Write(">>> ");
                return Input("");
            case "password":
                Console.WriteLine("密码：");
                Console.Write(">>> ");
                return Input("");
            case "phone_number":
                Console.WriteLine("手机号：");
                Console.Write(">>> ");
                return Input("");
            case "api_id":
                Console.WriteLine("API ID：");
                Console.Write(">>> ");
                return Input("");
            case "api_hash":
                Console.WriteLine("API Hash：");
                Console.Write(">>> ");
                return Input("");
            default:
                return null;
        }
    }
    static string Input(string defalutStr)
    {
        string? value = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(value) ? defalutStr : value;
    }
    static async Task ProcessMedia(FileManager fileManager, MessageMedia msgMedia, long msgId, long msgAuthor, long msgChannel)
    {
        try
        {
            switch (msgMedia)
            {
                case MessageMediaPhoto photo:
                    Console.WriteLine($"[MSG<{msgId}>]");
                    Console.WriteLine("[Photo]");
                    Console.WriteLine(photo.photo.ID);
                    await Download(fileManager, photo.photo, msgId, msgAuthor, msgChannel);
                    break;
                case MessageMediaDocument document:
                    Console.WriteLine($"[MSG<{msgId}>]");
                    Console.WriteLine("[Document]");
                    Console.WriteLine(document.document.ID);
                    await Download(fileManager, document.document, msgId, msgAuthor, msgChannel);
                    break;
                case MessageMediaPaidMedia paidmedia:
                    Console.WriteLine($"[MSG<{msgId}>]");
                    Console.WriteLine("[PaidMedia]");
                    foreach (MessageExtendedMedia media in paidmedia.extended_media.OfType<MessageExtendedMedia>())
                    {
                        await ProcessMedia(fileManager, media.media, msgId, msgAuthor, msgChannel);
                    }
                    break;

                default:
                    Console.WriteLine("[Unsupported]");
                    break;
            }
        }
        catch (Exception e)
        {
            string s = $"[{DateTime.Now:O}]{Environment.NewLine}{e}{Environment.NewLine}";
            Log(LOG_ERROR, s);
            File.AppendAllText("err.log", s);
        }
    }
    static async Task Download(FileManager fileManager, PhotoBase photo, long msgId, long msgAuthor, long msgChannel)
    {
        switch (photo)
        {
            case Photo p:
                {
                    string ext = ".bin";
                    string tmp = $"p{p.ID}.tmp";
                    string realTmp = Path.Combine(CacheDir, tmp);
                    if (fileManager.QueryIsFinished(p.ID, out string? s))
                    {
                        Console.WriteLine($"{p.ID} 已下载，正在跳过……");
                        if (!File.Exists(tmp))
                            return;
                        TGFileInfo? info = fileManager.GetFileInfo(p.ID);
                        ext = info?.Extension ?? ".bin";
                    }
                    else
                    {
                        Console.WriteLine($"{p.ID} 开始下载");
                        using FileStream fileStream = File.Open(realTmp, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write);
                        Storage_FileType type = await fileManager.DownloadFileAsync(p, fileStream, ReportProgress, msgId, msgAuthor, msgChannel);
                        fileStream.SetLength(fileStream.Position);
                        Console.WriteLine();
                        Console.WriteLine($"{p.ID} 下载完成");
                        if (type is not Storage_FileType.unknown and not Storage_FileType.partial)
                            ext = $".{type}";
                    }
                    string fileName = $"p{p.ID}{ext}";
                    string realFileName = Path.Combine(SaveDir, fileName);
                    File.Move(realTmp, realFileName, true);
                    fileManager.UpdateFileName(p.ID, fileName);
                    Console.WriteLine($"将 {p.ID} 保存为 {fileName}");
                }
                break;
            default:
                Console.WriteLine("空");
                break;
        }
    }
    static async Task Download(FileManager fileManager, DocumentBase document, long msgId, long msgAuthor, long msgChannel)
    {
        switch (document)
        {
            case Document d:
                {
                    string tmp = $"d{d.ID}.tmp";
                    string realTmp = Path.Combine(CacheDir, tmp);
                    if (fileManager.QueryIsFinished(d.ID, out string? s))
                    {
                        Console.WriteLine($"{d.ID} 已下载，正在跳过……");
                        if (!File.Exists(tmp))
                            return;
                    }
                    else
                    {
                        Console.WriteLine($"{d.ID} {d.Filename} 开始下载");
                        using FileStream fileStream = File.Open(realTmp, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write);
                        await fileManager.DownloadFileAsync(d, fileStream, ReportProgress, msgId, msgAuthor, msgChannel);
                        fileStream.SetLength(fileStream.Position);
                        Console.WriteLine();
                        Console.WriteLine($"{d.ID} {d.Filename} 下载完成");
                    }
                    string fileName = d.Filename;
                    int extLength = RxEnd().Match(fileName).Length;
                    string originalFileName = Path.Combine(SaveDir, fileName);
                    string realFileName = originalFileName;
                    int i = 1;
                    while (File.Exists(realFileName))
                    {
                        realFileName = AddNum(originalFileName, ++i, extLength);
                    }
                    File.Move(realTmp, realFileName, true);
                    string namePart = Path.GetFileName(realFileName);
                    Console.WriteLine($"将 {d.ID} 保存为 {namePart}");
                    fileManager.UpdateFileName(d.ID, namePart);

                    static string AddNum(ReadOnlySpan<char> original, int i, int extLength)
                    {
                        int mainLength = original.Length - extLength;
                        return $"{original[..mainLength]} ({i}){original[mainLength..]}";
                    }
                }
                break;
            default:
                Console.WriteLine("空");
                break;
        }
    }
    static string ByteUnit(long count, bool useSI = false)
    {
        if (useSI)
            return count switch
            {
                < 1000
                    => $"{count,4} B",
                < 1000000
                    => $"{count / 1000.0:F2} kB",
                < 1000000000
                    => $"{count / 1000000.0:F2} MB",
                < 1000000000000
                    => $"{count / 1000000000.0:F2} GB",
                _
                    => $"{count / 1000000000000.0:F2} TB",
            };
        else
            return count switch
            {
                < 1024
                    => $"{count,4} B",
                < 1048576
                    => $"{count / 1024.0:F2} KiB",
                < 1073741824
                    => $"{count / 1048576.0:F2} MiB",
                < 1099511627776
                    => $"{count / 1073741824.0:F2} GiB",
                _
                    => $"{count / 1099511627776.0:F2} TiB",
            };
    }
    static public void ReportProgress(long transmitted, long totalSize)
    {
        if (totalSize > 0)
        {
            double percent = transmitted / (double)totalSize;
            Console.Write($"\r[{DateTime.Now:O}] Downloading: {percent:P2} {ByteUnit(transmitted)}/{ByteUnit(totalSize)}    ");
        }
        else
        {
            Console.Write($"\r[{DateTime.Now:O}] Downloading: {ByteUnit(transmitted)}");
        }
    }

    [GeneratedRegex(@"(?:(?:\.[a-zA-Z0-9_\-]*)*\.[^\.]*)?$", RegexOptions.RightToLeft)]
    private static partial Regex RxEnd();
}