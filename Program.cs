using ContactsRag;

DotNetEnv.Env.TraversePath().Load();

using var host = AppHostBuilder.Build(args);

if (args.Contains("--test"))
    await ValidationTest.RunAsync(host.Services);
else
    await CliRunner.RunAsync(host.Services);
