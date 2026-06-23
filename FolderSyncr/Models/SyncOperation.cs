namespace FolderSyncr.Models;

public sealed class SyncOperation
{
    public required string RelativePath { get; init; }
    public FileSnapshot? Left { get; init; }
    public FileSnapshot? Right { get; init; }
    public OperationKind Kind { get; init; }
    public string Status { get; set; } = "Pending";

    public string Direction => Kind switch
    {
        OperationKind.Equal => "=",
        OperationKind.CopyLeftToRight => "Left to Right",
        OperationKind.CopyRightToLeft => "Right to Left",
        OperationKind.DeleteLeft => "Delete Left",
        OperationKind.DeleteRight => "Delete Right",
        OperationKind.Conflict => "Conflict",
        _ => string.Empty
    };

    public string ActionGlyph => Kind switch
    {
        OperationKind.Equal => "==",
        OperationKind.CopyLeftToRight => "=>",
        OperationKind.CopyRightToLeft => "<=",
        OperationKind.DeleteLeft => "X<",
        OperationKind.DeleteRight => ">X",
        OperationKind.Conflict => "!",
        _ => string.Empty
    };

    public string LeftName => Left?.RelativePath ?? string.Empty;

    public string RightName => Right?.RelativePath ?? string.Empty;

    public string LeftSize => Left is null ? string.Empty : FormatBytes(Left.Length);

    public string RightSize => Right is null ? string.Empty : FormatBytes(Right.Length);

    public string LeftInfo => Left is null
        ? "-"
        : $"{FormatBytes(Left.Length)}, {Left.LastWriteTimeUtc.ToLocalTime():g}";

    public string RightInfo => Right is null
        ? "-"
        : $"{FormatBytes(Right.Length)}, {Right.LastWriteTimeUtc.ToLocalTime():g}";

    public bool WillChangeFileSystem => Kind is
        OperationKind.CopyLeftToRight or
        OperationKind.CopyRightToLeft or
        OperationKind.DeleteLeft or
        OperationKind.DeleteRight;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
