using Microsoft.Data.Sqlite;
using TL;
using WTelegram;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace TGFileDownloader;

internal class FileManager : IDisposable
{
    struct Union<T1, T2>
    {
        public bool UseT2;
        public T1 Value1;
        public T1 Value2;
    }

    readonly SqliteConnection connection;
    readonly BlockingCollection<Union<Photo, Document>> queue = new(100);
    readonly Thread[] threads = new Thread[10];
    readonly Client tgClient;

    public FileManager(Client client, bool memoryDB = false)
    {
        tgClient = client;
        connection = new SqliteConnection(memoryDB ?
            "Data Source=:memory:" :
            "Data Source=TGFDL.db");
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS `FILES` (
    `ID`              INTEGER  PRIMARY KEY  NOT NULL,
    `RawFileName`     TEXT,
    `FileName`        TEXT,
    `Extension`       TEXT,
    `MIME`            TEXT,
    `FileType`        INTEGER               NOT NULL,
    `FileSize`        INTEGER               NOT NULL,
    `DownloadedSize`  INTEGER               NOT NULL,
    `IsFinished`      INTEGER               NOT NULL,
    `MessageID`       INTEGER               NOT NULL,
    `AuthorID`        INTEGER               NOT NULL,
    `ChannelID`       INTEGER               NOT NULL
)";
        command.ExecuteNonQuery();
    }

    private SqliteParameter CreateParameter(string name, object? value)
    {
        return new(name, value ?? DBNull.Value);
    }

    public TGFileInfo? GetFileInfo(long id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
@"
SELECT
    `ID`            ,
    `RawFileName`   ,
    `FileName`      ,
    `Extension`     ,
    `MIME`          ,
    `FileType`      ,
    `FileSize`      ,
    `DownloadedSize`,
    `IsFinished`    ,
    `MessageID`     ,
    `AuthorID`      ,
    `ChannelID`
FROM
    `FILES`
WHERE
    `ID` == $ID;
";
        SqliteParameter parameter = CreateParameter("$ID", id);
        command.Parameters.Add(parameter);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new()
        {
            ID = reader.GetInt64(0),
            RawFileName = reader.GetValue(1) as string,
            FileName = reader.GetValue(2) as string,
            Extension = reader.GetValue(3) as string,
            MIME = reader.GetValue(4) as string,
            FileType = (FileType)reader.GetInt64(5),
            FileSize = reader.GetInt64(6),
            DownloadedSize = reader.GetInt64(7),
            IsFinished = reader.GetBoolean(8),
            MessageID = reader.GetInt64(9),
            AuthorID = reader.GetInt64(10),
            ChannelID = reader.GetInt64(11)
        };
    }

    public int UpdateFileInfo(TGFileInfo info)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
@"
INSERT INTO `FILES` (
    `ID`            ,
    `RawFileName`   ,
    `FileName`      ,
    `Extension`     ,
    `MIME`          ,
    `FileType`      ,
    `FileSize`      ,
    `DownloadedSize`,
    `IsFinished`    ,
    `MessageID`     ,
    `AuthorID`      ,
    `ChannelID`
) VALUES (
    $ID            ,
    $RawFileName   ,
    $FileName      ,
    $Extension     ,
    $MIME          ,
    $FileType      ,
    $FileSize      ,
    $DownloadedSize,
    $IsFinished    ,
    $MessageID     ,
    $AuthorID      ,
    $ChannelID     
) ON CONFLICT (`ID`) DO UPDATE SET
    `RawFileName`    = excluded.`RawFileName`   ,
    `FileName`       = excluded.`FileName`      ,
    `Extension`      = excluded.`Extension`     ,
    `MIME`           = excluded.`MIME`          ,
    `FileType`       = excluded.`FileType`      ,
    `FileSize`       = excluded.`FileSize`      ,
    `DownloadedSize` = excluded.`DownloadedSize`,
    `IsFinished`     = excluded.`IsFinished`    ,
    `MessageID`      = excluded.`MessageID`     ,
    `AuthorID`       = excluded.`AuthorID`      ,
    `ChannelID`      = excluded.`ChannelID`     ;
";
        SqliteParameter pID = CreateParameter("$ID", info.ID);
        command.Parameters.Add(pID);
        SqliteParameter pRawFileName = CreateParameter("$RawFileName", info.RawFileName);
        command.Parameters.Add(pRawFileName);
        SqliteParameter pFileName = CreateParameter("$FileName", info.FileName);
        command.Parameters.Add(pFileName);
        SqliteParameter pExtension = CreateParameter("$Extension", info.Extension);
        command.Parameters.Add(pExtension);
        SqliteParameter pMIME = CreateParameter("$MIME", info.MIME);
        command.Parameters.Add(pMIME);
        SqliteParameter pFileType = CreateParameter("$FileType", (long)info.FileType);
        command.Parameters.Add(pFileType);
        SqliteParameter pFileSize = CreateParameter("$FileSize", info.FileSize);
        command.Parameters.Add(pFileSize);
        SqliteParameter pDownloadedSize = CreateParameter("$DownloadedSize", info.DownloadedSize);
        command.Parameters.Add(pDownloadedSize);
        SqliteParameter pIsFinished = CreateParameter("$IsFinished", info.IsFinished);
        command.Parameters.Add(pIsFinished);
        SqliteParameter pMessageID = CreateParameter("$MessageID", info.MessageID);
        command.Parameters.Add(pMessageID);
        SqliteParameter pAuthorID = CreateParameter("$AuthorID", info.AuthorID);
        command.Parameters.Add(pAuthorID);
        SqliteParameter pChannelID = CreateParameter("$ChannelID", info.ChannelID);
        command.Parameters.Add(pChannelID);

