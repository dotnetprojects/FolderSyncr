using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FolderSyncr.Models;

public sealed class SyncOperation : INotifyPropertyChanged
{
    private bool? _isSelectedForSync;
    private string _status = "Pending";
    private string? _movePartnerRelativePath;
    private OperationKind? _selectedKind;
    private int _displayIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string RelativePath { get; init; }
    public FileSnapshot? Left { get; init; }
    public FileSnapshot? Right { get; init; }
    public OperationKind Kind { get; init; }
    public int DisplayIndex
    {
        get => _displayIndex;
        set => SetProperty(ref _displayIndex, value);
    }

    public OperationKind SelectedKind
    {
        get => _selectedKind ?? Kind;
        set
        {
            var normalized = NormalizeSelectedKind(value);
            if (_selectedKind == normalized)
            {
                return;
            }

            _selectedKind = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveKind));
            OnPropertyChanged(nameof(Direction));
            OnPropertyChanged(nameof(ActionGlyph));
            OnPropertyChanged(nameof(ActionDescription));
            OnPropertyChanged(nameof(WillChangeFileSystem));
            OnPropertyChanged(nameof(CanSelectForSync));
            OnPropertyChanged(nameof(IsSelectedForSync));
            OnPropertyChanged(nameof(ShouldExecute));
        }
    }

    public OperationKind EffectiveKind => SelectedKind;

    public IReadOnlyList<SyncOperationActionChoice> ActionChoices => CreateActionChoices();

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
        get => WillChangeFileSystem && (_isSelectedForSync ?? true);
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

    public string Direction => EffectiveKind switch
    {
        OperationKind.Equal => "No action",
        OperationKind.CopyLeftToRight => "Copy to right",
        OperationKind.CopyRightToLeft => "Copy to left",
        OperationKind.DeleteLeft => "Delete left",
        OperationKind.DeleteRight => "Delete right",
        OperationKind.Conflict => "Conflict",
        _ => string.Empty
    };

    public string ActionDescription => IsDetectedMove
        ? $"Move detected with {MovePartnerRelativePath}"
        : Direction;

    public string ActionGlyph => EffectiveKind switch
    {
        OperationKind.CopyLeftToRight when IsDetectedMove => "M=>",
        OperationKind.CopyRightToLeft when IsDetectedMove => "<=M",
        OperationKind.DeleteLeft when IsDetectedMove => "M<X",
        OperationKind.DeleteRight when IsDetectedMove => "X>M",
        OperationKind.Equal => "--",
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

    public bool WillChangeFileSystem => EffectiveKind is
        OperationKind.CopyLeftToRight or
        OperationKind.CopyRightToLeft or
        OperationKind.DeleteLeft or
        OperationKind.DeleteRight;

    public bool ShouldExecute => WillChangeFileSystem && IsSelectedForSync;

    public bool CanSelectForSync => WillChangeFileSystem;

    public bool IsDetectedMove => !string.IsNullOrWhiteSpace(MovePartnerRelativePath);

    private OperationKind NormalizeSelectedKind(OperationKind requestedKind)
    {
        return ActionChoices.Any(choice => choice.Kind == requestedKind)
            ? requestedKind
            : Kind;
    }

    private IReadOnlyList<SyncOperationActionChoice> CreateActionChoices()
    {
        if (Kind == OperationKind.Equal)
        {
            return [CreateChoice(OperationKind.Equal)];
        }

        if (Left is not null && Right is not null)
        {
            var middleKind = Kind == OperationKind.Conflict ? OperationKind.Conflict : OperationKind.Equal;
            return
            [
                CreateChoice(OperationKind.CopyLeftToRight),
                CreateChoice(middleKind),
                CreateChoice(OperationKind.CopyRightToLeft)
            ];
        }

        if (Left is not null)
        {
            return
            [
                CreateChoice(OperationKind.CopyLeftToRight),
                CreateChoice(OperationKind.Equal),
                CreateChoice(OperationKind.DeleteLeft)
            ];
        }

        if (Right is not null)
        {
            return
            [
                CreateChoice(OperationKind.CopyRightToLeft),
                CreateChoice(OperationKind.Equal),
                CreateChoice(OperationKind.DeleteRight)
            ];
        }

        return [CreateChoice(OperationKind.Equal)];
    }

    private static SyncOperationActionChoice CreateChoice(OperationKind kind)
    {
        return kind switch
        {
            OperationKind.CopyLeftToRight => new SyncOperationActionChoice(kind, "=>", "Copy left item to right"),
            OperationKind.CopyRightToLeft => new SyncOperationActionChoice(kind, "<=", "Copy right item to left"),
            OperationKind.DeleteLeft => new SyncOperationActionChoice(kind, "X<", "Delete left item"),
            OperationKind.DeleteRight => new SyncOperationActionChoice(kind, ">X", "Delete right item"),
            OperationKind.Conflict => new SyncOperationActionChoice(kind, "!", "Conflict"),
            _ => new SyncOperationActionChoice(OperationKind.Equal, "--", "Do nothing")
        };
    }

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
