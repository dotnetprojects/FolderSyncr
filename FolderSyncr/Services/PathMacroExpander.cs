using System.Globalization;
using System.Text.RegularExpressions;

namespace FolderSyncr.Services;

public static partial class PathMacroExpander
{
    public static string Expand(string path)
    {
        return Expand(path, DateTime.Now);
    }

    public static string Expand(string path, DateTime now)
    {
        return string.IsNullOrWhiteSpace(path)
            ? path
            : Environment.ExpandEnvironmentVariables(ExpandInternalMacros(path, now));
    }

    private static string ExpandInternalMacros(string path, DateTime now)
    {
        return MacroRegex().Replace(path, match =>
        {
            var macro = match.Groups["name"].Value;
            return macro.ToUpperInvariant() switch
            {
                "DATE" => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "TIME" => now.ToString("HHmmss", CultureInfo.InvariantCulture),
                "TIMESTAMP" => now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture),
                "YEAR" => now.ToString("yyyy", CultureInfo.InvariantCulture),
                "MONTH" => now.ToString("MM", CultureInfo.InvariantCulture),
                "MONTHNAME" => now.ToString("MMM", CultureInfo.InvariantCulture),
                "DAY" => now.ToString("dd", CultureInfo.InvariantCulture),
                "HOUR" => now.ToString("HH", CultureInfo.InvariantCulture),
                "MIN" => now.ToString("mm", CultureInfo.InvariantCulture),
                "SEC" => now.ToString("ss", CultureInfo.InvariantCulture),
                "WEEKDAY" => GetWeekDay(now).ToString(CultureInfo.InvariantCulture),
                "WEEKDAYNAME" => now.ToString("ddd", CultureInfo.InvariantCulture),
                "WEEK" => ISOWeek.GetWeekOfYear(now).ToString("00", CultureInfo.InvariantCulture),
                _ => match.Value
            };
        });
    }

    private static int GetWeekDay(DateTime now)
    {
        return now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek;
    }

    [GeneratedRegex("%(?<name>Date|Time|TimeStamp|Year|Month|MonthName|Day|Hour|Min|Sec|WeekDay|WeekDayName|Week)%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MacroRegex();
}
