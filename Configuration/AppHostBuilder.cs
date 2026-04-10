using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using OpenAI;
using Spectre.Console;

namespace ContactsRag;

internal static class AppHostBuilder
{
    internal static IHost Build(string[] args)
    {
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        const string defaultChatModel = "gpt-5.4";
        const string defaultEmbedModel = "text-embedding-3-large";
        var chatModel = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL") ?? defaultChatModel;
        var embedModel = Environment.GetEnvironmentVariable("OPENAI_EMBED_MODEL") ?? defaultEmbedModel;

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            AnsiConsole.MarkupLine("[red bold]ERROR:[/] OPENAI_API_KEY is required. Set it in the environment or a .env file.");
            Environment.Exit(1);
        }

        var fileSystem = new FileSystem();
        var contentRoot = ContentRootResolver.Resolve(Directory.GetCurrentDirectory(), fileSystem);

        var resiliencePipeline = OpenAiResiliencePipeline.Create();

        var openAiClient = new OpenAIClient(openAiKey);
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSingleton<IFileSystem>(fileSystem);
        builder.Services.AddSingleton(resiliencePipeline);
        builder.Services.AddOpenAIChatClient(chatModel, openAiKey);
        builder.Services.AddSingleton(openAiClient);
        builder.Services.AddSingleton<IEmbeddingGenerator>(new OpenAiEmbeddingAdapter(openAiClient, embedModel, resiliencePipeline));
        builder.Services.AddSingleton<IVectorDatabase, InMemoryVectorDatabase>();
        builder.Services.AddRagEngine(contentRoot, static _ => { });

        builder.Services.AddSingleton<IChatIngestionService>(sp =>
            new ChatIngestionService(
                sp.GetRequiredService<IEmbeddingGenerator>(),
                sp.GetRequiredService<IVectorDatabase>(),
                sp.GetRequiredService<IEmbeddingCache>(),
                sp.GetRequiredService<IFileSystem>(),
                contentRoot));

        builder.Services.AddSingleton<IChatDataGenerator>(sp =>
            new ChatDataGenerator(sp.GetRequiredService<IFileSystem>()));

        builder.Services.AddSingleton<Kernel>(_ =>
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAIChatCompletion(chatModel, openAiKey!);
            return kernelBuilder.Build();
        });

        return builder.Build();
    }
}
