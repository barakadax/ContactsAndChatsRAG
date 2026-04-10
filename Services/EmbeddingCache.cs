using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContactsRag;

public sealed class EmbeddingCache : IEmbeddingCache
{
    private readonly string _cacheDir;
    private readonly IFileSystem _fileSystem;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        Converters = { new FloatMemoryConverter() }
    };

    public EmbeddingCache(IFileSystem? fileSystem = null, string? cacheDirectory = null)
    {
        _fileSystem = fileSystem ?? new FileSystem();
        _cacheDir = cacheDirectory
            ?? Path.Combine(ContentRootResolver.Resolve(Directory.GetCurrentDirectory(), _fileSystem), ".embedding_cache");
        _fileSystem.Directory.CreateDirectory(_cacheDir);
    }

    public string ComputeHash(string filePath)
    {
        var fileName = _fileSystem.Path.GetFileName(filePath);
        var content = _fileSystem.File.ReadAllText(filePath, Encoding.UTF8);
        return ComputeHashFromContent(fileName, content);
    }

    public string ComputeHashFromContent(string logicalName, string content)
    {
        var input = logicalName + "\n" + content;
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes);
    }

    public async Task<IReadOnlyList<VectorDocument>?> TryLoadAsync(string hash, CancellationToken ct)
    {
        var path = _fileSystem.Path.Combine(_cacheDir, hash + ".json");
        if (!_fileSystem.File.Exists(path))
            return null;

        await using var fs = _fileSystem.File.OpenRead(path);
        var entries = await JsonSerializer.DeserializeAsync<List<CacheEntry>>(fs, SerializerOptions, ct);
        if (entries is null)
            return null;

        return [.. entries.Select(e => new VectorDocument(
            e.Id,
            e.Embedding,
            e.Text,
            new Dictionary<string, string>(e.Metadata, StringComparer.OrdinalIgnoreCase)
        ))];
    }

    public async Task SaveAsync(string hash, IReadOnlyList<VectorDocument> documents, CancellationToken ct)
    {
        var entries = documents.Select(d => new CacheEntry
        {
            Id = d.Id,
            Embedding = d.Embedding,
            Text = d.Text,
            Metadata = d.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
        }).ToList();

        var tempPath = _fileSystem.Path.Combine(_cacheDir, hash + ".tmp");
        var finalPath = _fileSystem.Path.Combine(_cacheDir, hash + ".json");

        await using (var stream = _fileSystem.File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, entries, SerializerOptions, ct);
        }

        _fileSystem.File.Move(tempPath, finalPath, overwrite: true);
    }

    private sealed class CacheEntry
    {
        public string Id { get; set; } = "";
        public ReadOnlyMemory<float> Embedding { get; set; }
        public string Text { get; set; } = "";
        public Dictionary<string, string> Metadata { get; set; } = [];
    }

    private sealed class FloatMemoryConverter : JsonConverter<ReadOnlyMemory<float>>
    {
        public override ReadOnlyMemory<float> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var base64 = reader.GetString();
            if (string.IsNullOrEmpty(base64))
                return ReadOnlyMemory<float>.Empty;

            var bytes = Convert.FromBase64String(base64);
            var floats = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            return floats;
        }

        public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<float> value, JsonSerializerOptions options)
        {
            var bytes = MemoryMarshal.AsBytes(value.Span).ToArray();
            writer.WriteBase64StringValue(bytes);
        }
    }
}
