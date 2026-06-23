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
}
