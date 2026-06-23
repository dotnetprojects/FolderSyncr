# FolderSyncr

FolderSyncr is a .NET 10 WPF folder comparison and synchronization tool for Windows. It follows the proven compare-first workflow used by tools like FreeFileSync: choose two folders, compare them, review the planned operations, then synchronize.

FolderSyncr is an independent project and is not affiliated with FreeFileSync.

See the [tool documentation](docs/USER_GUIDE.md) for screenshots and usage details.

## Features

- Compare two folders before changing anything.
- Preview left-to-right copies, right-to-left copies, deletes, equal files, and conflicts.
- Synchronization modes:
  - `TwoWay`
  - `MirrorLeftToRight`
  - `MirrorRightToLeft`
  - `UpdateLeftToRight`
  - `UpdateRightToLeft`
- Compare by file time/size or SHA-256 content hash.
- Configurable file time tolerance for time/size comparisons.
- Optional binary verification after copying files.
- Deletion handling options: permanent delete, recycle bin, or timestamped versioning folder.
- Versioning modes for replace, timestamped folders, and file-time names.
- Lock files prevent concurrent sync jobs from writing the same folder pair.
- Built-in sample data generator for first-run testing.
- Drag-and-drop folder selection for left and right paths.
- Per-row checkboxes to include or exclude planned sync actions.
- Result-grid double-click plus context menu actions for opening items, copying relative paths, and include/exclude filters.
- Volume-label paths like `[Backup-Disk]\folder` and volume GUID paths like `\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\folder` for removable drives.
- UNC paths like `\\server\share\folder` and extended UNC paths like `\\?\UNC\server\share\folder`, with clear unavailable-share errors.
- Optional one-hour daylight-saving-time shift ignore for time/size comparisons.
- Include and exclude wildcard filters.
- Import FreeFileSync `.ffs_gui`, `.ffs_batch`, and `.ffs_real` configurations.
- Import FreeFileSync JSON results and log files.
- Store local sync run history as FreeFileSync-like JSON.
- Save, open, and reload native `.foldersyncr.json` configurations.
- Expand environment variables in folder paths.
- Expand date/time macros in folder paths.
- Expand common Windows special-folder macros in folder paths.
- Conflict detection when files differ but neither side is clearly newer.
- Cancellable compare and synchronization operations.
- FreeFileSync-inspired layout with configuration list, overview pane, split file grids, action column, and statistics bar.
- Light and dark mode toggle.

## Requirements

- Windows 10 or newer
- .NET 10 SDK

Check your installed SDKs:

```powershell
dotnet --list-sdks
```

## Build

```powershell
dotnet build .\FolderSyncr.slnx
```

## Test

```powershell
dotnet test .\FolderSyncr.slnx
```

## Release Builds

When a GitHub release is published, the release workflow builds `FolderSyncr` for `win-x64`, applies the release tag as the application version, creates a ZIP, and uploads it to the release assets.

Recommended release tag format:

```text
v1.2.3
```

The uploaded asset is named like:

```text
FolderSyncr-v1.2.3-win-x64.zip
```

## Run

```powershell
dotnet run --project .\FolderSyncr\FolderSyncr.csproj
```

Open a configuration at startup:

```powershell
dotnet run --project .\FolderSyncr\FolderSyncr.csproj -- .\Backup.foldersyncr.json
```

Override the left and right folders at startup:

```powershell
dotnet run --project .\FolderSyncr\FolderSyncr.csproj -- .\Backup.foldersyncr.json -dirpair C:\Source D:\Target
```

## How To Use

The short version is: choose two folders, click `Compare`, review the preview, then click `Synchronize`.

For the full walkthrough, see [docs/USER_GUIDE.md](docs/USER_GUIDE.md).

## Filters

Filters use simple wildcard patterns separated by semicolons, commas, vertical bars, or new lines.

Examples:

```text
*.txt;*.md
```

```text
**/bin/**;**/obj/**;**/.git/**
```

## Project Structure

```text
FolderSyncr/
  Models/       File comparison and operation models
  Services/     Filtering, comparison, and synchronization engine
  ViewModels/   WPF view models and commands
  MainWindow.*  Main WPF interface
```

## Safety Notes

FolderSyncr previews operations before it writes files. Still, synchronization tools can copy, overwrite, and delete data depending on the selected mode. Test with disposable folders before using it on important files.

## License

FolderSyncr is licensed under the MIT License. See [LICENSE](LICENSE).
