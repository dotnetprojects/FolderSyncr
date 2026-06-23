using FolderSyncr.Services;
using FolderSyncr.Models;

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

        var configurationPaths = new List<string>();
        string? left = null;
        string? right = null;
        string? jsonOutputPath = null;
        SyncErrorHandling? errorHandling = null;
        SymbolicLinkHandling? symbolicLinkHandling = null;
        var temporaryVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                case "--error-handling":
                    if (index + 1 >= args.Count)
                    {
                        return new BatchParseResult(null, "--error-handling requires show, ignore, or cancel.", ShowHelp: false);
                    }

                    if (!TryParseErrorHandling(args[++index], out var parsedErrorHandling))
                    {
                        return new BatchParseResult(null, "--error-handling requires show, ignore, or cancel.", ShowHelp: false);
                    }

                    errorHandling = parsedErrorHandling;
                    break;
                case "--symbolic-links":
                    if (index + 1 >= args.Count)
                    {
                        return new BatchParseResult(null, "--symbolic-links requires skip, follow, or copy.", ShowHelp: false);
                    }

                    if (!TryParseSymbolicLinkHandling(args[++index], out var parsedSymbolicLinkHandling))
                    {
                        return new BatchParseResult(null, "--symbolic-links requires skip, follow, or copy.", ShowHelp: false);
                    }

                    symbolicLinkHandling = parsedSymbolicLinkHandling;
                    break;
                case "--var":
                    if (index + 1 >= args.Count)
                    {
                        return new BatchParseResult(null, "--var requires NAME=VALUE.", ShowHelp: false);
                    }

                    if (!TryParseTemporaryVariable(args[++index], out var name, out var value))
                    {
                        return new BatchParseResult(null, "--var requires NAME=VALUE with a non-empty name.", ShowHelp: false);
                    }

                    temporaryVariables[name] = value;
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

                    configurationPaths.Add(argument);
                    break;
            }
        }

        return configurationPaths.Count == 0
            ? new BatchParseResult(null, "Pass a configuration path.", ShowHelp: false)
            : new BatchParseResult(new BatchRunOptions(configurationPaths, left, right, dryRun, jsonOutputPath, errorHandling, symbolicLinkHandling, temporaryVariables), null, ShowHelp: false);
    }

    private static bool TryParseTemporaryVariable(string argument, out string name, out string value)
    {
        var separatorIndex = argument.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            name = string.Empty;
            value = string.Empty;
            return false;
        }

        name = argument[..separatorIndex].Trim();
        value = argument[(separatorIndex + 1)..];
        return !string.IsNullOrWhiteSpace(name);
    }

    private static bool TryParseErrorHandling(string value, out SyncErrorHandling errorHandling)
    {
        errorHandling = value.ToLowerInvariant() switch
        {
            "show" or "showerrors" => SyncErrorHandling.ShowErrors,
            "ignore" or "ignoreerrors" => SyncErrorHandling.IgnoreErrors,
            "cancel" or "cancelonfirsterror" => SyncErrorHandling.CancelOnFirstError,
            _ => SyncErrorHandling.ShowErrors
        };

        return value.Equals("show", StringComparison.OrdinalIgnoreCase)
            || value.Equals("showerrors", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ignore", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ignoreerrors", StringComparison.OrdinalIgnoreCase)
            || value.Equals("cancel", StringComparison.OrdinalIgnoreCase)
            || value.Equals("cancelonfirsterror", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSymbolicLinkHandling(string value, out SymbolicLinkHandling symbolicLinkHandling)
    {
        symbolicLinkHandling = value.ToLowerInvariant() switch
        {
            "skip" => SymbolicLinkHandling.Skip,
            "follow" => SymbolicLinkHandling.Follow,
            "copy" or "copylinksaslinks" => SymbolicLinkHandling.CopyLinksAsLinks,
            _ => SymbolicLinkHandling.Skip
        };

        return value.Equals("skip", StringComparison.OrdinalIgnoreCase)
            || value.Equals("follow", StringComparison.OrdinalIgnoreCase)
            || value.Equals("copy", StringComparison.OrdinalIgnoreCase)
            || value.Equals("copylinksaslinks", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetUsage()
    {
        return """
               Usage:
                 FolderSyncr.Cli <configuration> [configuration ...] [--dry-run] [--json <path>] [--var NAME=VALUE] [--error-handling show|ignore|cancel] [--symbolic-links skip|follow|copy] [-dirpair <left> <right>]

               Additional positional paths may be FolderSyncr/FreeFileSync configurations or a FreeFileSync GlobalSettings.xml file.

               Exit codes:
                 0 success
                 1 warnings
                 2 errors
                 3 cancelled
               """;
    }
}

internal sealed record BatchParseResult(BatchRunOptions? Options, string? ErrorMessage, bool ShowHelp);
