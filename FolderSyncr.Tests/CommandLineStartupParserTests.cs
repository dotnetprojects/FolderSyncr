using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class CommandLineStartupParserTests
{
    [TestMethod]
    public void ParseReadsConfigurationPath()
    {
        var options = new CommandLineStartupParser().Parse(["backup.foldersyncr.json"]);

        Assert.AreEqual("backup.foldersyncr.json", options.ConfigurationPath);
        Assert.IsNull(options.OverrideLeftPath);
        Assert.IsNull(options.OverrideRightPath);
    }

    [TestMethod]
    public void ParseReadsDirPairOverride()
    {
        var options = new CommandLineStartupParser().Parse(
            ["backup.foldersyncr.json", "-dirpair", @"C:\NewSource", @"D:\NewTarget"]);

        Assert.AreEqual("backup.foldersyncr.json", options.ConfigurationPath);
        Assert.AreEqual(@"C:\NewSource", options.OverrideLeftPath);
        Assert.AreEqual(@"D:\NewTarget", options.OverrideRightPath);
    }

    [TestMethod]
    public void ParseRequiresBothDirPairPaths()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CommandLineStartupParser().Parse(["-dirpair", @"C:\OnlyLeft"]));
    }

    [TestMethod]
    public void ParseReadsMinimizedStartupOption()
    {
        var options = new CommandLineStartupParser().Parse(["backup.foldersyncr.json", "--minimized"]);

        Assert.AreEqual("backup.foldersyncr.json", options.ConfigurationPath);
        Assert.IsTrue(options.StartMinimized);
    }
}