        return command.ExecuteNonQuery();
    }

    public void UpdateFileProgress(long id, long downloadedSize, Storage_FileType type)
    {
        bool isPhoto = false;
        using (SqliteCommand commandRead = connection.CreateCommand())
        {
            commandRead.CommandText =
    @"
SELECT
    `FileType`
FROM
    `FILES`
WHERE
    `ID` == $ID;
";
            SqliteParameter parameter = CreateParameter("$ID", id);
            commandRead.Parameters.Add(parameter);
            switch (commandRead.ExecuteScalar())
            {
                case null:
                    return;
                case long v when v == (long)FileType.Photo:
                    isPhoto = true;
                    break;
            }
        }
        using SqliteCommand commandWrite = connection.CreateCommand();
        if (isPhoto)
        {
            commandWrite.CommandText =
@"
UPDATE `FILES` SET
    `Extension`      = $Extension     ,
    `MIME`           = $MIME          ,
    `DownloadedSize` = $DownloadedSize
WHERE
    `ID`             = $ID            ;
";
            SqliteParameter pExtension = CreateParameter("$Extension", $".{type}");
            commandWrite.Parameters.Add(pExtension);
            SqliteParameter pMIME = CreateParameter("$MIME", type.GetMIME());
            commandWrite.Parameters.Add(pMIME);
            SqliteParameter pDownloadedSize = CreateParameter("$DownloadedSize", downloadedSize);
            commandWrite.Parameters.Add(pDownloadedSize);
            SqliteParameter pID = CreateParameter("$ID", id);
            commandWrite.Parameters.Add(pID);
        }
        else
        {
            commandWrite.CommandText =
@"
UPDATE `FILES` SET
    `DownloadedSize` = $DownloadedSize
WHERE
    `ID`             = $ID            ;
";
            SqliteParameter pDownloadedSize = CreateParameter("$DownloadedSize", downloadedSize);
            commandWrite.Parameters.Add(pDownloadedSize);
            SqliteParameter pID = CreateParameter("$ID", id);
            commandWrite.Parameters.Add(pID);
        }
        commandWrite.ExecuteNonQuery();

    }

    public bool QueryIsFinished(long id, out string? fileName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
@"
SELECT
    `IsFinished`,
    `FileName`
FROM
    `FILES`
WHERE
    `ID` == $ID;
";
        SqliteParameter parameter = CreateParameter("$ID", id);
        command.Parameters.Add(parameter);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            fileName = null;
            return false;
        }
        fileName = reader.GetValue(1) as string;
        return reader.GetBoolean(0);
    }

    public int UpdateFileIsFinished(long id, bool isFinished)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
@"
UPDATE `FILES` SET
    `IsFinished`     = $IsFinished
WHERE
    `ID`             = $ID            ;
