using System.Text.RegularExpressions;

namespace FileSyncr.Services;

public sealed class FileFilter
{
    private readonly Regex[] _includes;
    private readonly Regex[] _excludes;

    public FileFilter(string includePatterns, string excludePatterns)
    {
        _includes = BuildRegexes(string.IsNullOrWhiteSpace(includePatterns) ? "*" : includePatterns);
        _excludes = BuildRegexes(excludePatterns);
    }

    public bool IsMatch(string relativePath)
    {
        var normalizedPath = Normalize(relativePath);
        return _includes.Any(pattern => pattern.IsMatch(normalizedPath))
            && !_excludes.Any(pattern => pattern.IsMatch(normalizedPath));
    }

    private static Regex[] BuildRegexes(string patterns)
    {
        return patterns
            .Split([';', ',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ToRegex)
            .ToArray();
    }

    private static Regex ToRegex(string pattern)
    {
        var normalized = Normalize(pattern);
        var matchesPath = normalized.Contains('/');
        var escaped = Regex.Escape(normalized)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        var expression = matchesPath ? $"^{escaped}$" : $"(^|.*/){escaped}$";
        return new Regex(expression, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}
