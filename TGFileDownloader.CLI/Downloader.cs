using Starksoft.Net.Proxy;
using System.Diagnostics;
using System.Text.RegularExpressions;
using TL;
using WTelegram;

namespace TGFileDownloader.CLI;

static partial class Downloader
{
    static readonly Stopwatch stopwatch = Stopwatch.StartNew();
    static readonly double freq = Stopwatch.Frequency;

    static string cacheDir = Program.DefaultCacheDir;
    static string saveDir = Program.DefaultSaveDir;
    static double speed = -1;
    static long startTick = 0;
    static long startTransmitted = 0;
    static long prevReportTick = 0;
    static long prevTransmitted = 0;
    static long prevTotalSize = 0;

    static void ReportProgress(long transmitted, long totalSize, bool newFile = false)
    {
        string avg = "";
        if (Program.Config.App.SpeedMoniterUpdateTime.HasValue)
        {
            long currentTick = stopwatch.ElapsedTicks;
            double elapsed = (currentTick - prevReportTick) / freq;
            double minElps = Program.Config.App.SpeedMoniterUpdateTime.Value;
            if (newFile)
            {
                speed = -1;
                prevReportTick = currentTick;
                startTick = currentTick;
                startTransmitted = transmitted;
                prevTotalSize = totalSize;
            }
            else if (transmitted == totalSize)
            {
                long diff = transmitted - startTransmitted;
                double totalElps = (currentTick - startTick) / freq;
                speed = diff / totalElps;
                avg = "avg ";
            }
            else if (elapsed >= minElps)
            {
                long diff = transmitted - prevTransmitted;
                if (diff >= 0)
                {
                    speed = diff / elapsed;
                }
                prevReportTick = currentTick;
                prevTransmitted = transmitted;
            }
        }

        if (speed < 0)
            if (totalSize > 0)
            {
                double percent = transmitted / (double)totalSize;
                SyncConsole.Write($"\r[{DateTime.Now:O}] Downloading: {percent:P2} {ByteUnit(transmitted)}/{ByteUnit(totalSize)}    ");
            }
            else
                SyncConsole.Write($"\r[{DateTime.Now:O}] Downloading: {ByteUnit(transmitted)}    ");
        else if (totalSize > 0)
        {
            double percent = transmitted / (double)totalSize;
            SyncConsole.Write($"\r[{DateTime.Now:O}] Downloading: {percent:P2} {ByteUnit(transmitted)}/{ByteUnit(totalSize)} ({avg}{ByteUnit(speed)}/s)    ");
        }
        else
            SyncConsole.Write($"\r[{DateTime.Now:O}] Downloading: {ByteUnit(transmitted)} ({avg}{ByteUnit(speed)}/s)    ");
    }

