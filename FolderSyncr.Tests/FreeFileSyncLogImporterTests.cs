using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class FreeFileSyncLogImporterTests
{
    [TestMethod]
    public void ImportReadsFreeFileSyncJsonResult()
    {
        using var file = TempLog.Create(
            "result.json",
            """
            {
              "syncResult": "warning",
              "startTime": "2026-06-23T12:25:42+00:00",
              "totalTimeSec": 12,
              "errors": 1,
              "warnings": 2,
              "totalItems": 1000,
              "totalBytes": 1024,
              "processedItems": 900,
              "processedBytes": 512,
              "logFile": "D:\\Logs\\Backup Projects 2026-06-23 122542.123.html"
            }
            """);

        var summary = new FreeFileSyncLogImporter().Import(file.Path);

        Assert.AreEqual("warning", summary.SyncResult);
        Assert.AreEqual(new DateTimeOffset(2026, 6, 23, 12, 25, 42, TimeSpan.Zero), summary.StartTime);
        Assert.AreEqual(12, summary.TotalTimeSeconds);
        Assert.AreEqual(1, summary.Errors);
        Assert.AreEqual(2, summary.Warnings);
        Assert.AreEqual(1000, summary.TotalItems);
        Assert.AreEqual(1024, summary.TotalBytes);
        Assert.AreEqual(900, summary.ProcessedItems);
        Assert.AreEqual(512, summary.ProcessedBytes);
        Assert.AreEqual(@"D:\Logs\Backup Projects 2026-06-23 122542.123.html", summary.LogFile);
    }

    [TestMethod]
    public void ImportSummarizesHtmlLogFallback()
    {
        using var file = TempLog.Create(
            "sync.html",
            """
            <html>
              <body>
                <h1>Synchronization completed with warnings</h1>
                <p>Errors: 0</p>
                <p>Warnings: 3</p>
              </body>
            </html>
            """);

        var summary = new FreeFileSyncLogImporter().Import(file.Path);

        Assert.AreEqual("warning", summary.SyncResult);
        Assert.AreEqual(0, summary.Errors);
        Assert.AreEqual(3, summary.Warnings);
        StringAssert.Contains(summary.RawSummary, "Synchronization completed with warnings");
    }

    private sealed class TempLog : IDisposable
    {
        private TempLog(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempLog Create(string fileName, string content)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FolderSyncrLogTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName);
            File.WriteAllText(path, content);
            return new TempLog(path);
        }

        public void Dispose()
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
