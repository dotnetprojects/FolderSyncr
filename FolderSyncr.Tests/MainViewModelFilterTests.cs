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

    [TestMethod]
    public async Task OperationCategoryCommandsFilterVisibleRows()
    {
        var viewModel = new MainViewModel();
        viewModel.Operations.Add(CreateOperation("same.txt", OperationKind.Equal));
        viewModel.Operations.Add(CreateOperation("copy-right.txt", OperationKind.CopyLeftToRight));
        viewModel.Operations.Add(CreateOperation("copy-left.txt", OperationKind.CopyRightToLeft));
        viewModel.Operations.Add(CreateOperation("delete-left.txt", OperationKind.DeleteLeft));
        viewModel.Operations.Add(CreateOperation("delete-right.txt", OperationKind.DeleteRight));
        viewModel.Operations.Add(CreateOperation("conflict.txt", OperationKind.Conflict));

        Assert.AreEqual(1, viewModel.EqualCount);
        Assert.AreEqual(1, viewModel.CopyLeftToRightCount);
        Assert.AreEqual(1, viewModel.CopyRightToLeftCount);
        Assert.AreEqual(1, viewModel.DeleteLeftCount);
        Assert.AreEqual(1, viewModel.DeleteRightCount);
        Assert.AreEqual(1, viewModel.ConflictCount);

        viewModel.ShowCopyLeftToRightOperationsCommand.Execute(null);
        await Task.Yield();
        CollectionAssert.AreEqual(new[] { "copy-right.txt" }, GetVisiblePaths(viewModel));

        viewModel.ShowDeleteRightOperationsCommand.Execute(null);
        await Task.Yield();
        CollectionAssert.AreEqual(new[] { "delete-right.txt" }, GetVisiblePaths(viewModel));

        viewModel.ShowAllOperationsCommand.Execute(null);
        await Task.Yield();
        Assert.HasCount(6, GetVisiblePaths(viewModel));
    }

    private static SyncOperation CreateOperation(string relativePath, OperationKind kind = OperationKind.CopyLeftToRight)
    {
        return new SyncOperation
        {
            RelativePath = relativePath,
            Kind = kind
        };
    }

    private static string[] GetVisiblePaths(MainViewModel viewModel)
    {
        return viewModel.OperationsView
            .Cast<SyncOperation>()
            .Select(operation => operation.RelativePath)
            .ToArray();
    }
}
