# FolderSyncr

FolderSyncr is a .NET 10 WPF folder comparison and synchronization tool for Windows. It follows the proven compare-first workflow used by tools like FreeFileSync: choose two folders, compare them, review the planned operations, then synchronize.

FolderSyncr is an independent project and is not affiliated with FreeFileSync.

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
- Include and exclude wildcard filters.
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

## How To Use

1. Start FolderSyncr.
2. Choose the left folder and right folder in the folder-pair row.
3. Choose a synchronization mode:
   - `TwoWay`: copies the newer side to the older or missing side.
   - `MirrorLeftToRight`: makes the right side match the left side, including deletes on the right.
   - `MirrorRightToLeft`: makes the left side match the right side, including deletes on the left.
   - `UpdateLeftToRight`: copies new or newer files from left to right without deleting target-only files.
   - `UpdateRightToLeft`: copies new or newer files from right to left without deleting target-only files.
4. Choose the comparison method:
   - `TimeAndSize`: fast and suitable for most sync jobs.
   - `ContentHash`: slower, but detects content changes even when timestamps are misleading.
5. Adjust include and exclude filters if needed.
6. Click `Compare`.
7. Review the preview grid:
   - `=>` copies from left to right.
   - `<=` copies from right to left.
   - `X<` deletes on the left.
   - `>X` deletes on the right.
   - `==` means both sides are equal.
   - `!` means a conflict needs attention.
8. Click `Synchronize` to apply the planned file operations.

## Filters

Filters use simple wildcard patterns separated by semicolons, commas, or new lines.

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
