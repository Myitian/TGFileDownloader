namespace TGFileDownloader;

public struct TGFileInfo
{
    public long AuthorID;
    public long ChannelID;
    public long DownloadedSize;
    public string? Extension;
    public string? FileName;
    public long FileSize;
    public FileType FileType;
    public long ID;
    public bool IsFinished;
    public long MessageID;
    public string? MIME;
    public string? RawFileName;
}
public enum FileType
{
    Photo,
    Document
}