    static string ByteUnit(long count, bool useSI = false)
    {
        if (useSI)
            return count switch
            {
                < 1000
                    => $"{count} B",
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
                    => $"{count} B",
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
    static string ByteUnit(double count, bool useSI = false)
    {
        if (useSI)
            return count switch
            {
                < 1000
                    => $"{count} B",
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
                    => $"{count:F2} B",
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

    static string? TGConfig(string what)
    {
        switch (what)
        {
            case "verification_code":
                SyncConsole.WriteLine("验证码：");
                SyncConsole.Write(">>> ");
                return Input("");
            case "email_verification_code":
                SyncConsole.WriteLine("邮件验证码：");
                SyncConsole.Write(">>> ");
                return Input("");
            case "password":
                SyncConsole.WriteLine("密码：");
                SyncConsole.Write(">>> ");
                return Input("");
            case "phone_number":
                if (Program.Config.Telegram.PhoneNumber is not null)
                    return Program.Config.Telegram.PhoneNumber;
                SyncConsole.WriteLine("手机号：");
                SyncConsole.Write(">>> ");
                return Input("");
            case "api_id":
                if (Program.Config.Telegram.API_ID is not null)
                    return Program.Config.Telegram.API_ID;
                SyncConsole.WriteLine("API ID：");
                SyncConsole.Write(">>> ");
                return Input("");
            case "api_hash":
                if (Program.Config.Telegram.API_Hash is not null)
                    return Program.Config.Telegram.API_Hash;
                SyncConsole.WriteLine("API Hash：");
                SyncConsole.Write(">>> ");
                return Input("");
            case "session_pathname":
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WTelegram.session");
            default:
                return null;
        }
    }

    static async Task<bool> Download(MultiThreadDownloader downloader, PhotoBase photo, long msgId, long msgAuthor, long msgChannel)
    {
        switch (photo)
        {
            case Photo p:
                {
                    string ext = ".bin";
                    string tmp = $"p{p.ID}.tmp";
                    string realTmp = Path.Combine(cacheDir, tmp);
                    if (downloader.FileManager.QueryIsFinished(p.ID, out string? s))
                    {
                        SyncConsole.WriteLine($"{p.ID} 已下载，正在跳过……");
                        if (!File.Exists(tmp))
                            return false;
                    }
                    else
                    {
                        SyncConsole.WriteLine($"{p.ID} 开始下载");
                        using FileStream fileStream = File.Open(realTmp, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                        fileStream.SetLength(p.LargestPhotoSize.FileSize);
                        await downloader.DownloadFileAsync(p, fileStream, ReportProgress, msgId, msgAuthor, msgChannel);
                        SyncConsole.WriteLine();
                        bool isFinished = downloader.FileManager.QueryIsFinished(p.ID, out _);
                        if (isFinished)
                        {
                            SyncConsole.WriteLine($"{p.ID} 下载完成");
                        }
                        else
                        {
                            SyncConsole.WriteLine($"{p.ID} 下载失败");
                            return false;
                        }
                    }
                    TGFileInfo? info = downloader.FileManager.GetFileInfo(p.ID);
                    ext = info?.Extension ?? ".bin";
                    string fileName = $"p{p.ID}{ext}";
                    string realFileName = Path.Combine(saveDir, fileName);
                    File.Move(realTmp, realFileName, true);
                    downloader.FileManager.UpdateFileName(p.ID, fileName);
                    SyncConsole.WriteLine($"将 {p.ID} 保存为 {fileName}");
                }
                break;
            default:
                SyncConsole.WriteLine("空");
                break;
        }
        return true;
    }
    static async Task<bool> Download(MultiThreadDownloader downloader, DocumentBase document, long msgId, long msgAuthor, long msgChannel)
    {
        switch (document)
        {
            case Document d:
                {
                    string tmp = $"d{d.ID}.tmp";
                    string realTmp = Path.Combine(cacheDir, tmp);
                    if (downloader.FileManager.QueryIsFinished(d.ID, out string? s))
                    {
                        SyncConsole.WriteLine($"{d.ID} 已下载，正在跳过……");
                        if (!File.Exists(tmp))
                            return true;
                    }
                    else
                    {
                        SyncConsole.WriteLine($"{d.ID} {d.Filename} 开始下载");
                        using FileStream fileStream = File.Open(realTmp, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                        fileStream.SetLength(d.size);
                        await downloader.DownloadFileAsync(d, fileStream, ReportProgress, msgId, msgAuthor, msgChannel);
                        SyncConsole.WriteLine();
                        bool isFinished = downloader.FileManager.QueryIsFinished(d.ID, out _);
                        if (isFinished)
                        {
                            SyncConsole.WriteLine($"{d.ID} {d.Filename} 下载完成");
                        }
                        else
                        {
                            SyncConsole.WriteLine($"{d.ID} {d.Filename} 下载失败");
                            return false;
                        }
                    }
                    string fileName = d.Filename;
                    int extLength = RxEnd().Match(fileName).Length;
                    string originalFileName = Path.Combine(saveDir, fileName);
                    string realFileName = originalFileName;
                    int i = 1;
                    while (File.Exists(realFileName))
                    {
                        realFileName = AddNum(originalFileName, ++i, extLength);
                    }
                    File.Move(realTmp, realFileName, true);
                    string namePart = Path.GetFileName(realFileName);
                    SyncConsole.WriteLine($"将 {d.ID} 保存为 {namePart}");
                    downloader.FileManager.UpdateFileName(d.ID, namePart);

                    static string AddNum(ReadOnlySpan<char> original, int i, int extLength)
                    {
                        int mainLength = original.Length - extLength;
                        return $"{original[..mainLength]} ({i}){original[mainLength..]}";
                    }
                }
                break;
            default:
                SyncConsole.WriteLine("空");
                break;
        }
        return true;
    }

    static string Input(string defalutStr)
    {
        string? value = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(value) ? defalutStr : value;
    }
    public static async Task DownloaderMain()
    {
        cacheDir = Directory.CreateDirectory(Program.Config.App.CacheDir ?? cacheDir).FullName;
        saveDir = Directory.CreateDirectory(Program.Config.App.SaveDir ?? saveDir).FullName;

        if (Program.Config.Log.LogFile is not null)
        {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Program.Config.Log.LogFile);
            SyncConsole.LogFile = new StreamWriter(file, Program.UTF8_NoBOM, new()
            {
                Access = FileAccess.Write,
                Mode = FileMode.Create,
                Share = FileShare.Read
            });
        }
        SyncConsole.MinFileLogLevel = Program.Config.Log.MinFileLogLevel;
        SyncConsole.MinConsoleLogLevel = Program.Config.Log.MinConsoleLogLevel;


        Helpers.Log = SyncConsole.Log;
        using Client client = new(TGConfig)
        {
            MaxAutoReconnects = Program.Config.App.MaxConnectRetry,
            FilePartSize = 1024 * 1024
        };
        SetProxy(client);
        User user = await client.LoginUserIfNeeded();
        SyncConsole.WriteLine($"以 {user}（ID {user.id}）身份登录");
        Messages_Chats chats = await client.Messages_GetAllChats();
        foreach ((long id, ChatBase chat) in chats.chats)
            switch (chat)
            {
                case Chat smallgroup when smallgroup.IsActive:
                    SyncConsole.WriteLine($"{id}\t: SmallGroup: {smallgroup.title} with {smallgroup.participants_count} members");
                    break;
                case Channel channel when channel.IsChannel:
                    SyncConsole.WriteLine($"{id}\t: Channel {channel.username}: {channel.title}");
                    break;
                case Channel group:
                    SyncConsole.WriteLine($"{id}\t: Group {group.username}: {group.title}");
                    break;
            }
        using FileManager fm = new(Program.Config.SQLite.UseMemoryDB, Program.Config.SQLite.Pragma);
        MultiThreadDownloader downloader = new(
            client,
            10,
            1024 * 1024,
            fm,
            c => c.MaxAutoReconnects = Program.Config.App.MaxConnectRetry,
            SyncConsole.Log);
        SyncConsole.WriteLine("输入聊天 ID 来获取数据：");
        SyncConsole.Write(">>> ");
        long chatId = long.Parse(Input(""));
        var target = chats.chats[chatId];
        SyncConsole.WriteLine("输入起始消息 ID（留空即为从最新开始）：");
        SyncConsole.Write(">>> ");
        int offset_id = int.Parse(Input("0"));
        for (; ; )
        {
            Messages_MessagesBase messages = await client.Messages_GetHistory(target, offset_id);
            if (messages.Messages.Length == 0)
                break;
            foreach (MessageBase? iter in messages.Messages)
            {
                MessageBase? msgBase = iter;
                for (int retry = 0; retry < Program.Config.App.MaxDownloadRetry; retry++)
                {
                    int id = msgBase.ID;
                    IPeerInfo? from = messages.UserOrChat(msgBase.From ?? msgBase.Peer);
                    if (msgBase is Message msg && msg.media is not null)
                    {
                        SyncConsole.WriteLine($"[{from}/{msg.ID}/{msg.Date:O}]>>");
                        if (await ProcessMedia(downloader, msg.media, msg.ID, from.ID, chatId))
                            break;
                    }
                    else
                        break;
                    Messages_MessagesBase retryMessages = await client.Messages_GetHistory(target, id + 1, limit: 1);
                    if (retryMessages.Messages.Length == 0)
                        break;
                    msgBase = retryMessages.Messages[0];
                    if (msgBase.ID != id)
                        break;
                    SyncConsole.WriteLine("重试……");
                }
            }
            offset_id = messages.Messages[^1].ID;
        }
        SyncConsole.Log(LogLevel.WARN, "Done!");
    }
    static async Task<bool> ProcessMedia(MultiThreadDownloader downloader, MessageMedia msgMedia, long msgId, long msgAuthor, long msgChannel)
    {
        try
        {
            switch (msgMedia)
            {
                case MessageMediaPhoto photo:
                    SyncConsole.WriteLine("[Photo]", photo.photo.ID);
                    return await Download(downloader, photo.photo, msgId, msgAuthor, msgChannel);
                case MessageMediaDocument document:
                    SyncConsole.WriteLine("[Document]", document.document.ID);
                    return await Download(downloader, document.document, msgId, msgAuthor, msgChannel);
                case MessageMediaPaidMedia paidmedia:
                    SyncConsole.WriteLine("[PaidMedia]");
                    foreach (MessageExtendedMedia media in paidmedia.extended_media.OfType<MessageExtendedMedia>())
                    {
                        if (!await ProcessMedia(downloader, media.media, msgId, msgAuthor, msgChannel))
                            return false;
                    }
                    return true;
                default:
                    SyncConsole.WriteLine("[Unsupported]");
                    return true;
            }
        }
        catch (Exception e)
        {
            string s = $"[{DateTime.Now:O}]{Environment.NewLine}{e}{Environment.NewLine}";
            SyncConsole.Log(LogLevel.ERROR, s);
            return false;
        }
    }

    [GeneratedRegex(@"(?:(?:\.[a-zA-Z0-9_\-]*)*\.[^\.]*)?$", RegexOptions.RightToLeft)]
    private static partial Regex RxEnd();

    static void SetProxy(Client client)
    {
        SyncConsole.WriteLine("代理设置：");
        Config.ProxyType? selection = Program.Config.Proxy.Type;
        while (true)
        {
            if (selection is null)
            {
                SyncConsole.WriteLine("  0. 无代理",
                                      "  1. Socks4",
                                      "  2. Socks4a",
                                      "  3. Socks5",
                                      "  4. Http");
                SyncConsole.Write(">>> ");
                ReadOnlySpan<char> selectionSpan = Console.ReadLine().AsSpan().Trim();
                selection = selectionSpan.Length > 0 ?
                    selectionSpan[0] switch
                    {
                        '0' => Config.ProxyType.None,
                        '1' => Config.ProxyType.Socks4,
                        '2' => Config.ProxyType.Socks4a,
                        '3' => Config.ProxyType.Socks5,
                        '4' => Config.ProxyType.Http,
                        _ => null
                    } : Config.ProxyType.None;
            }
            else
            {
                SyncConsole.WriteLine($"当前模式：{selection}");
            }
            if (selection is Config.ProxyType.None)
                return;
            string? proxyHost = Program.Config.Proxy.Host;
            if (proxyHost is null)
            {
                SyncConsole.WriteLine("主机名（留空为 localhost）：");
                proxyHost = Input("localhost");
            }
            ushort? proxyPort = Program.Config.Proxy.Port;
            if (proxyPort is null)
            {
                SyncConsole.WriteLine("端口（留空为 1080）：");
                proxyPort = ushort.Parse(Input("1080"));
            }
            switch (selection)
            {
                case Config.ProxyType.Socks4:
                case Config.ProxyType.Socks4a:
                    string? user = Program.Config.Proxy.User;
                    if (user is null)
                    {
                        SyncConsole.WriteLine("用户信息（可留空）：");
                        user = Console.ReadLine();
                    }
                    switch (selection)
                    {
                        case Config.ProxyType.Socks4:
                            client.TcpHandler = (address, port) =>
                            {
                                Socks4ProxyClient proxy = string.IsNullOrEmpty(user) ?
                                    new(proxyHost, proxyPort.Value) :
                                    new(proxyHost, proxyPort.Value, user);
                                return Task.FromResult(proxy.CreateConnection(address, port));
                            };
                            break;
                        case Config.ProxyType.Socks4a:
                            client.TcpHandler = (address, port) =>
                            {
                                Socks4aProxyClient proxy = string.IsNullOrEmpty(user) ?
                                    new(proxyHost, proxyPort.Value) :
                                    new(proxyHost, proxyPort.Value, user);
                                return Task.FromResult(proxy.CreateConnection(address, port));
                            };
                            break;
                    }
                    return;
                case Config.ProxyType.Socks5:
                    string? userName = Program.Config.Proxy.User;
                    if (userName is null)
                    {
                        SyncConsole.WriteLine("用户名（可留空）：");
                        userName = Console.ReadLine();
                    }
                    string? userPassword = Program.Config.Proxy.User;
                    if (userName is null)
                    {
                        SyncConsole.WriteLine("用户密码（可留空）：");
                        userPassword = Console.ReadLine();
                    }
                    client.TcpHandler = (address, port) =>
                    {
                        Socks5ProxyClient proxy = string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(userPassword) ?
                            new(proxyHost, proxyPort.Value) :
                            new(proxyHost, proxyPort.Value, userName, userPassword);
                        return Task.FromResult(proxy.CreateConnection(address, port));
                    };
                    return;
                case Config.ProxyType.Http:
                    client.TcpHandler = (address, port) =>
                    {
                        HttpProxyClient proxy = new(proxyHost, proxyPort.Value);
                        return Task.FromResult(proxy.CreateConnection(address, port));
                    };
                    return;
                default:
                    continue;
            }
        }
    }
}
