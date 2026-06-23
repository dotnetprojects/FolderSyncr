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
    public async Task RunAsyncSynchronizesEveryNativeFolderPair()
    {
        using var workspace = BatchWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.LeftPath, "copy.txt"), "native copy");
        File.WriteAllText(Path.Combine(workspace.SecondLeftPath, "second.txt"), "native second");
        var configPath = workspace.SaveNativeConfiguration("multi.foldersyncr.json", SyncMode.MirrorLeftToRight, includeSecondPair: true);

        var report = await workspace.CreateRunner().RunAsync(
            new BatchRunOptions(configPath, null, null, DryRun: false, JsonOutputPath: null),
            null,
            CancellationToken.None);

        Assert.AreEqual(0, report.ExitCode);
        Assert.AreEqual(2, report.Result.ProcessedItems);
        Assert.AreEqual("native copy", File.ReadAllText(Path.Combine(workspace.RightPath, "copy.txt")));
        Assert.AreEqual("native second", File.ReadAllText(Path.Combine(workspace.SecondRightPath, "second.txt")));
    }

    [TestMethod]
    public async Task RunAsyncMergesMultipleConfigurationFiles()
    {
        using var workspace = BatchWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.LeftPath, "first.txt"), "first merge");
        File.WriteAllText(Path.Combine(workspace.SecondLeftPath, "second.txt"), "second merge");
        var firstConfig = workspace.SaveNativeConfiguration("first.foldersyncr.json", SyncMode.MirrorLeftToRight);
        var secondConfig = workspace.SaveNativeConfiguration(
            "second.foldersyncr.json",
            SyncMode.MirrorLeftToRight,
            leftPath: workspace.SecondLeftPath,
            rightPath: workspace.SecondRightPath);

        var report = await workspace.CreateRunner().RunAsync(
            new BatchRunOptions([firstConfig, secondConfig], null, null, DryRun: false, JsonOutputPath: null),
            null,
            CancellationToken.None);

        Assert.AreEqual(0, report.ExitCode);
        Assert.AreEqual(2, report.Result.ProcessedItems);
        Assert.AreEqual("first merge", File.ReadAllText(Path.Combine(workspace.RightPath, "first.txt")));
        Assert.AreEqual("second merge", File.ReadAllText(Path.Combine(workspace.SecondRightPath, "second.txt")));
    }

    [TestMethod]
    public async Task RunAsyncAppliesAlternateGlobalSettingsFile()
    {
        using var workspace = BatchWorkspace.Create();
        var leftPath = Path.Combine(workspace.LeftPath, "same-size.txt");
        var rightPath = Path.Combine(workspace.RightPath, "same-size.txt");
        File.WriteAllText(leftPath, "same");
        File.WriteAllText(rightPath, "same");
        File.SetLastWriteTimeUtc(leftPath, new DateTime(2026, 1, 1, 12, 0, 10, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(rightPath, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var configPath = workspace.SaveNativeConfiguration("tolerance.foldersyncr.json", SyncMode.MirrorLeftToRight);
        var globalSettingsPath = workspace.SaveGlobalSettings("GlobalSettings.xml", fileTimeToleranceSeconds: 30, verifyCopiedFiles: true);

        var report = await workspace.CreateRunner().RunAsync(
            new BatchRunOptions([configPath, globalSettingsPath], null, null, DryRun: true, JsonOutputPath: null),
            null,
            CancellationToken.None);

        Assert.AreEqual(0, report.ExitCode);
        Assert.AreEqual("dry-run", report.Result.SyncResult);
        Assert.AreEqual(0, report.Result.ProcessedItems);
    }

    [TestMethod]
    public async Task RunAsyncAppliesTemporaryVariablesToFolderPaths()
    {
        using var workspace = BatchWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.LeftPath, "temp-var.txt"), "temporary variable copy");
        var original = Environment.GetEnvironmentVariable("FOLDERSYNCR_TEMP_LEFT");
        var configPath = workspace.SaveNativeConfiguration(
            "variables.foldersyncr.json",
            SyncMode.MirrorLeftToRight,
            leftPath: @"%FOLDERSYNCR_TEMP_LEFT%",
            rightPath: @"%FOLDERSYNCR_TEMP_RIGHT%");

        try
        {
            Environment.SetEnvironmentVariable("FOLDERSYNCR_TEMP_LEFT", "should be restored");

            var report = await workspace.CreateRunner().RunAsync(
                new BatchRunOptions(
                    configPath,
                    null,
                    null,
                    DryRun: false,
                    JsonOutputPath: null,
                    TemporaryVariables: new Dictionary<string, string>
                    {
                        ["FOLDERSYNCR_TEMP_LEFT"] = workspace.LeftPath,
                        ["FOLDERSYNCR_TEMP_RIGHT"] = workspace.RightPath
                    }),
                null,
                CancellationToken.None);

            Assert.AreEqual(0, report.ExitCode);
            Assert.AreEqual("temporary variable copy", File.ReadAllText(Path.Combine(workspace.RightPath, "temp-var.txt")));
            Assert.AreEqual("should be restored", Environment.GetEnvironmentVariable("FOLDERSYNCR_TEMP_LEFT"));
            Assert.IsNull(Environment.GetEnvironmentVariable("FOLDERSYNCR_TEMP_RIGHT"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOLDERSYNCR_TEMP_LEFT", original);
            Environment.SetEnvironmentVariable("FOLDERSYNCR_TEMP_RIGHT", null);
        }
    }

    [TestMethod]
    public async Task RunAsyncSynchronizesEveryImportedFreeFileSyncFolderPair()
    {
        using var workspace = BatchWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.LeftPath, "copy.txt"), "warning copy");
        File.WriteAllText(Path.Combine(workspace.SecondLeftPath, "second.txt"), "second copy");
        var configPath = workspace.SaveFreeFileSyncConfigurationWithTwoPairs();

        var report = await workspace.CreateRunner().RunAsync(
            new BatchRunOptions(configPath, null, null, DryRun: false, JsonOutputPath: null),
            null,
            CancellationToken.None);

        Assert.AreEqual(0, report.ExitCode);
        Assert.AreEqual("success", report.Result.SyncResult);
        Assert.AreEqual(0, report.Result.Warnings);
        Assert.AreEqual(2, report.Result.ProcessedItems);
        Assert.AreEqual("warning copy", File.ReadAllText(Path.Combine(workspace.RightPath, "copy.txt")));
        Assert.AreEqual("second copy", File.ReadAllText(Path.Combine(workspace.SecondRightPath, "second.txt")));
    }

    [TestMethod]
    public async Task RunAsyncUsesLocalFiltersForImportedFolderPairs()
    {
        using var workspace = BatchWorkspace.Create();
        File.WriteAllText(Path.Combine(workspace.LeftPath, "copy.txt"), "copy text");
        File.WriteAllText(Path.Combine(workspace.LeftPath, "skip.jpg"), "skip photo");
        File.WriteAllText(Path.Combine(workspace.SecondLeftPath, "copy.jpg"), "copy photo");
        File.WriteAllText(Path.Combine(workspace.SecondLeftPath, "skip.txt"), "skip text");
        var configPath = workspace.SaveFreeFileSyncConfigurationWithPairFilters();

        var report = await workspace.CreateRunner().RunAsync(
            new BatchRunOptions(configPath, null, null, DryRun: false, JsonOutputPath: null),
            null,
            CancellationToken.None);

        Assert.AreEqual(0, report.ExitCode);
        Assert.AreEqual(2, report.Result.ProcessedItems);
        Assert.AreEqual("copy text", File.ReadAllText(Path.Combine(workspace.RightPath, "copy.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(workspace.RightPath, "skip.jpg")));
        Assert.AreEqual("copy photo", File.ReadAllText(Path.Combine(workspace.SecondRightPath, "copy.jpg")));
        Assert.IsFalse(File.Exists(Path.Combine(workspace.SecondRightPath, "skip.txt")));
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

    [TestMethod]
    public void LoadSyncOptionsAppliesRemoteConnectionOverrides()
    {
        using var workspace = BatchWorkspace.Create();
        var configPath = workspace.SaveNativeConfiguration("remote-options.foldersyncr.json", SyncMode.TwoWay);

        var options = workspace.CreateRunner().LoadSyncOptions(
            new BatchRunOptions(
                configPath,
                null,
                null,
                DryRun: true,
                JsonOutputPath: null,
                RemoteConnectionCount: 5,
                SftpCompression: true),
            out _);

        Assert.HasCount(1, options);
        Assert.AreEqual(5, options[0].RemoteConnectionCount);
        Assert.IsTrue(options[0].SftpCompression);
    }

    private sealed class BatchWorkspace : IDisposable
    {
        private BatchWorkspace(string rootPath)
        {
            RootPath = rootPath;
            LeftPath = Path.Combine(rootPath, "left");
            RightPath = Path.Combine(rootPath, "right");
            SecondLeftPath = Path.Combine(rootPath, "left-2");
            SecondRightPath = Path.Combine(rootPath, "right-2");
            Directory.CreateDirectory(LeftPath);
            Directory.CreateDirectory(RightPath);
            Directory.CreateDirectory(SecondLeftPath);
            Directory.CreateDirectory(SecondRightPath);
        }

        public string RootPath { get; }
        public string LeftPath { get; }
        public string RightPath { get; }
        public string SecondLeftPath { get; }
        public string SecondRightPath { get; }

        public static BatchWorkspace Create()
        {
            return new BatchWorkspace(Path.Combine(Path.GetTempPath(), "FolderSyncrBatchTests_" + Guid.NewGuid().ToString("N")));
        }

        public FolderSyncrBatchRunner CreateRunner()
        {
            return new FolderSyncrBatchRunner(runHistoryStore: new SyncRunHistoryStore(Path.Combine(RootPath, "history")));
        }

        public string SaveNativeConfiguration(
            string fileName,
            SyncMode mode,
            bool includeSecondPair = false,
            string? leftPath = null,
            string? rightPath = null)
        {
            var path = Path.Combine(RootPath, fileName);
            leftPath ??= LeftPath;
            rightPath ??= RightPath;
            new FolderSyncrConfigurationStore().Save(path, new FolderSyncrConfiguration(
                Version: includeSecondPair ? 10 : 1,
                Name: "Batch test",
                leftPath,
                rightPath,
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
                IsDarkMode: false,
                FolderPairs: includeSecondPair
                    ?
                    [
                        new FolderPairConfiguration(LeftPath, RightPath),
                        new FolderPairConfiguration(SecondLeftPath, SecondRightPath)
                    ]
                    : null));
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
                        <Left>{{SecondLeftPath}}</Left>
                        <Right>{{SecondRightPath}}</Right>
                      </Pair>
                    </FolderPairs>
                  </FreeFileSync>
                  """);
            return path;
        }

        public string SaveFreeFileSyncConfigurationWithPairFilters()
        {
            var path = Path.Combine(RootPath, "pair-filters.ffs_batch");
            File.WriteAllText(path,
                $$"""
                  <FreeFileSync XmlType="BATCH">
                    <FolderPairs>
                      <Pair>
                        <Left>{{LeftPath}}</Left>
                        <Right>{{RightPath}}</Right>
                        <LocalFilter>
                          <Include><Item>*.txt</Item></Include>
                        </LocalFilter>
                      </Pair>
                      <Pair>
                        <Left>{{SecondLeftPath}}</Left>
                        <Right>{{SecondRightPath}}</Right>
                        <LocalFilter>
                          <Include><Item>*.jpg</Item></Include>
                        </LocalFilter>
                      </Pair>
                    </FolderPairs>
                  </FreeFileSync>
                  """);
            return path;
        }

        public string SaveGlobalSettings(string fileName, int fileTimeToleranceSeconds, bool verifyCopiedFiles)
        {
            var path = Path.Combine(RootPath, fileName);
            File.WriteAllText(path,
                $$"""
                  <FreeFileSync XmlType="GLOBAL">
                    <Shared>
                      <FileTimeTolerance Seconds="{{fileTimeToleranceSeconds}}" />
                      <VerifyCopiedFiles Enabled="{{verifyCopiedFiles.ToString().ToLowerInvariant()}}" />
                    </Shared>
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
