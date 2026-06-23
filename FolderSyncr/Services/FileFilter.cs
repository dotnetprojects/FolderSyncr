using System.Text.RegularExpressions;

namespace FolderSyncr.Services;

public sealed class FileFilter
{
    private readonly FilterRule[] _includes;
    private readonly FilterRule[] _excludes;

    public FileFilter(string includePatterns, string excludePatterns)
    {
        _includes = BuildRegexes(string.IsNullOrWhiteSpace(includePatterns) ? "*" : includePatterns);
        _excludes = BuildRegexes(excludePatterns);
    }

    public bool IsMatch(string relativePath)
    {
        var normalizedPath = Normalize(relativePath);
        return _includes.Any(pattern => pattern.IsMatch(normalizedPath, isDirectory: false))
            && !_excludes.Any(pattern => pattern.IsMatch(normalizedPath, isDirectory: false));
    }

    private static FilterRule[] BuildRegexes(string patterns)
    {
        return patterns
            .Split([';', ',', '|', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ToRule)
            .ToArray();
    }

    private static FilterRule ToRule(string pattern)
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
            return new FilterRule(
                CreateRegex(anchoredToRoot ? $"^{folderExpression}$" : $"(^|.*/){folderExpression}$"),
                FilterRuleKind.FolderOnly);
        }

        var matchesPath = normalized.Contains('/');
        var escaped = WildcardsToRegex(normalized, allowSlashInStar: matchesPath);

        var expression = anchoredToRoot ? $"^{escaped}$" : $"(^|.*/){escaped}$";
        return new FilterRule(CreateRegex(expression), fileOnly ? FilterRuleKind.FileOnly : FilterRuleKind.FileOrFolder);
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

    private static Regex CreateRegex(string expression)
    {
        return new Regex(expression, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Normalize(string value) => value.Replace('\\', '/');

    private enum FilterRuleKind
    {
        FileOrFolder,
        FileOnly,
        FolderOnly
    }

    private sealed class FilterRule(Regex regex, FilterRuleKind kind)
    {
        public bool IsMatch(string normalizedPath, bool isDirectory)
        {
            if (kind == FilterRuleKind.FileOnly)
            {
                return !isDirectory && regex.IsMatch(normalizedPath);
            }

            if (isDirectory)
            {
                return regex.IsMatch(normalizedPath);
            }

            return kind == FilterRuleKind.FileOrFolder && regex.IsMatch(normalizedPath)
                || ParentFolders(normalizedPath).Any(regex.IsMatch);
        }

        private static IEnumerable<string> ParentFolders(string normalizedPath)
        {
            var slashIndex = normalizedPath.LastIndexOf('/');
            while (slashIndex > 0)
            {
                var folder = normalizedPath[..slashIndex];
                yield return folder;
                slashIndex = folder.LastIndexOf('/');
            }
        }
    }
}
