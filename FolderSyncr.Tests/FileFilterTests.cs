using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class FileFilterTests
{
    [TestMethod]
    public void PipeSeparatedPatternsAreMatched()
    {
        var filter = new FileFilter("*.txt | *.md", string.Empty);

        Assert.IsTrue(filter.IsMatch("notes.txt"));
        Assert.IsTrue(filter.IsMatch(Path.Combine("docs", "readme.md")));
        Assert.IsFalse(filter.IsMatch("image.png"));
    }

    [TestMethod]
    public void QuestionStarRequiresAtLeastOneCharacter()
    {
        var filter = new FileFilter("file-?*.txt", string.Empty);

        Assert.IsTrue(filter.IsMatch("file-a.txt"));
        Assert.IsFalse(filter.IsMatch("file-.txt"));
    }

    [TestMethod]
    public void FileOnlyColonHintMatchesFiles()
    {
        var filter = new FileFilter("*:", string.Empty);

        Assert.IsTrue(filter.IsMatch("root.txt"));
        Assert.IsTrue(filter.IsMatch(Path.Combine("folder", "nested.txt")));
    }

    [TestMethod]
    public void FolderOnlySlashHintMatchesDescendantFiles()
    {
        var filter = new FileFilter("*", "bin\\");

        Assert.IsFalse(filter.IsMatch(Path.Combine("bin", "app.dll")));
        Assert.IsFalse(filter.IsMatch(Path.Combine("src", "bin", "app.dll")));
        Assert.IsTrue(filter.IsMatch(Path.Combine("src", "app.cs")));
    }

    [TestMethod]
    public void FolderOnlySlashHintDoesNotMatchRootFileWithSameName()
    {
        var filter = new FileFilter("*", "bin\\");

        Assert.IsTrue(filter.IsMatch("bin"));
        Assert.IsFalse(filter.IsMatch(Path.Combine("bin", "app.dll")));
    }

    [TestMethod]
    public void FolderOnlyWildcardMatchesOnlyItemsInsideSubfolders()
    {
        var filter = new FileFilter("*\\", string.Empty);

        Assert.IsFalse(filter.IsMatch("root.txt"));
        Assert.IsTrue(filter.IsMatch(Path.Combine("sub", "child.txt")));
        Assert.IsTrue(filter.IsMatch(Path.Combine("sub", "nested", "child.txt")));
    }

    [TestMethod]
    public void LeadingSlashAnchorsPatternToFolderPairRoot()
    {
        var filter = new FileFilter("*", @"\bin\");

        Assert.IsFalse(filter.IsMatch(Path.Combine("bin", "app.dll")));
        Assert.IsTrue(filter.IsMatch(Path.Combine("src", "bin", "app.dll")));
    }

    [TestMethod]
    public void UnanchoredPathPatternMatchesFolderAtAnyLevel()
    {
        var filter = new FileFilter("*", @"SubFolder\*.tmp");

        Assert.IsFalse(filter.IsMatch(Path.Combine("SubFolder", "file.tmp")));
        Assert.IsFalse(filter.IsMatch(Path.Combine("Nested", "SubFolder", "file.tmp")));
        Assert.IsTrue(filter.IsMatch(Path.Combine("Other", "file.tmp")));
    }

    [TestMethod]
    public void NormalRulesCanMatchFoldersAndTheirDescendants()
    {
        var filter = new FileFilter("*", "*temp*");

        Assert.IsFalse(filter.IsMatch(Path.Combine("tempRoot", "child.txt")));
        Assert.IsFalse(filter.IsMatch(Path.Combine("src", "tempArea", "nested", "child.txt")));
        Assert.IsFalse(filter.IsMatch("my-temp-file.txt"));
        Assert.IsTrue(filter.IsMatch(Path.Combine("src", "normal", "child.txt")));
    }
}
