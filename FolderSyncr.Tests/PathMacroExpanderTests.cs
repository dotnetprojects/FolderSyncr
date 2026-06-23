using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class PathMacroExpanderTests
{
    [TestMethod]
    public void ExpandResolvesEnvironmentVariables()
    {
        var original = Environment.GetEnvironmentVariable("FOLDERSYNCR_TEST_PATH");
        try
        {
            Environment.SetEnvironmentVariable("FOLDERSYNCR_TEST_PATH", @"C:\Data\FolderSyncr");

            var expanded = PathMacroExpander.Expand(@"%FOLDERSYNCR_TEST_PATH%\Backup");

            Assert.AreEqual(@"C:\Data\FolderSyncr\Backup", expanded);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOLDERSYNCR_TEST_PATH", original);
        }
    }

    [TestMethod]
    public void ExpandResolvesDateAndTimeMacros()
    {
        var now = new DateTime(2026, 6, 23, 12, 25, 42);

        var expanded = PathMacroExpander.Expand(@"C:\Backups\%Date%\%Time%\%TimeStamp%", now);

        Assert.AreEqual(@"C:\Backups\2026-06-23\122542\2026-06-23 122542", expanded);
    }

    [TestMethod]
    public void ExpandResolvesDatePartMacros()
    {
        var now = new DateTime(2026, 6, 23, 12, 25, 42);

        var expanded = PathMacroExpander.Expand(@"%Year%-%Month%-%MonthName%-%Day%-%Hour%-%Min%-%Sec%", now);

        Assert.AreEqual("2026-06-Jun-23-12-25-42", expanded);
    }

    [TestMethod]
    public void ExpandResolvesWeekMacros()
    {
        var now = new DateTime(2026, 6, 23, 12, 25, 42);

        var expanded = PathMacroExpander.Expand(@"%WeekDay%-%WeekDayName%-%Week%", now);

        Assert.AreEqual("2-Tue-26", expanded);
    }

    [TestMethod]
    public void ExpandResolvesSpecialFolderMacros()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Backup");

        var expanded = PathMacroExpander.Expand(@"%csidl_Desktop%\Backup");

        Assert.AreEqual(expected, expanded);
    }

    [TestMethod]
    public void ExpandResolvesPublicSpecialFolderMacros()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);

        var expanded = PathMacroExpander.Expand("%csidl_PublicDocuments%");

        Assert.AreEqual(expected, expanded);
    }

    [TestMethod]
    public void ExpandLeavesUnknownSpecialFolderMacrosUnchanged()
    {
        var expanded = PathMacroExpander.Expand("%csidl_NotARealFolder%");

        Assert.AreEqual("%csidl_NotARealFolder%", expanded);
    }

    [TestMethod]
    public void ExpandResolvesVolumeLabelPaths()
    {
        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backup-Disk"] = @"E:\"
        };

        var expanded = PathMacroExpander.Expand(@"[Backup-Disk]\folder\file.txt", DateTime.Now, roots);

        Assert.AreEqual(@"E:\folder\file.txt", expanded);
    }

    [TestMethod]
    public void ExpandLeavesUnknownVolumeLabelPathsUnchanged()
    {
        var expanded = PathMacroExpander.Expand(@"[Missing-Disk]\folder", DateTime.Now, new Dictionary<string, string>());

        Assert.AreEqual(@"[Missing-Disk]\folder", expanded);
        Assert.AreEqual("Missing-Disk", PathMacroExpander.GetVolumeLabelReference(expanded));
    }
}
