using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class RemoteSyncRootTests
{
    [TestMethod]
    public void TryParseParsesSftpRoot()
    {
        Assert.IsTrue(RemoteSyncRoot.TryParse("sftp://user:p%40ss@example.com:2222/backups/root", out var root));
        Assert.IsNotNull(root);
        Assert.AreEqual(RemoteSyncProtocol.Sftp, root.Protocol);
        Assert.AreEqual("example.com", root.Host);
        Assert.AreEqual(2222, root.Port);
        Assert.AreEqual("user", root.Username);
        Assert.AreEqual("p@ss", root.Password);
        Assert.AreEqual("/backups/root", root.RootPath);
        Assert.AreEqual("/backups/root/nested/file.txt", root.Combine("nested\\file.txt"));
        Assert.AreEqual("nested/file.txt", root.GetRelativePath("/backups/root/nested/file.txt"));
    }

    [TestMethod]
    public void TryParseParsesFtpRootWithDefaultPort()
    {
        Assert.IsTrue(RemoteSyncRoot.TryParse("ftp://user:pass@example.com/files", out var root));
        Assert.IsNotNull(root);
        Assert.AreEqual(RemoteSyncProtocol.Ftp, root.Protocol);
        Assert.AreEqual(21, root.Port);
        Assert.AreEqual("/files", root.RootPath);
    }

    [TestMethod]
    public async Task CompareRequiresSftpCredentials()
    {
        using var workspace = TestWorkspace.Create();
        var options = new SyncOptions
        {
            LeftPath = "sftp://example.com/backups",
            RightPath = workspace.RootPath,
            Mode = SyncMode.TwoWay,
            CompareMethod = CompareMethod.TimeAndSize
        };

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new SyncEngine().CompareAsync(options, null, CancellationToken.None));

        StringAssert.Contains(exception.Message, "SFTP path must include");
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _root;

        private TestWorkspace(string root)
        {
            _root = root;
            Directory.CreateDirectory(_root);
        }

        public string RootPath => _root;

        public static TestWorkspace Create()
        {
            return new TestWorkspace(Path.Combine(Path.GetTempPath(), "FolderSyncrRemoteTests_" + Guid.NewGuid().ToString("N")));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
