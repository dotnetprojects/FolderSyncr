using System.Text.RegularExpressions;

namespace FolderSyncr.Services;

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
            .Split([';', ',', '|', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ToRegex)
            .ToArray();
    }

    private static Regex ToRegex(string pattern)
    {
        var normalized = Normalize(pattern).Trim();
        var folderOnly = normalized.EndsWith("/", StringComparison.Ordinal);
        var fileOnly = normalized.EndsWith(":", StringComparison.Ordinal);
        if (folderOnly || fileOnly)
        {
            normalized = normalized[..^1];
        }

        var anchoredToRoot = normalized.StartsWith("/", StringComparison.Ordinal);
        normalized = normalized.TrimStart('/');

        if (folderOnly)
        {
            var folderExpression = WildcardsToRegex(normalized, allowSlashInStar: true);
            return new Regex(
                anchoredToRoot
                    ? $"^{folderExpression}(/.*)?$"
                    : $"(^|.*/){folderExpression}(/.*)?$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        var matchesPath = normalized.Contains('/');
        var escaped = WildcardsToRegex(normalized, allowSlashInStar: matchesPath);

        var expression = anchoredToRoot || matchesPath ? $"^{escaped}$" : $"(^|.*/){escaped}$";
        return new Regex(expression, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string WildcardsToRegex(string pattern, bool allowSlashInStar)
    {
        var starExpression = allowSlashInStar ? ".*" : "[^/]*";
        return Regex.Escape(pattern)
            .Replace("\\?\\*", allowSlashInStar ? ".+" : "[^/]+", StringComparison.Ordinal)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", starExpression, StringComparison.Ordinal)
            .Replace("\\?", "[^/]", StringComparison.Ordinal);
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}
