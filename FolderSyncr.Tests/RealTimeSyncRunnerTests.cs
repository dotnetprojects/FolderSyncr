using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class RealTimeSyncRunnerTests
{
    [TestMethod]
    public async Task RunOnceAfterNextChangeSynchronizesAfterIdleDelayAndExposesChangeVariables()
    {
        var root = Path.Combine(Path.GetTempPath(), "FolderSyncrRealTimeTests_" + Guid.NewGuid().ToString("N"));
        var left = Path.Combine(root, "left");
        var targetRoot = Path.Combine(root, "target");
        var createTarget = Path.Combine(targetRoot, "create");
        var updateTarget = Path.Combine(targetRoot, "update");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(createTarget);
        Directory.CreateDirectory(updateTarget);

        try
        {
            var configPath = Path.Combine(root, "watch.foldersyncr.json");
            new FolderSyncrConfigurationStore().Save(configPath, new FolderSyncrConfiguration(
                Version: 11,
                Name: "Realtime",
                LeftPath: left,
                RightPath: Path.Combine(targetRoot, "%change_action%"),
                SyncMode.MirrorLeftToRight,
                CompareMethod.TimeAndSize,
                FileTimeToleranceSeconds: 2,
                IgnoreDaylightSavingTimeShift: false,
                VerifyCopiedFiles: false,
                DeletionHandling.Permanent,
                VersioningMode.TimeStampFolder,
                VersioningFolderPath: string.Empty,
                ErrorHandling: SyncErrorHandling.ShowErrors,
                SymbolicLinkHandling: SymbolicLinkHandling.Skip,
                IncludePatterns: "*",
                ExcludePatterns: string.Empty,
                IsDarkMode: false));

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var runTask = new RealTimeSyncRunner(new FolderSyncrBatchRunner(
                runHistoryStore: new SyncRunHistoryStore(Path.Combine(root, "history"))))
                .RunOnceAfterNextChangeAsync(
                    new BatchRunOptions(
                        configPath,
                        null,
                        null,
                        DryRun: false,
                        JsonOutputPath: null,
                        WatchIdleDelay: TimeSpan.FromMilliseconds(200)),
                    null,
                    cancellation.Token);

            await Task.Delay(400, cancellation.Token);
            File.WriteAllText(Path.Combine(left, "changed.txt"), "realtime");

            var report = await runTask;

            Assert.AreEqual(0, report.ExitCode);
            Assert.IsTrue(
                File.Exists(Path.Combine(createTarget, "changed.txt"))
                || File.Exists(Path.Combine(updateTarget, "changed.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
