using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AlgoTrading.Research.Persistence;

public sealed class BoundedEvidenceWriter<T> : IDisposable
{
    private const int MaxRecordsLimit = 512;
    private const long MaxBytesLimit = 4 * 1024 * 1024; // 4 MiB

    private readonly AtomicResearchPublisher _publisher;
    private readonly string _relativePath;
    private readonly List<T> _buffer = new(MaxRecordsLimit);

    private long _currentBufferBytes;
    private long _totalRecordCount;
    private int _partIndex;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BoundedEvidenceWriter(AtomicResearchPublisher publisher, string relativePathBase)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _relativePath = relativePathBase;
    }

    public void Append(T record)
    {
        ArgumentNullException.ThrowIfNull(record);

        string jsonLine = JsonSerializer.Serialize(record, JsonOptions) + "\n";
        byte[] lineBytes = Encoding.UTF8.GetBytes(jsonLine);

        // Check buffer boundary limits (512 records or 4 MiB)
        if (_buffer.Count >= MaxRecordsLimit || (_currentBufferBytes + lineBytes.Length) > MaxBytesLimit)
        {
            FlushChunk();
        }

        _buffer.Add(record);
        _currentBufferBytes += lineBytes.Length;
        _totalRecordCount++;
    }

    public void Flush()
    {
        if (_buffer.Count > 0)
        {
            FlushChunk();
        }
    }

    private void FlushChunk()
    {
        var sb = new StringBuilder();
        foreach (var item in _buffer)
        {
            sb.AppendLine(JsonSerializer.Serialize(item, JsonOptions));
        }

        byte[] chunkBytes = Encoding.UTF8.GetBytes(sb.ToString());
        string partPath = $"{_relativePath}.part-{_partIndex++:D6}.jsonl";

        _publisher.PublishArtifact(partPath, chunkBytes, _buffer.Count);

        _buffer.Clear();
        _currentBufferBytes = 0;
    }

    public void Dispose()
    {
        Flush();
    }
}