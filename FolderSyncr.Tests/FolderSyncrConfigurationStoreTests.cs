using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class FolderSyncrConfigurationStoreTests
{
    [TestMethod]
    public void SaveAndLoadRoundTripsConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), "FolderSyncrConfigStore_" + Guid.NewGuid().ToString("N"), "backup.foldersyncr.json");
        try
        {
            var original = new FolderSyncrConfiguration(
                Version: 1,
                Name: "Backup",
                LeftPath: @"C:\Source",
                RightPath: @"D:\Target",
                SyncMode.MirrorLeftToRight,
                CompareMethod.ContentHash,
                FileTimeToleranceSeconds: 7,
                VerifyCopiedFiles: true,
                IncludePatterns: "*.txt|*.md",
                ExcludePatterns: @"\bin\",
                IsDarkMode: true);

            var store = new FolderSyncrConfigurationStore();
            store.Save(path, original);

            var loaded = store.Load(path);

            Assert.AreEqual(original, loaded);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
