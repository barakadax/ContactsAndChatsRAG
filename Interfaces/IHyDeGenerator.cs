namespace ContactsRag;

public interface IHyDeGenerator
{
    Task<string> GenerateAsync(string query, HyDeOptions? options = null, CancellationToken ct = default);
}
