using System.Globalization;
using System.IO;
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
            if (TryExpandSpecialFolder(macro, out var specialFolderPath))
            {
                return specialFolderPath;
            }

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

    private static bool TryExpandSpecialFolder(string macro, out string path)
    {
        path = string.Empty;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        path = macro.ToUpperInvariant() switch
        {
            "CSIDL_DESKTOP" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "CSIDL_DOCUMENTS" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CSIDL_PICTURES" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "CSIDL_MUSIC" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "CSIDL_VIDEOS" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "CSIDL_DOWNLOADS" => Path.Combine(userProfile, "Downloads"),
            "CSIDL_FAVORITES" => Environment.GetFolderPath(Environment.SpecialFolder.Favorites),
            "CSIDL_RESOURCES" => Environment.GetFolderPath(Environment.SpecialFolder.Resources),
            "CSIDL_QUICKLAUNCH" => Path.Combine(appData, "Microsoft", "Internet Explorer", "Quick Launch"),
            "CSIDL_STARTMENU" => Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "CSIDL_PROGRAMS" => Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            "CSIDL_STARTUP" => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "CSIDL_NETHOOD" => Environment.GetFolderPath(Environment.SpecialFolder.NetworkShortcuts),
            "CSIDL_TEMPLATES" => Environment.GetFolderPath(Environment.SpecialFolder.Templates),
            "CSIDL_PUBLICDOCUMENTS" => Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            "CSIDL_PUBLICPICTURES" => Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures),
            "CSIDL_PUBLICMUSIC" => Environment.GetFolderPath(Environment.SpecialFolder.CommonMusic),
            "CSIDL_PUBLICVIDEOS" => Environment.GetFolderPath(Environment.SpecialFolder.CommonVideos),
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(path);
    }

    private static int GetWeekDay(DateTime now)
    {
        return now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek;
    }

    [GeneratedRegex("%(?<name>Date|Time|TimeStamp|Year|Month|MonthName|Day|Hour|Min|Sec|WeekDay|WeekDayName|Week|csidl_[A-Za-z]+)%", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MacroRegex();
}
