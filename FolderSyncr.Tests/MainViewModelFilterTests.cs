using FolderSyncr.Models;
using FolderSyncr.ViewModels;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class MainViewModelFilterTests
{
    [TestMethod]
    public async Task ExcludeOperationAddsRelativePathToExcludePatternsOnce()
    {
        var viewModel = new MainViewModel { ExcludePatterns = "existing.txt" };
        var operation = CreateOperation(@"nested\skip.txt");

        await viewModel.ExcludeOperationAsync(operation);
        await viewModel.ExcludeOperationAsync(operation);

        Assert.AreEqual($"existing.txt{Environment.NewLine}nested/skip.txt", viewModel.ExcludePatterns);
        StringAssert.Contains(viewModel.Status, "Excluded nested\\skip.txt");
    }

    [TestMethod]
    public async Task IncludeOperationAddsRelativePathAndRemovesExactExclude()
    {
        var viewModel = new MainViewModel
        {
            IncludePatterns = "*.txt",
            ExcludePatterns = $"nested/keep.txt{Environment.NewLine}other.tmp"
        };

        await viewModel.IncludeOperationAsync(CreateOperation(@"nested\keep.txt"));

        Assert.AreEqual($"*.txt{Environment.NewLine}nested/keep.txt", viewModel.IncludePatterns);
        Assert.AreEqual("other.tmp", viewModel.ExcludePatterns);
        StringAssert.Contains(viewModel.Status, "Included nested\\keep.txt");
    }

    [TestMethod]
    public async Task IncludeOperationKeepsWildcardIncludePattern()
    {
        var viewModel = new MainViewModel { IncludePatterns = "*", ExcludePatterns = "nested/keep.txt" };

        await viewModel.IncludeOperationAsync(CreateOperation(@"nested\keep.txt"));

        Assert.AreEqual("*", viewModel.IncludePatterns);
        Assert.AreEqual(string.Empty, viewModel.ExcludePatterns);
    }

    private static SyncOperation CreateOperation(string relativePath)
    {
        return new SyncOperation
        {
            RelativePath = relativePath,
            Kind = OperationKind.CopyLeftToRight
        };
    }
}
