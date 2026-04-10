namespace ContactsRag;

public interface IChatDataGenerator
{
    Task<(int Read, int Structured)> GenerateAsync(string rootDir, CancellationToken cancellationToken);
}
