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
  - `Custom` rule matrix
- Custom synchronization rules for left-only, right-only, newer, and same-time-different files.
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
- SFTP roots such as `sftp://user:password@example.com/backups` for comparing, copying, and permanently deleting remote files.
- FTP roots such as `ftp://user:password@example.com/backups`, with anonymous FTP when no credentials are supplied.
- Configurable remote connection/channel count and optional SFTP compression.
- Optional one-hour daylight-saving-time shift ignore for time/size comparisons.
- Include and exclude wildcard filters.
- Import FreeFileSync `.ffs_gui`, `.ffs_batch`, and `.ffs_real` configurations.
- Edit multiple folder pairs in the WPF UI.
- Export supported settings to FreeFileSync-compatible `.ffs_gui` files.
- Import FreeFileSync JSON results and log files.
- Store local sync run history as FreeFileSync-like JSON.
- Maintain a hidden `sync.ffs_db`-style two-way sync database in each synchronized root.
- Detect moved files from the sync database and show linked move hints in the preview.
- Run saved configurations unattended with `FolderSyncr.Cli`, FreeFileSync-like exit codes, and JSON output.
- Save, open, and reload native `.foldersyncr.json` configurations.
- Expand environment variables in folder paths.
- Expand date/time macros in folder paths.
- Expand common Windows special-folder macros in folder paths.
- Conflict detection when files differ, neither side is clearly newer, or both sides changed since the last two-way sync.
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

When a GitHub release is published, the release workflow builds `FolderSyncr.exe` and `FolderSyncr.Cli.exe` for `win-x64`, applies the release tag as the application version, creates a ZIP, and uploads it to the release assets.

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

Start the WPF app minimized:

```powershell
dotnet run --project .\FolderSyncr\FolderSyncr.csproj -- .\Backup.foldersyncr.json --minimized
```

Run a saved configuration unattended:

```powershell
dotnet run --project .\FolderSyncr.Cli\FolderSyncr.Cli.csproj -- .\Backup.foldersyncr.json --json .\result.json
```

`FolderSyncr.Cli` writes the run result JSON to stdout, optionally writes the same JSON to `--json <path>`, exits automatically after the unattended run, and returns `0` for success, `1` for warnings, `2` for errors, or `3` for cancellation. Pass multiple configuration files to merge their folder pairs into one in-memory batch run, and optionally include a FreeFileSync `GlobalSettings.xml` file for supported global options. Add `--dry-run` to compare without applying changes, `--watch --idle <seconds>` for realtime monitoring, `--var NAME=VALUE` for temporary run-scoped variables, `--error-handling show|ignore|cancel` to control per-item sync errors, `--symbolic-links skip|follow|copy` to control symbolic links, `--connections <count>` to run file operations in parallel, and `--sftp-compression` to prefer SFTP compression.

When a native or imported FreeFileSync configuration contains multiple preserved folder pairs, `FolderSyncr.Cli` processes every pair in one run and applies local per-pair filters when present. Passing a single `-dirpair <left> <right>` override runs only that overridden pair.

For service-style usage, run `FolderSyncr.Cli` from Windows Task Scheduler at logon, on a schedule, on workstation unlock, or on idle. Use the WPF app with `--minimized` only when a user should still be able to review the session interactively.

## How To Use

The short version is: choose two folders, click `Compare`, review the preview, then click `Synchronize`.

For the full walkthrough, see [docs/USER_GUIDE.md](docs/USER_GUIDE.md).

For the MTP/portable-device implementation investigation, see [docs/MTP_SUPPORT_INVESTIGATION.md](docs/MTP_SUPPORT_INVESTIGATION.md).

## Filters

Filters follow the FreeFileSync wildcard syntax. Use `|` or new lines between items; FolderSyncr also accepts semicolons and commas for older native configurations. Folder rules match all descendant files.

Examples:

```text
*.txt | *.md
```

```text
bin\ | obj\ | .git\
```

## External Commands

Use `Tools` -> `External commands` to add row context-menu commands such as Explorer, clipboard, or WinMerge actions. Press `0` through `9` to run configured commands for selected rows. Commands support FreeFileSync-style item macros including `%item_path%`, `%local_path%`, `%item_name%`, `%parent_path%`, opposite-side suffix `2`, and selected-item-list suffix `s`.

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
