using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class FreeFileSyncGlobalSettingsImporterTests
{
    [TestMethod]
    public void TryImportReadsSupportedGlobalSettings()
    {
        using var file = TempFile.Create(
            "GlobalSettings.xml",
            """
            <FreeFileSync XmlType="GLOBAL">
              <Shared>
                <FileTimeTolerance Seconds="7" />
                <VerifyCopiedFiles Enabled="true" />
              </Shared>
            </FreeFileSync>
            """);

        var imported = new FreeFileSyncGlobalSettingsImporter().TryImport(file.Path, out var settings);

        Assert.IsTrue(imported);
        Assert.AreEqual(7, settings.FileTimeToleranceSeconds);
        Assert.IsTrue(settings.VerifyCopiedFiles);
    }

    [TestMethod]
    public void TryImportRejectsSynchronizationConfigurations()
    {
        using var file = TempFile.Create(
            "backup.ffs_gui",
            """
            <FreeFileSync XmlType="GUI">
              <FolderPairs />
            </FreeFileSync>
            """);

        Assert.IsFalse(new FreeFileSyncGlobalSettingsImporter().TryImport(file.Path, out _));
    }

    private sealed class TempFile : IDisposable
    {
        private TempFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempFile Create(string fileName, string content)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FolderSyncrGlobalSettingsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName);
            File.WriteAllText(path, content);
            return new TempFile(path);
        }

        public void Dispose()
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
