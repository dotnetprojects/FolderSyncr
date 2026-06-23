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

        public SyncOptions CreateOptions(SyncMode mode)
        {
            return new SyncOptions
            {
                LeftPath = _left,
                RightPath = _right,
                Mode = mode,
                CompareMethod = CompareMethod.TimeAndSize,
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
