namespace ContactsRag;

public interface IChatIngestionService
{
    Task<(int Read, int Embedded, int Stored, int CacheHits)> IngestAsync(CancellationToken cancellationToken);
}
