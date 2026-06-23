using FolderSyncr.Models;
using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class ExternalCommandMacroExpanderTests
{
    [TestMethod]
    public void ExpandsPrimaryAndOppositeSideItemPaths()
    {
        var operation = CreateOperation(
            relativePath: Path.Combine("docs", "file one.txt"),
            leftRoot: @"C:\Left",
            rightRoot: @"D:\Right",
            includeRight: true);

        var expanded = ExternalCommandMacroExpander.Expand(
            "compare %local_path% %local_path2%",
            [operation],
            @"C:\Left",
            @"D:\Right");

        Assert.AreEqual(
            @"compare ""C:\Left\docs\file one.txt"" ""D:\Right\docs\file one.txt""",
            expanded);
    }

    [TestMethod]
    public void ExpandsMissingOppositeSidePathFromConfiguredRoot()
    {
        var operation = CreateOperation(
            relativePath: "new.txt",
            leftRoot: @"C:\Left",
            rightRoot: @"D:\Right",
            includeRight: false);

        var expanded = ExternalCommandMacroExpander.Expand(
            "echo %item_path 2%",
            [operation],
            @"C:\Left",
            @"D:\Right");

        Assert.AreEqual(@"echo ""D:\Right\new.txt""", expanded);
    }

    [TestMethod]
    public void ExpandsSelectedItemLists()
    {
        var first = CreateOperation("a.txt", @"C:\Left", @"D:\Right", includeRight: true);
        var second = CreateOperation("b.txt", @"C:\Left", @"D:\Right", includeRight: false);

        var expanded = ExternalCommandMacroExpander.Expand(
            "script %item_names%",
            [first, second],
            @"C:\Left",
            @"D:\Right");

        Assert.AreEqual(@"script ""a.txt"" ""b.txt""", expanded);
    }

    [TestMethod]
    public void ExpandsParentPath()
    {
        var operation = CreateOperation(
            relativePath: Path.Combine("docs", "nested", "file.txt"),
            leftRoot: @"C:\Left",
            rightRoot: @"D:\Right",
            includeRight: true);

        var expanded = ExternalCommandMacroExpander.Expand(
            "cmd /k cd /D %parent_path%",
            [operation],
            @"C:\Left",
            @"D:\Right");

        Assert.AreEqual(@"cmd /k cd /D ""C:\Left\docs\nested""", expanded);
    }

    private static SyncOperation CreateOperation(string relativePath, string leftRoot, string rightRoot, bool includeRight)
    {
        return new SyncOperation
        {
            RelativePath = relativePath,
            Left = new FileSnapshot(
                leftRoot,
                relativePath,
                Path.Combine(leftRoot, relativePath),
                Length: 10,
                LastWriteTimeUtc: DateTime.UtcNow,
                Hash: null),
            Right = includeRight
                ? new FileSnapshot(
                    rightRoot,
                    relativePath,
                    Path.Combine(rightRoot, relativePath),
                    Length: 10,
                    LastWriteTimeUtc: DateTime.UtcNow,
                    Hash: null)
                : null,
            Kind = OperationKind.Equal
        };
    }
}
