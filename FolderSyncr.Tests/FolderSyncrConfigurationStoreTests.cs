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
                IgnoreDaylightSavingTimeShift: true,
                VerifyCopiedFiles: true,
                DeletionHandling.RecycleBin,
                VersioningMode.FileTime,
                VersioningFolderPath: @"E:\Versions",
                ErrorHandling: SyncErrorHandling.IgnoreErrors,
                SymbolicLinkHandling: SymbolicLinkHandling.CopyLinksAsLinks,
                IncludePatterns: "*.txt|*.md",
                ExcludePatterns: @"\bin\",
                IsDarkMode: true,
                ExternalCommands:
                [
                    new ExternalCommandDefinition("Compare", "winmerge %local_path% %local_path2%")
                ],
                FolderPairs:
                [
                    new FolderPairConfiguration(@"C:\Source", @"D:\Target", "*.txt", "cache\\"),
                    new FolderPairConfiguration(@"E:\More", @"F:\MoreBackup", "*.jpg", "thumbs.db")
                ],
                CustomRules: new CustomSyncRules(
                    CustomSyncAction.DoNothing,
                    CustomSyncAction.DeleteRight,
                    CustomSyncAction.CopyLeftToRight,
                    CustomSyncAction.CopyRightToLeft,
                    CustomSyncAction.DeleteLeft),
                RemoteConnectionCount: 4,
                SftpCompression: true,
                UseVolumeShadowCopy: true);

            var store = new FolderSyncrConfigurationStore();
            store.Save(path, original);

            var loaded = store.Load(path);

            Assert.AreEqual(original with { ExternalCommands = loaded.ExternalCommands, FolderPairs = loaded.FolderPairs }, loaded);
            CollectionAssert.AreEqual(
                original.ExternalCommands!.ToArray(),
                loaded.ExternalCommands!.ToArray());
            CollectionAssert.AreEqual(
                original.FolderPairs!.ToArray(),
                loaded.FolderPairs!.ToArray());
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
