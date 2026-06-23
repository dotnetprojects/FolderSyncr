using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class SyncEngineTests
{
    [TestMethod]
    public async Task MirrorLeftToRightCopiesAndDeletesFiles()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteLeft("keep.txt", "left version", DateTime.UtcNow);
        workspace.WriteLeft(Path.Combine("nested", "new.txt"), "new file", DateTime.UtcNow);
        workspace.WriteRight("keep.txt", "old right version", DateTime.UtcNow.AddMinutes(-10));
        workspace.WriteRight("delete.txt", "delete me", DateTime.UtcNow);

        var options = workspace.CreateOptions(SyncMode.MirrorLeftToRight);
        var engine = new SyncEngine();

        var operations = await engine.CompareAsync(options, null, CancellationToken.None);

        AssertOperation(operations, "keep.txt", OperationKind.CopyLeftToRight);
        AssertOperation(operations, Path.Combine("nested", "new.txt"), OperationKind.CopyLeftToRight);
        AssertOperation(operations, "delete.txt", OperationKind.DeleteRight);

        await engine.ExecuteAsync(operations, options, null, CancellationToken.None);

        Assert.IsFalse(File.Exists(workspace.RightPath("delete.txt")));
        Assert.AreEqual("left version", File.ReadAllText(workspace.RightPath("keep.txt")));
        Assert.AreEqual("new file", File.ReadAllText(workspace.RightPath(Path.Combine("nested", "new.txt"))));
    }

    [TestMethod]
    public async Task TwoWayCopiesNewestChangedFile()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteLeft("shared.txt", "old left", DateTime.UtcNow.AddMinutes(-20));
        workspace.WriteRight("shared.txt", "new right", DateTime.UtcNow);

        var options = workspace.CreateOptions(SyncMode.TwoWay);
        var engine = new SyncEngine();

        var operations = await engine.CompareAsync(options, null, CancellationToken.None);

        AssertOperation(operations, "shared.txt", OperationKind.CopyRightToLeft);

        await engine.ExecuteAsync(operations, options, null, CancellationToken.None);

        Assert.AreEqual("new right", File.ReadAllText(workspace.LeftPath("shared.txt")));
    }

    [TestMethod]
    public async Task TwoWayMarksChangedFilesAsConflictWhenTimestampDoesNotPickWinner()
    {
        using var workspace = TestWorkspace.Create();
        var timestamp = DateTime.UtcNow;
        workspace.WriteLeft("conflict.txt", "left", timestamp);
        workspace.WriteRight("conflict.txt", "right", timestamp);

        var options = workspace.CreateOptions(SyncMode.TwoWay);
        var engine = new SyncEngine();

        var operations = await engine.CompareAsync(options, null, CancellationToken.None);

        AssertOperation(operations, "conflict.txt", OperationKind.Conflict);
    }

    [TestMethod]
    public async Task SizeOnlyComparisonTreatsSameLengthFilesAsEqual()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteLeft("same-size.txt", "left", DateTime.UtcNow);
        workspace.WriteRight("same-size.txt", "rght", DateTime.UtcNow.AddDays(-2));

        var options = workspace.CreateOptions(SyncMode.TwoWay, CompareMethod.SizeOnly);
        var engine = new SyncEngine();

        var operations = await engine.CompareAsync(options, null, CancellationToken.None);

        AssertOperation(operations, "same-size.txt", OperationKind.Equal);
    }

    [TestMethod]
    public async Task SizeOnlyComparisonDetectsDifferentFileSizes()
    {
        using var workspace = TestWorkspace.Create();
        var timestamp = DateTime.UtcNow;
        workspace.WriteLeft("different-size.txt", "left", timestamp);
        workspace.WriteRight("different-size.txt", "right side", timestamp);

        var options = workspace.CreateOptions(SyncMode.TwoWay, CompareMethod.SizeOnly);
        var engine = new SyncEngine();

        var operations = await engine.CompareAsync(options, null, CancellationToken.None);

        AssertOperation(operations, "different-size.txt", OperationKind.Conflict);
    }

    [TestMethod]
    public async Task TimeAndSizeUsesConfiguredFileTimeTolerance()
    {
        using var workspace = TestWorkspace.Create();
        var timestamp = DateTime.UtcNow;
        workspace.WriteLeft("near.txt", "same", timestamp);
        workspace.WriteRight("near.txt", "same", timestamp.AddSeconds(-5));

        var options = workspace.CreateOptions(SyncMode.TwoWay, CompareMethod.TimeAndSize, fileTimeToleranceSeconds: 10);
        var engine = new SyncEngine();

        var operations = await engine.CompareAsync(options, null, CancellationToken.None);

        AssertOperation(operations, "near.txt", OperationKind.Equal);
    }

    [TestMethod]
    public async Task TimeAndSizeMarksDifferenceOutsideConfiguredTolerance()
    {
        using var workspace = TestWorkspace.Create();
        var timestamp = DateTime.UtcNow;
        workspace.WriteLeft("outside.txt", "same", timestamp);
        workspace.WriteRight("outside.txt", "same", timestamp.AddSeconds(-5));

        var options = workspace.CreateOptions(SyncMode.TwoWay, CompareMethod.TimeAndSize, fileTimeToleranceSeconds: 1);
        var engine = new SyncEngine();

        var operations = await engine.CompareAsync(options, null, CancellationToken.None);

        AssertOperation(operations, "outside.txt", OperationKind.CopyLeftToRight);
    }

    [TestMethod]
    public async Task CompareExpandsEnvironmentVariablesInFolderPaths()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteLeft("env.txt", "left", DateTime.UtcNow);

        var variableName = "FOLDERSYNCR_TEST_ROOT_" + Guid.NewGuid().ToString("N");
        var original = Environment.GetEnvironmentVariable(variableName);
        try
        {
            Environment.SetEnvironmentVariable(variableName, workspace.RootPath);
            var options = new SyncOptions
            {
                LeftPath = $"%{variableName}%\\left",
                RightPath = $"%{variableName}%\\right",
                Mode = SyncMode.TwoWay,
                CompareMethod = CompareMethod.TimeAndSize,
                IncludePatterns = "*",
                ExcludePatterns = string.Empty
            };

            var operations = await new SyncEngine().CompareAsync(options, null, CancellationToken.None);

            AssertOperation(operations, "env.txt", OperationKind.CopyLeftToRight);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, original);
        }
    }

    [TestMethod]
    public async Task ExecuteVerifiesCopiedFilesWhenEnabled()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteLeft("verify.txt", "verify me", DateTime.UtcNow);

        var options = workspace.CreateOptions(SyncMode.TwoWay, verifyCopiedFiles: true);
        var engine = new SyncEngine();
        var messages = new List<string>();
        var progress = new Progress<string>(messages.Add);

        var operations = await engine.CompareAsync(options, null, CancellationToken.None);
        await engine.ExecuteAsync(operations, options, progress, CancellationToken.None);

        Assert.AreEqual("verify me", File.ReadAllText(workspace.RightPath("verify.txt")));
        Assert.IsTrue(messages.Any(message => message.Contains("Verify verify.txt", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task VersioningDeletionMovesTargetFileToVersioningFolder()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteRight("obsolete.txt", "keep a version", DateTime.UtcNow);

        var versioningRoot = Path.Combine(workspace.RootPath, "versions");
        var options = workspace.CreateOptions(
            SyncMode.MirrorLeftToRight,
            deletionHandling: DeletionHandling.VersioningFolder,
            versioningFolderPath: versioningRoot);
        var engine = new SyncEngine();

        var operations = await engine.CompareAsync(options, null, CancellationToken.None);
        await engine.ExecuteAsync(operations, options, null, CancellationToken.None);

        Assert.IsFalse(File.Exists(workspace.RightPath("obsolete.txt")));
        var versionedFile = Directory.EnumerateFiles(versioningRoot, "obsolete.txt", SearchOption.AllDirectories).Single();
        Assert.AreEqual("keep a version", File.ReadAllText(versionedFile));
    }

    private static void AssertOperation(IEnumerable<SyncOperation> operations, string relativePath, OperationKind kind)
    {
        Assert.IsTrue(
            operations.Any(operation =>
                string.Equals(operation.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase)
                && operation.Kind == kind),
            $"Expected {relativePath} to have operation {kind}.");
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _root;
        private readonly string _left;
        private readonly string _right;

        private TestWorkspace(string root)
        {
            _root = root;
            _left = Path.Combine(root, "left");
            _right = Path.Combine(root, "right");
            Directory.CreateDirectory(_left);
            Directory.CreateDirectory(_right);
        }

        public static TestWorkspace Create()
        {
            return new TestWorkspace(Path.Combine(Path.GetTempPath(), "FolderSyncrTests_" + Guid.NewGuid().ToString("N")));
        }

        public string LeftPath(string relativePath) => Path.Combine(_left, relativePath);

        public string RightPath(string relativePath) => Path.Combine(_right, relativePath);

        public string RootPath => _root;

        public SyncOptions CreateOptions(
            SyncMode mode,
            CompareMethod compareMethod = CompareMethod.TimeAndSize,
            int fileTimeToleranceSeconds = 2,
            bool verifyCopiedFiles = false,
            DeletionHandling deletionHandling = DeletionHandling.Permanent,
            string versioningFolderPath = "")
        {
            return new SyncOptions
            {
                LeftPath = _left,
                RightPath = _right,
                Mode = mode,
                CompareMethod = compareMethod,
                FileTimeToleranceSeconds = fileTimeToleranceSeconds,
                VerifyCopiedFiles = verifyCopiedFiles,
                DeletionHandling = deletionHandling,
                VersioningFolderPath = versioningFolderPath,
                IncludePatterns = "*",
                ExcludePatterns = string.Empty,
                DryRun = false
            };
        }

        public void WriteLeft(string relativePath, string content, DateTime lastWriteTimeUtc)
        {
            WriteFile(LeftPath(relativePath), content, lastWriteTimeUtc);
        }

        public void WriteRight(string relativePath, string content, DateTime lastWriteTimeUtc)
        {
            WriteFile(RightPath(relativePath), content, lastWriteTimeUtc);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static void WriteFile(string path, string content, DateTime lastWriteTimeUtc)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        }
    }
}
