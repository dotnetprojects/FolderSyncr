using FolderSyncr.Models;
using FolderSyncr.ViewModels;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class MainViewModelOverviewTests
{
    [TestMethod]
    public void SelectedOverviewItemFiltersOperationsToTopFolder()
    {
        var viewModel = CreateViewModelWithOperations();

        viewModel.SelectedOverviewItem = new OverviewItem
        {
            Folder = "nested",
            Items = 2,
            Size = "2 B",
            Percentage = 66
        };

        CollectionAssert.AreEqual(new[] { "nested/a.txt", "nested/b.txt" }, GetVisiblePaths(viewModel));
        StringAssert.Contains(viewModel.Status, "nested");
    }

    [TestMethod]
    public async Task ShowAllOperationsClearsOverviewNavigation()
    {
        var viewModel = CreateViewModelWithOperations();
        viewModel.SelectedOverviewItem = new OverviewItem
        {
            Folder = "nested",
            Items = 2,
            Size = "2 B",
            Percentage = 66
        };

        viewModel.ShowAllOperationsCommand.Execute(null);
        await Task.Yield();

        Assert.IsNull(viewModel.SelectedOverviewItem);
        Assert.HasCount(3, GetVisiblePaths(viewModel));
    }

    private static MainViewModel CreateViewModelWithOperations()
    {
        var viewModel = new MainViewModel();
        viewModel.Operations.Add(CreateOperation("nested/a.txt"));
        viewModel.Operations.Add(CreateOperation("nested/b.txt"));
        viewModel.Operations.Add(CreateOperation("root.txt"));
        return viewModel;
    }

    private static SyncOperation CreateOperation(string relativePath)
    {
        return new SyncOperation
        {
            RelativePath = relativePath,
            Kind = OperationKind.CopyLeftToRight
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
