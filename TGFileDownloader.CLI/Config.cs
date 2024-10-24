using System.Text.Json;
using System.Text.Json.Serialization;

namespace TGFileDownloader.CLI;

public partial class Config
{
    [Generated.NonNullSetter(Accessibility = "public")]
    private TelegramInfo telegram = new();
    [Generated.NonNullSetter(Accessibility = "public")]
    private AppInfo app = new();
    [Generated.NonNullSetter(Accessibility = "public")]
    private LogInfo log = new();
    [Generated.NonNullSetter(Accessibility = "public")]
    private ProxyInfo proxy = new();
    [Generated.NonNullSetter(Accessibility = "public")]
    private SQLiteInfo sQLite = new();

    public class TelegramInfo
    {
        public string? API_Hash { get; set; } = null;
        public string? API_ID { get; set; } = null;
        public string? PhoneNumber { get; set; } = null;
    }
    public partial class AppInfo
    {
        public int MaxConnectRetry { get; set; } = int.MaxValue;
        public int MaxDownloadRetry { get; set; } = 100;
        [Generated.NonNullSetter(Accessibility = "public")]
        public string cacheDir = Program.DefaultCacheDir;
        [Generated.NonNullSetter(Accessibility = "public")]
        public string saveDir = Program.DefaultSaveDir;
        public double? SpeedMoniterUpdateTime { get; set; } = 1;
    }
    public partial class LogInfo
    {
        public LogLevel MinConsoleLogLevel { get; set; } = LogLevel.WARN;
        public LogLevel MinFileLogLevel { get; set; } = LogLevel.INFO;
        public string? LogFile { get; set; } = "TGFDL.log";
    }
    public class ProxyInfo
    {
        public ProxyType? Type { get; set; } = null;
        public string? Host { get; set; } = null;
        public ushort? Port { get; set; } = null;
        public string? User { get; set; } = null;
        public string? Password { get; set; } = null;
    }
    public class SQLiteInfo
    {
        public bool UseMemoryDB { get; set; } = false;
        public Dictionary<string, string>? Pragma { get; set; } = null;
    }
    public enum ProxyType
    {
        None,
        Socks4,
        Socks4a,
        Socks5,
        Http
    }

    [JsonIgnore]
    static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static Config Load(string file)
    {
        file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
        if (!File.Exists(file))
            return new();
        using FileStream fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<Config>(fs, options) ?? new();
    }

    public void Save(string file)
    {
        file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
        using FileStream fs = File.Open(file, FileMode.Create, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(fs, this, options);
    }
}
