using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace TGFileDownloader;
public enum PartStatus
{
    Normal,
    Error,
    Fatal
}

public struct Part(long offset, long length) : IComparable<Part>
{
    public long Offset = offset, Length = length;

    public readonly int CompareTo(Part other)
    {
        return Offset.CompareTo(other.Offset);
    }
}
public struct PartResult(PartStatus status, string? message = null)
{
    public string? Message = message;
    public PartStatus Status = status;
}
public class PartManager
{
    public Action<long, int, IEnumerable<Part>>? PartInfoSaver;
    private readonly object lockObj = new();
    private readonly ConcurrentBag<Part> unusedParts = [];
    private readonly ConcurrentDictionary<long, Part> usingParts = [];

    public bool IsCompleted { get; private set; } = false;
    public bool IsFailed { get; private set; } = false;



    public Action<LogLevel, string>? Log { get; set; }

    public PartManager(int partSize, params IEnumerable<Part>? parts)
    {
        PartSize = partSize;
        LoadParts(parts: parts);
    }

    public int Count => unusedParts.Count + usingParts.Count;
    public int PartSize { get; }
    public long TotalLength
    {
        get
        {
            long v = 0;
            lock (lockObj)
                foreach (Part part in GetParts())
                    v += part.Length;
            return v;
        }
    }
    public static IEnumerable<Part> LoadPartsFromStream(Stream stream)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(16);

        stream.ReadExactly(buffer, 0, 4);
        int count = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        for (int i = 0; i < count; i++)
        {
            stream.ReadExactly(buffer);
            Span<byte> offsetBuffer = buffer.AsSpan();
            Span<byte> lengthBuffer = buffer.AsSpan(8);
            long offset = BinaryPrimitives.ReadInt64LittleEndian(offsetBuffer);
            long length = BinaryPrimitives.ReadInt64LittleEndian(lengthBuffer);
            yield return new(offset, length);
        }
    }

    public static void SavePartsToBytes(int count, IEnumerable<Part> parts, Span<byte> destination)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, count);
        destination = destination[4..];
        foreach (Part part in parts)
        {
            Span<byte> offsetBuffer = destination[..8];
            Span<byte> lengthBuffer = destination[8..];
            BinaryPrimitives.WriteInt64LittleEndian(offsetBuffer, part.Offset);
            BinaryPrimitives.WriteInt64LittleEndian(lengthBuffer, part.Length);
            destination = destination[16..];
        }
    }

    public void LoadParts(Part? clamp = null, params IEnumerable<Part>? parts)
    {
        if (!usingParts.IsEmpty)
            throw new InvalidOperationException();
        lock (lockObj)
        {
            IsCompleted = false;
            IsFailed = false;
            unusedParts.Clear();

            List<Part> partList = [.. parts];
            Span<Part> partSpan = CollectionsMarshal.AsSpan(partList);
            partSpan.Sort();

            Log?.Invoke(LogLevel.DEBUG, $"Load parts #1: {partSpan.Length}");

            if (clamp.HasValue)
            {
                long lowerBound = clamp.Value.Offset;
                long upperBound = lowerBound + clamp.Value.Length;
                for (int i = 0; i < partSpan.Length; i++)
                {
                    ref Part p = ref partSpan[i];
                    long pLower = p.Offset;
                    long pUpper = pLower + p.Length;
                    if (pLower >= upperBound || pUpper <= lowerBound)
                    {
                        p.Length = 0;
                        continue;
                    }
                    if (pLower < lowerBound)
                    {
                        long diff = lowerBound - pLower;
                        p.Offset += diff;
                        p.Length -= diff;
                    }
                    if (pUpper > upperBound)
                    {
                        long diff = pUpper - upperBound;
                        p.Length -= diff;
                    }
                }
            }
            switch (partSpan.Length)
            {
                case 0:
                    break;
                case 1:
                    unusedParts.Add(partSpan[0]);
                    break;
                default:
                    for (int i = 1; i < partSpan.Length; i++)
                    {
                        ref Part A = ref partSpan[i - 1];
                        ref Part B = ref partSpan[i];
                        if (A.Offset == B.Offset)
                        {
                            B.Length = Math.Max(A.Length, B.Length);
                            A.Length = 0;
                        }
                        else if (A.Offset + A.Length >= B.Offset)
                        {
                            long diff = B.Offset - A.Offset;
                            B.Length = Math.Max(A.Length, B.Length + diff);
                            B.Offset = A.Offset;
                            A.Length = 0;
                        }
                    }
                    foreach (Part part in partSpan)
                    {
                        if (part.Length > 0)
                            unusedParts.Add(part);
                    }
                    break;
            }

            Log?.Invoke(LogLevel.DEBUG, $"Load parts #2: {unusedParts.Count}");
        }
    }

    public void ReportPartResult(long offset, PartResult result)
    {
        lock (lockObj)
        {
            switch (result.Status)
            {
                case PartStatus.Normal:
                    usingParts.Remove(offset, out _);
                    if (Count == 0)
                        IsCompleted = true;
                    break;
                case PartStatus.Error:
                    Log?.Invoke(LogLevel.WARN, $"Part {offset}: {result.Status} {result.Message}");
                    if (usingParts.Remove(offset, out Part part))
                        unusedParts.Add(part);
                    break;
                case PartStatus.Fatal:
                    Log?.Invoke(LogLevel.ERROR, $"Part {offset}: {result.Status} {result.Message}");
                    IsCompleted = true;
                    IsFailed = true;
                    if (usingParts.Remove(offset, out part))
                        unusedParts.Add(part);
                    break;
            }
        }
    }

    public Part? RequestPart()
    {
        if (IsCompleted)
            return null;
        lock (lockObj)
        {
            if (!unusedParts.TryTake(out Part part))
                return null;
            usingParts[part.Offset] = part;
            if (part.Length <= PartSize)
            {
                return part;
            }
            Part retPart = new(part.Offset, PartSize);
            part.Offset += PartSize;
            part.Length -= PartSize;
            unusedParts.Add(part);
            usingParts[retPart.Offset] = retPart;
            return retPart;
        }
    }

    public void SaveParts(long id)
    {
        lock (lockObj)
        {
            PartInfoSaver?.Invoke(id, Count, GetParts());
        }
    }

    private IEnumerable<Part> GetParts()
    {
        foreach ((_, Part part) in usingParts)
        {
            yield return part;
        }
        foreach (Part part in unusedParts)
        {
            yield return part;
        }
    }
}