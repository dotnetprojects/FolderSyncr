using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FolderSyncr.Models;

public sealed class SyncOperation : INotifyPropertyChanged
{
    private bool? _isSelectedForSync;
    private string _status = "Pending";
    private string? _movePartnerRelativePath;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string RelativePath { get; init; }
    public FileSnapshot? Left { get; init; }
    public FileSnapshot? Right { get; init; }
    public OperationKind Kind { get; init; }
    public string? MovePartnerRelativePath
    {
        get => _movePartnerRelativePath;
        set
        {
            if (SetProperty(ref _movePartnerRelativePath, value))
            {
                OnPropertyChanged(nameof(IsDetectedMove));
                OnPropertyChanged(nameof(ActionGlyph));
                OnPropertyChanged(nameof(ActionDescription));
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsSelectedForSync
    {
        get => _isSelectedForSync ?? WillChangeFileSystem;
        set
        {
            if (!WillChangeFileSystem)
            {
                value = false;
            }

            if (_isSelectedForSync != value)
            {
                _isSelectedForSync = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShouldExecute));
            }
        }
    }

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

    public string ActionDescription => IsDetectedMove
        ? $"Move detected with {MovePartnerRelativePath}"
        : Direction;

    public string ActionGlyph => Kind switch
    {
        OperationKind.CopyLeftToRight when IsDetectedMove => "M=>",
        OperationKind.CopyRightToLeft when IsDetectedMove => "<=M",
        OperationKind.DeleteLeft when IsDetectedMove => "M<X",
        OperationKind.DeleteRight when IsDetectedMove => "X>M",
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

    public bool ShouldExecute => WillChangeFileSystem && IsSelectedForSync;

    public bool CanSelectForSync => WillChangeFileSystem;

    public bool IsDetectedMove => !string.IsNullOrWhiteSpace(MovePartnerRelativePath);

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

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
