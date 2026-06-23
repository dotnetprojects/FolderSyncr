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
    public void LeadingSlashAnchorsPatternToFolderPairRoot()
    {
        var filter = new FileFilter("*", @"\bin\");

        Assert.IsFalse(filter.IsMatch(Path.Combine("bin", "app.dll")));
        Assert.IsTrue(filter.IsMatch(Path.Combine("src", "bin", "app.dll")));
    }
}
