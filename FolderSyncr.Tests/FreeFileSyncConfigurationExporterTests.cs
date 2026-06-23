using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class FreeFileSyncConfigurationExporterTests
{
    [TestMethod]
    public void CreateDocumentWritesSupportedFreeFileSyncSettings()
    {
        var configuration = CreateConfiguration(
            SyncMode.MirrorRightToLeft,
            CompareMethod.ContentHash,
            includePatterns: "*.txt;*.md",
            excludePatterns: "**/bin/**");

        var document = new FreeFileSyncConfigurationExporter().CreateDocument(configuration);

        Assert.AreEqual("FreeFileSync", document.Root?.Name.LocalName);
        Assert.AreEqual("GUI", document.Root?.Attribute("XmlType")?.Value);
        Assert.AreEqual("C:\\Left", document.Descendants("Left").Single().Value);
        Assert.AreEqual("D:\\Right", document.Descendants("Right").Single().Value);
        Assert.AreEqual("Content", document.Descendants("Comparison").Single().Element("Variant")?.Value);
        Assert.AreEqual("Mirror", document.Descendants("Synchronization").Single().Element("Variant")?.Value);
        Assert.AreEqual("RightToLeft", document.Descendants("Synchronization").Single().Element("Direction")?.Value);
        CollectionAssert.AreEqual(
            new[] { "*.txt", "*.md" },
            document.Descendants("Include").Single().Elements("Item").Select(item => item.Value).ToArray());
        Assert.AreEqual("**/bin/**", document.Descendants("Exclude").Single().Elements("Item").Single().Value);
    }

    [TestMethod]
    public void SaveWritesConfigurationThatImporterCanRead()
    {
        var path = Path.Combine(Path.GetTempPath(), "FolderSyncrFfsExport_" + Guid.NewGuid().ToString("N"), "backup.ffs_gui");
        try
        {
            new FreeFileSyncConfigurationExporter().Save(
                path,
                CreateConfiguration(SyncMode.UpdateLeftToRight, CompareMethod.SizeOnly, includePatterns: "*", excludePatterns: "*.tmp"));

            var imported = new FreeFileSyncConfigurationImporter().Import(path);

            Assert.HasCount(1, imported.FolderPairs);
            Assert.AreEqual("C:\\Left", imported.FolderPairs[0].LeftPath);
            Assert.AreEqual("D:\\Right", imported.FolderPairs[0].RightPath);
            Assert.AreEqual(SyncMode.UpdateLeftToRight, imported.SyncMode);
            Assert.AreEqual(CompareMethod.SizeOnly, imported.CompareMethod);
            Assert.AreEqual("*", imported.FolderPairs[0].IncludePatterns);
            Assert.AreEqual("*.tmp", imported.FolderPairs[0].ExcludePatterns);
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

    [TestMethod]
    public void SaveWritesEveryPreservedFolderPair()
    {
        var path = Path.Combine(Path.GetTempPath(), "FolderSyncrFfsExport_" + Guid.NewGuid().ToString("N"), "backup.ffs_gui");
        try
        {
            new FreeFileSyncConfigurationExporter().Save(
                path,
                CreateConfiguration(
                    SyncMode.TwoWay,
                    CompareMethod.TimeAndSize,
                    includePatterns: "*",
                    excludePatterns: string.Empty)
                with
                {
                    FolderPairs =
                    [
                        new FolderPairConfiguration("C:\\Left1", "D:\\Right1"),
                        new FolderPairConfiguration("C:\\Left2", "D:\\Right2")
                    ]
                });

            var imported = new FreeFileSyncConfigurationImporter().Import(path);

            Assert.HasCount(2, imported.FolderPairs);
            Assert.AreEqual("C:\\Left1", imported.FolderPairs[0].LeftPath);
            Assert.AreEqual("D:\\Right1", imported.FolderPairs[0].RightPath);
            Assert.AreEqual("C:\\Left2", imported.FolderPairs[1].LeftPath);
            Assert.AreEqual("D:\\Right2", imported.FolderPairs[1].RightPath);
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

    [TestMethod]
    public void SaveWritesPairSpecificFilters()
    {
        var configuration = CreateConfiguration(
            SyncMode.TwoWay,
            CompareMethod.TimeAndSize,
            includePatterns: "*",
            excludePatterns: string.Empty)
            with
            {
                FolderPairs =
                [
                    new FolderPairConfiguration("C:\\Docs", "D:\\Docs", "*.txt", "cache\\"),
                    new FolderPairConfiguration("C:\\Photos", "D:\\Photos", "*.jpg", "thumbs.db")
                ]
            };

        var document = new FreeFileSyncConfigurationExporter().CreateDocument(configuration);
        var pairs = document.Descendants("Pair").ToArray();

        Assert.AreEqual("*.txt", pairs[0].Descendants("Include").Single().Elements("Item").Single().Value);
        Assert.AreEqual("cache\\", pairs[0].Descendants("Exclude").Single().Elements("Item").Single().Value);
        Assert.AreEqual("*.jpg", pairs[1].Descendants("Include").Single().Elements("Item").Single().Value);
        Assert.AreEqual("thumbs.db", pairs[1].Descendants("Exclude").Single().Elements("Item").Single().Value);
    }



    private static FolderSyncrConfiguration CreateConfiguration(
        SyncMode syncMode,
        CompareMethod compareMethod,
        string includePatterns,
        string excludePatterns)
    {
        return new FolderSyncrConfiguration(
            Version: 6,
            Name: "Export test",
            LeftPath: "C:\\Left",
            RightPath: "D:\\Right",
            syncMode,
            compareMethod,
            FileTimeToleranceSeconds: 2,
            IgnoreDaylightSavingTimeShift: false,
            VerifyCopiedFiles: false,
            DeletionHandling.Permanent,
            VersioningMode.TimeStampFolder,
            VersioningFolderPath: string.Empty,
            ErrorHandling: SyncErrorHandling.ShowErrors,
            SymbolicLinkHandling: SymbolicLinkHandling.Skip,
            IncludePatterns: includePatterns,
            ExcludePatterns: excludePatterns,
            IsDarkMode: false);
    }
}
