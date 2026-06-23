namespace FolderSyncr.Services;

public sealed class CommandLineStartupParser
{
    public CommandLineStartupOptions Parse(IEnumerable<string> args)
    {
        string? configurationPath = null;
        string? overrideLeftPath = null;
        string? overrideRightPath = null;
        var arguments = args.ToArray();

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (string.Equals(argument, "-dirpair", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "/dirpair", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 2 >= arguments.Length)
                {
                    throw new ArgumentException("The -dirpair option requires a left path and a right path.");
                }

                overrideLeftPath = arguments[++index];
                overrideRightPath = arguments[++index];
                continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal) || argument.StartsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            configurationPath ??= argument;
        }

        return new CommandLineStartupOptions(configurationPath, overrideLeftPath, overrideRightPath);
    }
}
