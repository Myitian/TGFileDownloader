using System.Buffers;
using TL;
using WTelegram;

namespace TGFileDownloader;

public delegate void ProgressChangedCallback(long transmitted, long totalSize, bool newFile = false);

public class MultiThreadDownloader
{
    private readonly object downloadLock = new();
    private readonly PartManager partManager;

    public MultiThreadDownloader(
        Client client,
        int threadCount,
        int maxPartSize,
        FileManager fileManager,
        Action<Client>? clientInit = null,
        Action<LogLevel, string>? log = null)
    {
        Client = client;
        FileManager = fileManager;
        partManager = new(maxPartSize)
        {
            PartInfoSaver = SaveParts
        };
        ThreadCount = threadCount;
        ClientInit = clientInit;
        Log = log;
    }

    public Client Client { get; }
    public FileManager FileManager { get; }
    public Action<Client>? ClientInit { get; set; }
    public Action<LogLevel, string>? Log { get => partManager.Log; set => partManager.Log = value; }

    public int ThreadCount { get; set; }

    public async Task<Storage_FileType> DownloadFileAsync(
        Photo photo,
        Stream outputStream,
        ProgressChangedCallback? progress = null,
        long message = 0,
        long author = 0,
        long channel = 0)
    {
        long id = photo.id;
        PhotoSizeBase photoSize = photo.LargestPhotoSize;
        TGFileInfo? fileInfo = FileManager.GetFileInfo(id);
        if (fileInfo.HasValue)
        {
            progress?.Invoke(fileInfo.Value.DownloadedSize, photoSize.FileSize, true);
        }
        else
        {
            progress?.Invoke(0, photoSize.FileSize, true);
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
            FileManager.UpdateFileInfo(fileInfo.Value);
        }
        InputPhotoFileLocation location = photo.ToFileLocation(photoSize);
        Storage_FileType result = await DownloadFileAsync(location, outputStream, id, photo.dc_id, photoSize.FileSize, 0, progress);
        return result;
    }

    public async Task<Storage_FileType> DownloadFileAsync(
        Document document,
        Stream outputStream,
        ProgressChangedCallback? progress = null,
        long message = 0,
        long author = 0,
        long channel = 0)
    {
        long id = document.id;
        TGFileInfo? fileInfo = FileManager.GetFileInfo(id);
        if (fileInfo.HasValue)
        {
            progress?.Invoke(fileInfo.Value.DownloadedSize, document.size, true);
        }
        else
        {
            progress?.Invoke(0, document.size, true);
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
            FileManager.UpdateFileInfo(fileInfo.Value);

        }
        InputDocumentFileLocation location = document.ToFileLocation((PhotoSizeBase?)null);
        Storage_FileType result = await DownloadFileAsync(location, outputStream, id, document.dc_id, document.size, 0, progress);
        return result;
    }

    public async Task<Storage_FileType> DownloadFileAsync(
        InputFileLocationBase fileLocation,
        Stream outputStream,
        long id,
        int dc_id,
        long fileSize,
        long offset = 0,
        ProgressChangedCallback? progress = null)
    {
        if (outputStream.CanSeek)
        {
            return await Task.Run(() => ParallelDownloadFile(fileLocation, outputStream, id, dc_id, fileSize, offset, progress));
        }
        else
        {
            return await SequentialDownloadFileAsync(fileLocation, outputStream, id, dc_id, fileSize, offset, progress);
        }
    }

    public async Task<Storage_FileType> SequentialDownloadFileAsync(
        InputFileLocationBase fileLocation,
        Stream outputStream,
        long id,
        int dc_id,
        long fileSize = 0,
        long offset = 0,
        ProgressChangedCallback? progress = null)
    {
        Log?.Invoke(LogLevel.INFO, $"Sequential download started: {id}");
        Storage_FileType fileType = Storage_FileType.unknown;
        Client client = await GetClientForDC(Client, -dc_id, true);
        if (outputStream.CanSeek)
        {
            outputStream.SetLength(offset);
            outputStream.Seek(offset, SeekOrigin.Begin);
        }
        FileManager.UpdateFileProgress(id, offset, fileType);
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
                client = await GetClientForDC(client, -ex.X, true);
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
                    FileManager.UpdateFileProgress(id, offset, fileType);
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
        FileManager.UpdateFileProgress(id, offset, fileType);
        progress?.Invoke(offset, fileSize);
        FileManager.UpdateFileIsFinished(id, true);
        return fileType;
    }

