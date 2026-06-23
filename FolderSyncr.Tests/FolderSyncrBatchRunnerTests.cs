using System.Text.Json;
using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class FolderSyncrBatchRunnerTests
{
    [TestMethod]
    public async Task RunAsyncSynchronizesNativeConfigurationAndWritesJson()
    {
        using var workspace = BatchWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.LeftPath, "copy.txt"), "batch copy");
        var configPath = workspace.SaveNativeConfiguration("job.foldersyncr.json", SyncMode.MirrorLeftToRight);
        var jsonPath = Path.Combine(workspace.RootPath, "result.json");

        var report = await workspace.CreateRunner().RunAsync(
            new BatchRunOptions(configPath, null, null, DryRun: false, jsonPath),
            null,
            CancellationToken.None);

        Assert.AreEqual(0, report.ExitCode);
        Assert.AreEqual("success", report.Result.SyncResult);
        Assert.AreEqual("batch copy", File.ReadAllText(Path.Combine(workspace.RightPath, "copy.txt")));
        Assert.IsTrue(File.Exists(jsonPath));

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.AreEqual("success", document.RootElement.GetProperty("syncResult").GetString());
        Assert.AreEqual(1, document.RootElement.GetProperty("processedItems").GetInt64());
    }

    [TestMethod]
    public async Task RunAsyncReturnsWarningsForImportedConfigurationWarnings()
    {
        using var workspace = BatchWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.LeftPath, "copy.txt"), "warning copy");
        var configPath = workspace.SaveFreeFileSyncConfigurationWithTwoPairs();

        var report = await workspace.CreateRunner().RunAsync(
            new BatchRunOptions(configPath, null, null, DryRun: true, JsonOutputPath: null),
            null,
            CancellationToken.None);

        Assert.AreEqual(1, report.ExitCode);
        Assert.AreEqual("dry-run", report.Result.SyncResult);
        Assert.AreEqual(1, report.Result.Warnings);
        Assert.AreEqual(1, report.Result.ProcessedItems);
        Assert.IsFalse(File.Exists(Path.Combine(workspace.RightPath, "copy.txt")));
    }

    [TestMethod]
    public async Task RunAsyncReturnsErrorForMissingConfiguration()
    {
        using var workspace = BatchWorkspace.Create();

        var report = await workspace.CreateRunner().RunAsync(
            new BatchRunOptions(Path.Combine(workspace.RootPath, "missing.foldersyncr.json"), null, null, DryRun: false, JsonOutputPath: null),
            null,
            CancellationToken.None);

        Assert.AreEqual(2, report.ExitCode);
        Assert.AreEqual("error", report.Result.SyncResult);
        Assert.AreEqual(1, report.Result.Errors);
    }

    [TestMethod]
    public async Task RunAsyncReportsIgnoredOperationErrors()
    {
        using var workspace = BatchWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.LeftPath, "locked.txt"), "locked");
        File.WriteAllText(Path.Combine(workspace.LeftPath, "ok.txt"), "ok");
        var configPath = workspace.SaveNativeConfiguration("ignore-errors.foldersyncr.json", SyncMode.MirrorLeftToRight);

        await using var lockStream = File.Open(Path.Combine(workspace.LeftPath, "locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var report = await workspace.CreateRunner().RunAsync(
            new BatchRunOptions(configPath, null, null, DryRun: false, JsonOutputPath: null, SyncErrorHandling.IgnoreErrors),
            null,
            CancellationToken.None);

        Assert.AreEqual(2, report.ExitCode);
        Assert.AreEqual("error", report.Result.SyncResult);
        Assert.AreEqual(1, report.Result.Errors);
        Assert.AreEqual(1, report.Result.ProcessedItems);
        Assert.AreEqual("ok", File.ReadAllText(Path.Combine(workspace.RightPath, "ok.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(workspace.RightPath, "locked.txt")));
    }

    [TestMethod]
    public void BatchRunOptionsCanOverrideErrorHandling()
    {
        var options = new BatchRunOptions("backup.foldersyncr.json", null, null, DryRun: false, JsonOutputPath: null, SyncErrorHandling.CancelOnFirstError);

        Assert.AreEqual(SyncErrorHandling.CancelOnFirstError, options.ErrorHandling);
    }

    private sealed class BatchWorkspace : IDisposable
    {
        private BatchWorkspace(string rootPath)
        {
            RootPath = rootPath;
            LeftPath = Path.Combine(rootPath, "left");
            RightPath = Path.Combine(rootPath, "right");
            Directory.CreateDirectory(LeftPath);
            Directory.CreateDirectory(RightPath);
        }

        public string RootPath { get; }
        public string LeftPath { get; }
        public string RightPath { get; }

        public static BatchWorkspace Create()
        {
            return new BatchWorkspace(Path.Combine(Path.GetTempPath(), "FolderSyncrBatchTests_" + Guid.NewGuid().ToString("N")));
        }

        public FolderSyncrBatchRunner CreateRunner()
        {
            return new FolderSyncrBatchRunner(runHistoryStore: new SyncRunHistoryStore(Path.Combine(RootPath, "history")));
        }

        public string SaveNativeConfiguration(string fileName, SyncMode mode)
        {
            var path = Path.Combine(RootPath, fileName);
            new FolderSyncrConfigurationStore().Save(path, new FolderSyncrConfiguration(
                Version: 1,
                Name: "Batch test",
                LeftPath,
                RightPath,
                mode,
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
            return path;
        }

        public string SaveFreeFileSyncConfigurationWithTwoPairs()
        {
            var path = Path.Combine(RootPath, "warning.ffs_batch");
            File.WriteAllText(path,
                $$"""
                  <FreeFileSync XmlType="BATCH">
                    <FolderPairs>
                      <Pair>
                        <Left>{{LeftPath}}</Left>
                        <Right>{{RightPath}}</Right>
                      </Pair>
                      <Pair>
                        <Left>C:\OtherLeft</Left>
                        <Right>C:\OtherRight</Right>
                      </Pair>
                    </FolderPairs>
                  </FreeFileSync>
                  """);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
