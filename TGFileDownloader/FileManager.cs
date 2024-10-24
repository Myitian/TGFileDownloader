using Microsoft.Data.Sqlite;
using TL;

namespace TGFileDownloader;

public class FileManager : IDisposable
{
    readonly object SQLock = new();
    readonly SqliteConnection connection;
    bool disposed = false;

    public Action<LogLevel, string>? Log { get; set; }

    public FileManager(bool memoryDB = false, params IEnumerable<KeyValuePair<string, string>>? pragma)
    {
        connection = new SqliteConnection(memoryDB ?
            "Data Source=:memory:" :
            "Data Source=TGFDL.db");
        connection.Open();

        using (SqliteCommand commandPragma = connection.CreateCommand())
        {
            commandPragma.CommandText = "PRAGMA $k = $v;";
            SqliteParameter pK = CreateParameter("$k");
            commandPragma.Parameters.Add(pK);
            SqliteParameter pV = CreateParameter("$v");
            commandPragma.Parameters.Add(pV);
            if (pragma is not null)
                foreach ((string key, string value) in pragma)
                    try
                    {
                        Log?.Invoke(LogLevel.INFO, $"Try set pragma {key} to {value}");
                        pK.Value = key;
                        pV.Value = value;
                        commandPragma.ExecuteNonQuery();
                    }
                    catch { }
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
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
                    `ChannelID`       INTEGER               NOT NULL,
                    `ExtraData`       BLOB
                )
                """;
            command.ExecuteNonQuery();
        }

        try
        {
            const string sql = "ALTER TABLE `FILES` ADD COLUMN `ExtraData` BLOB;";
            Log?.Invoke(LogLevel.INFO, $"Try execute {sql}");
            using SqliteCommand commandAddBlob = connection.CreateCommand();
            commandAddBlob.CommandText = sql;
            commandAddBlob.ExecuteNonQuery();
        }
        catch { }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            connection.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    public Stream? GetExtraData(long id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                `ExtraData`
            FROM
                `FILES`
            WHERE
                `ID` == $ID;
            """;
        SqliteParameter parameter = CreateParameter("$ID", id);
        command.Parameters.Add(parameter);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        if (reader.IsDBNull(0))
            return null;
        return reader.GetStream(0);
    }

    public TGFileInfo? GetFileInfo(long id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
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
            """;
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

    public bool QueryIsFinished(long id, out string? fileName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                `IsFinished`,
                `FileName`
            FROM
                `FILES`
            WHERE
                `ID` == $ID;
            """;
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

    public int UpdateExtraData(long id, byte[] data)
    {
        lock (SQLock)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                UPDATE `FILES` SET
                    `ExtraData`      = $ExtraData
                WHERE
                    `ID`             = $ID            ;
                """;
            SqliteParameter pID = CreateParameter("$ID", id);
            command.Parameters.Add(pID);
            SqliteParameter pExtraData = CreateParameter("$ExtraData", data);
            command.Parameters.Add(pExtraData);

            return command.ExecuteNonQuery();
        }
    }

    public int UpdateFileInfo(TGFileInfo info)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
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
            """;
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

    public int UpdateFileIsFinished(long id, bool isFinished)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE `FILES` SET
                `IsFinished`     = $IsFinished
            WHERE
                `ID`             = $ID            ;
            """;
        SqliteParameter pID = CreateParameter("$ID", id);
        command.Parameters.Add(pID);
        SqliteParameter pIsFinished = CreateParameter("$IsFinished", isFinished);
        command.Parameters.Add(pIsFinished);

        return command.ExecuteNonQuery();
    }

    public int UpdateFileName(long id, string fileName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE `FILES` SET
                `FileName`       = $FileName
            WHERE
                `ID`             = $ID            ;
            """;
        SqliteParameter pID = CreateParameter("$ID", id);
        command.Parameters.Add(pID);
        SqliteParameter pFileName = CreateParameter("$FileName", fileName);
        command.Parameters.Add(pFileName);

        return command.ExecuteNonQuery();
    }

    public void UpdateFileProgress(long id, long downloadedSize, Storage_FileType? type = null)
    {
        lock (SQLock)
        {
            bool isPhoto = false;
            if (type.HasValue)
                using (SqliteCommand commandRead = connection.CreateCommand())
                {
                    commandRead.CommandText = """
                        SELECT
                            `FileType`
                        FROM
                            `FILES`
                        WHERE
                            `ID` == $ID;
                        """;
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
            if (isPhoto && type.HasValue)
            {
                commandWrite.CommandText = """
                    UPDATE `FILES` SET
                        `Extension`      = $Extension     ,
                        `MIME`           = $MIME          ,
                        `DownloadedSize` = $DownloadedSize
                    WHERE
                        `ID`             = $ID            ;
                    """;
                SqliteParameter pExtension = CreateParameter("$Extension", $".{type.Value}");
                commandWrite.Parameters.Add(pExtension);
                SqliteParameter pMIME = CreateParameter("$MIME", type.Value.GetMIME());
                commandWrite.Parameters.Add(pMIME);
                SqliteParameter pDownloadedSize = CreateParameter("$DownloadedSize", downloadedSize);
                commandWrite.Parameters.Add(pDownloadedSize);
                SqliteParameter pID = CreateParameter("$ID", id);
                commandWrite.Parameters.Add(pID);
            }
            else
            {
                commandWrite.CommandText = """
                    UPDATE `FILES` SET
                        `DownloadedSize` = $DownloadedSize
                    WHERE
                        `ID`             = $ID            ;
                    """;
                SqliteParameter pDownloadedSize = CreateParameter("$DownloadedSize", downloadedSize);
                commandWrite.Parameters.Add(pDownloadedSize);
                SqliteParameter pID = CreateParameter("$ID", id);
                commandWrite.Parameters.Add(pID);
            }
            commandWrite.ExecuteNonQuery();
        }
    }

    static SqliteParameter CreateParameter(string name, object? value = null)
    {
        return new(name, value ?? DBNull.Value);
    }
}
