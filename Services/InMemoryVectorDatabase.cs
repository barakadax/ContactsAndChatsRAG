using System.Collections.Concurrent;
using System.Numerics;

namespace ContactsRag;

public sealed class InMemoryVectorDatabase : IVectorDatabase
{
    private readonly ConcurrentDictionary<string, VectorDocument> _records = new(StringComparer.Ordinal);

    public Task UpsertAsync(IEnumerable<VectorDocument> documents, CancellationToken ct = default)
    {
        foreach (var doc in documents)
        {
            ct.ThrowIfCancellationRequested();
            _records[doc.Id] = doc;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        ReadOnlyMemory<float> query,
        int topK,
        IReadOnlyDictionary<string, string>? metadataFilter = null,
        CancellationToken ct = default)
    {
        var querySpan = query.Span;
        var results = new List<VectorSearchResult>(topK);

        foreach (var doc in _records.Values)
        {
            ct.ThrowIfCancellationRequested();

            if (!MatchesFilter(doc.Metadata, metadataFilter))
            {
                continue;
            }

            var score = CosineSimilaritySimd(querySpan, doc.Embedding.Span);
            results.Add(new VectorSearchResult(doc.Id, score, doc.Text, doc.Metadata));
        }

        results.Sort(static (x, y) => y.Score.CompareTo(x.Score));

        if (results.Count > topK)
        {
            results.RemoveRange(topK, results.Count - topK);
        }

        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
    }

    private static bool MatchesFilter(
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyDictionary<string, string>? filter)
    {
        if (filter is null || filter.Count == 0)
            return true;

        return filter.All(kvp =>
            metadata.TryGetValue(kvp.Key, out var value)
            && (value.Contains('|')
                ? value.Split('|').Any(segment => segment.Contains(kvp.Value, StringComparison.OrdinalIgnoreCase))
                : value.Contains(kvp.Value, StringComparison.OrdinalIgnoreCase)));
    }

    private static double CosineSimilaritySimd(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var len = Math.Min(a.Length, b.Length);
        if (len == 0) return 0;

        var simdWidth = Vector<float>.Count;
        var dot = 0d;
        var na = 0d;
        var nb = 0d;
        var i = 0;

        if (len >= simdWidth)
        {
            var vDot = Vector<float>.Zero;
            var vNa = Vector<float>.Zero;
            var vNb = Vector<float>.Zero;

            for (; i <= len - simdWidth; i += simdWidth)
            {
                var va = new Vector<float>(a.Slice(i, simdWidth));
                var vb = new Vector<float>(b.Slice(i, simdWidth));
                vDot += va * vb;
                vNa += va * va;
                vNb += vb * vb;
            }

            dot = Vector.Sum(vDot);
            na = Vector.Sum(vNa);
            nb = Vector.Sum(vNb);
        }

        for (; i < len; i++)
        {
            var av = a[i];
            var bv = b[i];
            dot += av * bv;
            na += av * av;
            nb += bv * bv;
        }

        var denom = Math.Sqrt(na) * Math.Sqrt(nb);
        return denom <= 0 ? 0 : dot / denom;
    }
}
