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
}
