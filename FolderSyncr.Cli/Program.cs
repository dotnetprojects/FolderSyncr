using FolderSyncr.Services;

var parseResult = BatchCommandLineParser.Parse(args);
if (parseResult.ShowHelp)
{
    Console.WriteLine(BatchCommandLineParser.GetUsage());
    return 0;
}

if (parseResult.ErrorMessage is not null)
{
    Console.Error.WriteLine(parseResult.ErrorMessage);
    Console.Error.WriteLine(BatchCommandLineParser.GetUsage());
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var progress = new Progress<string>(message => Console.Error.WriteLine(message));
var report = await new FolderSyncrBatchRunner().RunAsync(parseResult.Options!, progress, cancellation.Token);
Console.WriteLine(SyncRunJsonWriter.Serialize(report.Result));
return report.ExitCode;

internal static class BatchCommandLineParser
{
    public static BatchParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.Any(arg => arg is "-h" or "--help" or "/?"))
        {
            return new BatchParseResult(null, null, ShowHelp: true);
        }

        string? configurationPath = null;
        string? left = null;
        string? right = null;
        string? jsonOutputPath = null;
        var dryRun = false;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            switch (argument.ToLowerInvariant())
            {
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--json":
                    if (index + 1 >= args.Count)
                    {
                        return new BatchParseResult(null, "--json requires an output path.", ShowHelp: false);
                    }

                    jsonOutputPath = args[++index];
                    break;
                case "-dirpair":
                    if (index + 2 >= args.Count)
                    {
                        return new BatchParseResult(null, "-dirpair requires a left and right folder.", ShowHelp: false);
                    }

                    left = args[++index];
                    right = args[++index];
                    break;
                default:
                    if (argument.StartsWith('-'))
                    {
                        return new BatchParseResult(null, $"Unknown option: {argument}", ShowHelp: false);
                    }

                    if (configurationPath is not null)
                    {
                        return new BatchParseResult(null, $"Unexpected extra argument: {argument}", ShowHelp: false);
                    }

                    configurationPath = argument;
                    break;
            }
        }

        return string.IsNullOrWhiteSpace(configurationPath)
            ? new BatchParseResult(null, "Pass a configuration path.", ShowHelp: false)
            : new BatchParseResult(new BatchRunOptions(configurationPath, left, right, dryRun, jsonOutputPath), null, ShowHelp: false);
    }

    public static string GetUsage()
    {
        return """
               Usage:
                 FolderSyncr.Cli <configuration> [--dry-run] [--json <path>] [-dirpair <left> <right>]

               Exit codes:
                 0 success
                 1 warnings
                 2 errors
                 3 cancelled
               """;
    }
}

internal sealed record BatchParseResult(BatchRunOptions? Options, string? ErrorMessage, bool ShowHelp);
