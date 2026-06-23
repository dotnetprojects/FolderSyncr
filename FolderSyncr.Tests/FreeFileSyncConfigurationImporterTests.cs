using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class FreeFileSyncConfigurationImporterTests
{
    [TestMethod]
    public void ImportReadsGuiFolderPairFiltersAndSettings()
    {
        using var file = TempConfig.Create(
            "project.ffs_gui",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <FreeFileSync XmlType="GUI">
              <FolderPairs>
                <Pair>
                  <Left>%USERPROFILE%\Source</Left>
                  <Right>D:\Backup</Right>
                  <LocalFilter>
                    <Include>
                      <Item>*</Item>
                    </Include>
                    <Exclude>
                      <Item>*.tmp | \bin\</Item>
                    </Exclude>
                  </LocalFilter>
                </Pair>
              </FolderPairs>
              <Comparison>
                <Variant>Content</Variant>
              </Comparison>
              <Synchronization>
                <Variant>TwoWay</Variant>
              </Synchronization>
            </FreeFileSync>
            """);

        var configuration = new FreeFileSyncConfigurationImporter().Import(file.Path);

        Assert.HasCount(1, configuration.FolderPairs, "Expected one imported folder pair.");
        Assert.AreEqual(Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Source"), configuration.FolderPairs[0].LeftPath);
        Assert.AreEqual(@"D:\Backup", configuration.FolderPairs[0].RightPath);
        Assert.AreEqual(CompareMethod.ContentHash, configuration.CompareMethod);
        Assert.AreEqual(SyncMode.TwoWay, configuration.SyncMode);
        StringAssert.Contains(configuration.IncludePatterns, "*");
        StringAssert.Contains(configuration.ExcludePatterns, "*.tmp");
        StringAssert.Contains(configuration.ExcludePatterns, @"\bin\");
    }

    [TestMethod]
    public void ImportReadsBatchModeAndWarnsForMultiplePairs()
    {
        using var file = TempConfig.Create(
            "job.ffs_batch",
            """
            <FreeFileSync XmlType="BATCH">
              <FolderPairs>
                <Pair>
                  <Left Path="C:\Left1" />
                  <Right Path="C:\Right1" />
                </Pair>
                <Pair>
                  <Left>C:\Left2</Left>
                  <Right>C:\Right2</Right>
                </Pair>
              </FolderPairs>
              <Compare Variant="TimeAndSize" />
              <Sync Variant="Mirror" Direction="RightToLeft" />
            </FreeFileSync>
            """);

        var configuration = new FreeFileSyncConfigurationImporter().Import(file.Path);

        Assert.HasCount(2, configuration.FolderPairs, "Expected two imported folder pairs.");
        Assert.AreEqual(@"C:\Left1", configuration.FolderPairs[0].LeftPath);
        Assert.AreEqual(@"C:\Right1", configuration.FolderPairs[0].RightPath);
        Assert.AreEqual(CompareMethod.TimeAndSize, configuration.CompareMethod);
        Assert.AreEqual(SyncMode.MirrorRightToLeft, configuration.SyncMode);
        Assert.IsTrue(configuration.Warnings.Any(warning => warning.Contains("multiple folder pairs", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class TempConfig : IDisposable
    {
        private TempConfig(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempConfig Create(string fileName, string content)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FolderSyncrConfigTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName);
            File.WriteAllText(path, content);
            return new TempConfig(path);
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