    public Storage_FileType ParallelDownloadFile(
        InputFileLocationBase fileLocation,
        Stream outputStream,
        long id,
        int dc_id,
        long fileSize,
        long offset = 0,
        ProgressChangedCallback? progress = null)
    {
        Log?.Invoke(LogLevel.INFO, $"Parallel download started: {id}");
        lock (downloadLock)
        {
            Part mainArea = new(offset, fileSize - offset);
            using (Stream? s = FileManager.GetExtraData(id))
            {
                if (s is null)
                {
                    TGFileInfo? fi = FileManager.GetFileInfo(id);
                    long offs2 = fi?.DownloadedSize ?? offset;
                    partManager.LoadParts(mainArea, new Part(offs2, fileSize - offs2));
                }
                else
                {
                    partManager.LoadParts(mainArea, PartManager.LoadPartsFromStream(s));
                }
            }
            int threadCount = ThreadCount;
            using DownloadInfo info = new(
                Client,
                fileLocation,
                outputStream,
                id,
                dc_id,
                fileSize - partManager.TotalLength,
                fileSize,
                progress,
                threadCount);
            for (int i = 0; i < threadCount; i++)
            {
                ThreadPool.QueueUserWorkItem(PartsConsumer, info, false);
            }
            info.AllDone.WaitOne();
            FileManager.UpdateFileProgress(info.ID, info.Transmitted);
            info.ProgressChanged?.Invoke(info.Transmitted, info.FileSize);
            if (partManager.Count == 0 && partManager.TotalLength == 0 && info.FailedThreads == 0)
            {
                FileManager.UpdateFileIsFinished(id, true);
            }
            partManager.SaveParts(id);
            return info.FileType;
        }
    }

    private async Task<PartResult> DownloadPartAsync(
        Part part,
        int partLength,
        DownloadInfo info)
    {
        Client client = await GetClientForDC(info.Client, -info.DcID, true);
        Upload_FileBase fileBase;
        try
        {
            fileBase = await client.Upload_GetFile(info.FileLocation, part.Offset, partLength);
        }
        catch (RpcException ex) when (ex.Code == 303 && ex.Message == "FILE_MIGRATE_X")
        {
            client = await GetClientForDC(info.Client, -ex.X, true);
            fileBase = await client.Upload_GetFile(info.FileLocation, part.Offset, partLength);
        }
        catch (Exception ex)
        {
            return new(PartStatus.Fatal, ex.Message);
        }
        if (fileBase is not Upload_File fileData)
            return new(PartStatus.Fatal, "Upload_GetFile returned unsupported " + fileBase?.GetType().Name);
        if (fileData.bytes.Length == 0)
            return new(PartStatus.Normal);
        if (fileData.bytes.Length != part.Length)
            return new(PartStatus.Error, "fileData.bytes.Length != part.Length");
        if (fileData.bytes.Length != 0)
        {
            info.FileType = fileData.type;
            lock (info.Lock)
            {
                try
                {
                    info.OutputStream.Seek(part.Offset, SeekOrigin.Begin);
                    info.OutputStream.Write(fileData.bytes);
                    info.OutputStream.Flush();
                }
                catch (Exception ex)
                {
                    return new(PartStatus.Fatal, ex.Message);
                }
            }
            try
            {
                Interlocked.Add(ref info.Transmitted, fileData.bytes.Length);
                FileManager.UpdateFileProgress(info.ID, info.Transmitted, fileData.type);
                info.ProgressChanged?.Invoke(info.Transmitted, info.FileSize);
            }
            catch (Exception ex)
            {
                return new(PartStatus.Normal, ex.Message);
            }
        }
        return new(PartStatus.Normal);
    }

    private async Task<Client> GetClientForDC(Client client, int dcId, bool connect = true)
    {
        Client c = await client.GetClientForDC(dcId, connect);
        ClientInit?.Invoke(c);
        return c;
    }

    private async void PartsConsumer(DownloadInfo info)
    {
        Part? part;
        try
        {
            bool failed = false;
            while ((part = partManager.RequestPart()).HasValue)
            {
                PartResult result = new(PartStatus.Fatal);
                try
                {
                    int len = partManager.PartSize;
                    result = await DownloadPartAsync(part.Value, len, info);
                }
                catch
                {
                    failed = true;
                    throw;
                }
                finally
                {
                    partManager.ReportPartResult(part.Value.Offset, result);
                    partManager.SaveParts(info.ID);
                }
            }
            if (!failed)
                Interlocked.Decrement(ref info.FailedThreads);
        }
        finally
        {
            if (Interlocked.Decrement(ref info.RunningThreads) == 0)
                info.AllDone.Set();
        }
    }

    private void SaveParts(long id, int count, IEnumerable<Part> parts)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(sizeof(int) + 2 * sizeof(long) * count);
        try
        {
            PartManager.SavePartsToBytes(count, parts, buffer);
            FileManager.UpdateExtraData(id, buffer);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public class DownloadInfo(
        Client client,
        InputFileLocationBase fileLocation,
        Stream outputStream,
        long id,
        int dcID,
        long transmitted,
        long fileSize,
        ProgressChangedCallback? progressChanged,
        int runningThreads) : IDisposable
    {
        public ManualResetEvent AllDone = new(false);
        public Client Client = client;
        public int DcID = dcID;
        public InputFileLocationBase FileLocation = fileLocation;
        public long FileSize = fileSize;
        public Storage_FileType FileType = Storage_FileType.unknown;
        public long ID = id;
        public object Lock = new();
        public Stream OutputStream = outputStream;
        public ProgressChangedCallback? ProgressChanged = progressChanged;
        public int RunningThreads = runningThreads;
        public int FailedThreads = runningThreads;
        public long Transmitted = transmitted;

        public void Dispose()
        {
            AllDone.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}