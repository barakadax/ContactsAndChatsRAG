using System.ClientModel;
using System.Globalization;
using Polly;
using Polly.Retry;
using Spectre.Console;

namespace ContactsRag;

public static class OpenAiResiliencePipeline
{
    private const int MaxRetries = 4;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);
    private static readonly HashSet<int> RetryableStatusCodes = [429, 500, 502, 503, 504];

    public static ResiliencePipeline Create()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetries,
                Delay = BaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<ClientResultException>(ex => RetryableStatusCodes.Contains(ex.Status))
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException),
                OnRetry = static args =>
                {
                    var status = ExtractStatusCode(args.Outcome.Exception);
                    AnsiConsole.MarkupLine(
                        $"[yellow]\u26a1 OpenAI {status} \u2014 retry {args.AttemptNumber + 1}/{MaxRetries} in {args.RetryDelay.TotalSeconds:F1}s[/]");
                    return default;
                }
            })
            .Build();
    }

    private static string ExtractStatusCode(Exception? exception) =>
        exception is ClientResultException cre
            ? cre.Status.ToString(CultureInfo.InvariantCulture)
            : "unknown";
}
