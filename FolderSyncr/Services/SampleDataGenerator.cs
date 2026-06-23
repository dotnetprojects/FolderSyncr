using System.IO;

namespace FolderSyncr.Services;

public sealed class SampleDataGenerator
{
    public SampleDataSet Create()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FolderSyncr",
            "Samples",
            "Sample-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        var left = Path.Combine(root, "Left");
        var right = Path.Combine(root, "Right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);

        var now = DateTime.UtcNow;
        WriteFile(Path.Combine(left, "equal.txt"), "same on both sides", now);
        WriteFile(Path.Combine(right, "equal.txt"), "same on both sides", now);

        WriteFile(Path.Combine(left, "left-only.txt"), "copy me to the right", now);
        WriteFile(Path.Combine(right, "right-only.txt"), "copy me to the left", now);

        WriteFile(Path.Combine(left, "newer-left.txt"), "newer left version", now);
        WriteFile(Path.Combine(right, "newer-left.txt"), "older right version", now.AddMinutes(-10));

        WriteFile(Path.Combine(left, "newer-right.txt"), "older left version", now.AddMinutes(-10));
        WriteFile(Path.Combine(right, "newer-right.txt"), "newer right version", now);

        WriteFile(Path.Combine(left, "conflict.txt"), "left conflict", now);
        WriteFile(Path.Combine(right, "conflict.txt"), "right conflict", now);

        WriteFile(Path.Combine(left, "Docs", "report.md"), "# Report\n\nLeft sample document.", now);
        WriteFile(Path.Combine(right, "Media", "notes.txt"), "Right sample note.", now);

        return new SampleDataSet(root, left, right);
    }

    private static void WriteFile(string path, string content, DateTime lastWriteTimeUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
    }
}
