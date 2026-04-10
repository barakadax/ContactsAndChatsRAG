using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Polly;

namespace ContactsRag;

public static class RagEngineExtensions
{
    public static IServiceCollection AddRagEngine(
        this IServiceCollection services,
        string contentRoot,
        Action<RagEngineOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IHyDeGenerator, HyDeGenerator>();
        services.AddSingleton<RagOrchestrator>();
        services.AddSingleton<IEmbeddingCache>(sp =>
            new EmbeddingCache(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<IRagOrchestrator>(sp =>
            new DomainRagOrchestrator(
                sp.GetRequiredService<RagOrchestrator>(),
                sp.GetRequiredService<IEmbeddingGenerator>(),
                sp.GetRequiredService<IVectorDatabase>(),
                sp.GetRequiredService<Kernel>(),
                sp.GetRequiredService<IOptions<RagEngineOptions>>(),
                sp.GetRequiredService<ResiliencePipeline>(),
                sp.GetRequiredService<IFileSystem>(),
                contentRoot));
        return services;
    }
}
