using System.Text.Json;
using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class SyncRunHistoryStoreTests
{
    [TestMethod]
    public void SaveWritesFreeFileSyncLikeJsonResult()
    {
        var result = new SyncRunResult(
            "success",
            new DateTimeOffset(2026, 6, 23, 12, 25, 42, TimeSpan.Zero),
            TotalTimeSec: 3,
            Errors: 0,
            Warnings: 1,
            TotalItems: 5,
            TotalBytes: 100,
            ProcessedItems: 4,
            ProcessedBytes: 80,
            LogFile: null,
            Message: "done");

        var path = new SyncRunHistoryStore().Save(result);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            Assert.AreEqual("success", root.GetProperty("syncResult").GetString());
            Assert.AreEqual(3, root.GetProperty("totalTimeSec").GetInt32());
            Assert.AreEqual(0, root.GetProperty("errors").GetInt32());
            Assert.AreEqual(1, root.GetProperty("warnings").GetInt32());
            Assert.AreEqual(5, root.GetProperty("totalItems").GetInt64());
            Assert.AreEqual(100, root.GetProperty("totalBytes").GetInt64());
            Assert.AreEqual(4, root.GetProperty("processedItems").GetInt64());
            Assert.AreEqual(80, root.GetProperty("processedBytes").GetInt64());
            Assert.AreEqual(path, root.GetProperty("logFile").GetString());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