";
        SqliteParameter pID = CreateParameter("$ID", id);
        command.Parameters.Add(pID);
        SqliteParameter pIsFinished = CreateParameter("$IsFinished", isFinished);
        command.Parameters.Add(pIsFinished);

        return command.ExecuteNonQuery();
    }

    public int UpdateFileName(long id, string fileName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
@"
UPDATE `FILES` SET
    `FileName`       = $FileName
WHERE
    `ID`             = $ID            ;
";
        SqliteParameter pID = CreateParameter("$ID", id);
        command.Parameters.Add(pID);
        SqliteParameter pFileName = CreateParameter("$FileName", fileName);
        command.Parameters.Add(pFileName);

        return command.ExecuteNonQuery();
    }

    public async Task<string> DownloadFileAsync(
        Document document,
        Stream outputStream,
        Client.ProgressCallback? progress = null,
        long message = 0,
        long author = 0,
        long channel = 0)
    {
        long id = document.id;
        InputDocumentFileLocation location = document.ToFileLocation((PhotoSizeBase?)null);
        TGFileInfo? fileInfo = GetFileInfo(id);
        if (!fileInfo.HasValue)
        {
            fileInfo = new()
            {
                ID = id,
                RawFileName = document.Filename,
                Extension = Path.GetExtension(document.Filename),
                MIME = document.mime_type,
                FileType = FileType.Document,
                FileSize = document.size,
                DownloadedSize = 0,
                IsFinished = false,
                MessageID = message,
                AuthorID = author,
                ChannelID = channel
            };
            UpdateFileInfo(fileInfo.Value);
        }
        await DownloadFileAsync(id, location, outputStream, document.dc_id, fileInfo.Value.DownloadedSize, document.size, progress);
        return document.mime_type;
    }
    public async Task<Storage_FileType> DownloadFileAsync(
        Photo photo,
        Stream outputStream,
        Client.ProgressCallback? progress = null,
        long message = 0,
        long author = 0,
        long channel = 0)
    {
        long id = photo.id;
        PhotoSizeBase photoSize = photo.LargestPhotoSize;
        InputPhotoFileLocation location = photo.ToFileLocation(photoSize);
        TGFileInfo? fileInfo = GetFileInfo(id);
        if (!fileInfo.HasValue)
        {
            fileInfo = new()
            {
                ID = id,
                RawFileName = null,
                Extension = null,
                MIME = null,
                FileType = FileType.Photo,
                FileSize = photoSize.FileSize,
                DownloadedSize = 0,
                IsFinished = false,
                MessageID = message,
                AuthorID = author,
                ChannelID = channel
            };
            UpdateFileInfo(fileInfo.Value);
        }
        return await DownloadFileAsync(id, location, outputStream, photo.dc_id, fileInfo.Value.DownloadedSize, photoSize.FileSize, progress);
    }
    public async Task<Storage_FileType> DownloadFileAsync(
        long id,
        InputFileLocationBase fileLocation,
        Stream outputStream,
        int dc_id,
        long offset = 0,
        long fileSize = 0,
        Client.ProgressCallback? progress = null)
    {
        Storage_FileType fileType = Storage_FileType.unknown;
        Client client = await tgClient.GetClientForDC(-dc_id, true);
        if (outputStream.CanSeek)
        {
            outputStream.SetLength(offset);
            outputStream.Seek(offset, SeekOrigin.Begin);
        }
        UpdateFileProgress(id, offset, fileType);
        progress?.Invoke(offset, fileSize);
        bool abort = false;
        while (!abort)
        {
            Upload_FileBase fileBase;
            try
            {
                fileBase = await client.Upload_GetFile(fileLocation, offset, client.FilePartSize);
            }
            catch (RpcException ex) when (ex.Code == 303 && ex.Message == "FILE_MIGRATE_X")
            {
                client = await client.GetClientForDC(-ex.X, true);
                fileBase = await client.Upload_GetFile(fileLocation, offset, client.FilePartSize);
            }
            catch (RpcException ex) when (ex.Code == 400 && ex.Message == "OFFSET_INVALID")
            {
                abort = true;
                break;
            }
            catch (Exception)
            {
                await outputStream.FlushAsync();
                throw;
            }
            if (fileBase is not Upload_File fileData)
                throw new WTException("Upload_GetFile returned unsupported " + fileBase?.GetType().Name);
            if (fileData.bytes.Length != client.FilePartSize)
                abort = true;
            if (fileData.bytes.Length != 0)
            {
                fileType = fileData.type;
                try
                {
                    await outputStream.WriteAsync(fileData.bytes.AsMemory());
                    offset += fileData.bytes.Length;
                    UpdateFileProgress(id, offset, fileType);
                    progress?.Invoke(offset, fileSize);
                }
                catch (Exception)
                {
                    await outputStream.FlushAsync();
                    throw;
                }
                finally
                {
                }
            }
            if (fileSize != 0 && offset > fileSize)
                throw new WTException("Downloaded file size does not match expected file size");
        }
        await outputStream.FlushAsync();
        UpdateFileIsFinished(id, true);
        return fileType;
    }

    public void Dispose()
    {
        connection.Dispose();
    }
}

struct TGFileInfo
{
    public long ID;
    public string? RawFileName;
    public string? FileName;
    public string? Extension;
    public string? MIME;
    public FileType FileType;
    public long FileSize;
    public long DownloadedSize;
    public bool IsFinished;
    public long MessageID;
    public long AuthorID;
    public long ChannelID;
}
enum FileType
{
    Photo,
    Document
}