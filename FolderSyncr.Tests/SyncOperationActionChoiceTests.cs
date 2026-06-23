using FolderSyncr.Models;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class SyncOperationActionChoiceTests
{
    [TestMethod]
    public void LeftOnlyRowsOfferCopyNoActionAndDeleteLeft()
    {
        var operation = CreateOperation("left.txt", OperationKind.CopyLeftToRight, hasLeft: true, hasRight: false);

        CollectionAssert.AreEqual(
            new[] { OperationKind.CopyLeftToRight, OperationKind.Equal, OperationKind.DeleteLeft },
            operation.ActionChoices.Select(choice => choice.Kind).ToArray());
    }

    [TestMethod]
    public void RightOnlyRowsOfferCopyNoActionAndDeleteRight()
    {
        var operation = CreateOperation("right.txt", OperationKind.DeleteRight, hasLeft: false, hasRight: true);

        CollectionAssert.AreEqual(
            new[] { OperationKind.CopyRightToLeft, OperationKind.Equal, OperationKind.DeleteRight },
            operation.ActionChoices.Select(choice => choice.Kind).ToArray());
    }

    [TestMethod]
    public void ChangedRowsOfferBothCopyDirectionsAndNoAction()
    {
        var operation = CreateOperation("changed.txt", OperationKind.CopyLeftToRight, hasLeft: true, hasRight: true);

        CollectionAssert.AreEqual(
            new[] { OperationKind.CopyLeftToRight, OperationKind.Equal, OperationKind.CopyRightToLeft },
            operation.ActionChoices.Select(choice => choice.Kind).ToArray());
    }

    [TestMethod]
    public void ConflictRowsKeepConflictAsMiddleChoice()
    {
        var operation = CreateOperation("conflict.txt", OperationKind.Conflict, hasLeft: true, hasRight: true);

        CollectionAssert.AreEqual(
            new[] { OperationKind.CopyLeftToRight, OperationKind.Conflict, OperationKind.CopyRightToLeft },
            operation.ActionChoices.Select(choice => choice.Kind).ToArray());
    }

    [TestMethod]
    public void SelectingNoActionStopsExecution()
    {
        var operation = CreateOperation("changed.txt", OperationKind.CopyLeftToRight, hasLeft: true, hasRight: true);

        Assert.IsTrue(operation.ShouldExecute);

        operation.SelectedKind = OperationKind.Equal;

        Assert.AreEqual(OperationKind.Equal, operation.EffectiveKind);
        Assert.IsFalse(operation.ShouldExecute);
        Assert.IsFalse(operation.CanSelectForSync);
    }

    private static SyncOperation CreateOperation(string relativePath, OperationKind kind, bool hasLeft, bool hasRight)
    {
        return new SyncOperation
        {
            RelativePath = relativePath,
            Kind = kind,
            Left = hasLeft ? CreateSnapshot("left", relativePath) : null,
            Right = hasRight ? CreateSnapshot("right", relativePath) : null
        };
    }

    private static FileSnapshot CreateSnapshot(string root, string relativePath)
    {
        return new FileSnapshot(root, relativePath, Path.Combine(root, relativePath), 10, DateTime.UtcNow, null);
    }
}
