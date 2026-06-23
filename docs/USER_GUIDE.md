# FolderSyncr User Guide

FolderSyncr compares two folders, shows a preview of the file operations it plans to perform, and then synchronizes only after you confirm by clicking `Synchronize`.

## Main Window

This screenshot is captured from the actual WPF application.

![FolderSyncr main window](screenshots/foldersyncr-light.png)

Dark mode is also captured from the running application.

![FolderSyncr dark mode](screenshots/foldersyncr-dark.png)

Menu rendering is checked in the same smoke run.

![FolderSyncr light menu](screenshots/foldersyncr-light-menu.png)

Dark-mode dialogs are also checked.

![FolderSyncr dark settings dialog](screenshots/foldersyncr-dark-settings.png)

## Basic Workflow

1. Choose a left folder.
2. Choose a right folder.
3. Select the synchronization mode.
4. Select the comparison method.
5. Click `Compare`.
6. Review the planned actions in the center preview grid.
7. Click `Synchronize` when the preview looks correct.

You can also drag a folder from Explorer onto either path box to set the left or right folder.

## Synchronization Modes

Use the gear button to choose `TwoWay`, mirror, update, or `Custom` synchronization. `Custom` lets you choose a separate action for left-only files, right-only files, left-newer files, right-newer files, and files that differ without a clear newer side. The available actions are do nothing, copy left to right, copy right to left, delete left, and delete right. Impossible actions are shown as conflicts during compare.

## Sample Data

Use `Tools` -> `Create sample data` to create a disposable folder pair under `%LOCALAPPDATA%\FolderSyncr\Samples`. The sample includes equal files, left-only files, right-only files, newer files on each side, nested folders, and a conflict case.

## FreeFileSync Import

Use `File` -> `Open configuration` to import a FreeFileSync `.ffs_gui`, `.ffs_batch`, or `.ffs_real` file. FolderSyncr preserves all imported folder pairs and local per-pair filters in saved native configurations and compatible FreeFileSync exports. Use the plus button beside the folder path row to edit the preserved folder pairs. The main view shows the first pair, while `FolderSyncr.Cli.exe` runs every preserved pair with its local filters unless you pass a single `-dirpair` override. For `.ffs_real` files, FolderSyncr imports the configured folders but does not start a real-time monitor yet. Unsupported FreeFileSync options are written as import warnings in the app log.

Use `Tools` -> `Open FreeFileSync log` to import a FreeFileSync JSON result or log file. JSON results from FreeFileSync batch runs show the synchronization result, start time, duration, errors, warnings, processed item counts, processed byte counts, and referenced log file.

## Run History

Each synchronization writes a JSON run result to `%LOCALAPPDATA%\FolderSyncr\History`. The JSON uses FreeFileSync-like fields: `syncResult`, `startTime`, `totalTimeSec`, `errors`, `warnings`, `totalItems`, `totalBytes`, `processedItems`, `processedBytes`, and `logFile`.

## Sync Database

After a successful two-way synchronization, FolderSyncr writes a hidden `sync.ffs_db`-style JSON inventory to both synchronized roots. The database records the last known left and right fingerprints for each synchronized relative path. FolderSyncr ignores this metadata file during compare and sync previews.

When the database is available, two-way compare uses it to distinguish one-sided changes from conflicts. If both sides changed since the last completed two-way sync, the item is shown as a conflict even when one side has a newer timestamp. If one side deleted an unchanged file, FolderSyncr propagates that deletion to the other side.

FolderSyncr also uses the database to detect moved files. A detected move is shown as a linked copy/delete pair with a move glyph and tooltip in the action column. Synchronization currently applies the move through the existing copy and delete operations.

## FolderSyncr Configurations

Use `File` -> `Save` or `Save as` to store the current folder pair, any preserved imported folder pairs, sync mode, custom rules, compare method, filters, external commands, and theme as a `.foldersyncr.json` file. Use `File` -> `Open configuration` to reopen it later, and `File` -> `Reload configuration` to discard local changes and load the current file again.

Use `File` -> `Export FreeFileSync configuration` to write a compatible `.ffs_gui` file. The export includes the current or preserved folder pairs, comparison method, standard synchronization mode/direction, and include/exclude filters. FolderSyncr-only settings such as custom rules, theme, recycle-bin deletion, and versioning options stay in the native `.foldersyncr.json` format.

## Remote Folders

Use an SFTP URI in either folder field to compare and synchronize with an SSH file server:

```text
sftp://user:password@example.com/backups
sftp://user:p%40ss@example.com:2222/backups
```

FolderSyncr supports SFTP scanning, time/size or content-hash comparison, copying in either direction, and permanent remote delete. Recycle-bin and versioning-folder deletion are local-folder features, so choose permanent delete when a synchronization can delete files on an SFTP side.

## Command Line

Pass a `.foldersyncr.json`, `.ffs_gui`, or `.ffs_batch` file as the first argument to `FolderSyncr.exe` to open it in the UI at startup. Add `-dirpair <left> <right>` to override the loaded folder pair.

```powershell
FolderSyncr.exe Backup.foldersyncr.json -dirpair C:\Source D:\Target
FolderSyncr.exe Backup.foldersyncr.json --minimized
```

Use `FolderSyncr.Cli.exe` for true background unattended batch jobs. It compares and synchronizes the saved configuration, writes a FreeFileSync-like JSON result to stdout, optionally writes the same JSON to `--json <path>`, exits automatically when the run is complete, and returns `0` for success, `1` for warnings, `2` for errors, or `3` for cancellation. Pass multiple configuration files to merge their folder pairs into one in-memory batch run. You can also pass a FreeFileSync `GlobalSettings.xml` file; FolderSyncr applies supported global settings such as file-time tolerance and verify-copied-files. Add `--var NAME=VALUE` to define temporary environment variables for this run, `--watch` to monitor configured folders continuously, `--idle <seconds>` to set the realtime idle delay, `--error-handling show|ignore|cancel` to show errors, continue past item errors, or cancel on the first item error, and `--symbolic-links skip|follow|copy` to skip links, follow links as normal files/folders, or copy file links as links.

```powershell
FolderSyncr.Cli.exe Backup.foldersyncr.json --json Backup-result.json
FolderSyncr.Cli.exe Backup.foldersyncr.json --dry-run
FolderSyncr.Cli.exe Backup.ffs_batch -dirpair C:\Source D:\Target
FolderSyncr.Cli.exe BackupA.foldersyncr.json BackupB.ffs_batch --json Merged-result.json
FolderSyncr.Cli.exe Backup.ffs_batch "D:\Different GlobalSettings.xml"
FolderSyncr.Cli.exe Backup.ffs_batch --var ActiveDir=C:\Work
FolderSyncr.Cli.exe Backup.ffs_real --watch --idle 10
FolderSyncr.Cli.exe Backup.foldersyncr.json --error-handling ignore
FolderSyncr.Cli.exe Backup.foldersyncr.json --symbolic-links copy
```

## Windows Task Scheduler

FolderSyncr can run unattended from Task Scheduler with a saved configuration. Create a basic task, choose `Start a program`, set the program to the full path of `FolderSyncr.Cli.exe`, and put the configuration path in `Add arguments`.

```text
"C:\Path\To\Backup.foldersyncr.json"
```

To override the configured folders for a scheduled run, add `-dirpair` after the configuration path.

```text
"C:\Path\To\Backup.foldersyncr.json" -dirpair "C:\Source" "D:\Target"
```

## Startup And Service-Style Usage

FolderSyncr.Cli is the recommended executable for unattended background jobs. It runs one saved configuration, writes a machine-readable JSON result, returns a process exit code, and exits. For service-style operation, create one or more Task Scheduler tasks with triggers such as `At log on`, `On a schedule`, `On workstation unlock`, or `On idle`.

Use the WPF app when someone should review the comparison preview. You can start it with a saved configuration and `--minimized` when you want the session loaded but do not want the window in front immediately.

```powershell
FolderSyncr.exe Backup.foldersyncr.json --minimized
FolderSyncr.Cli.exe Backup.foldersyncr.json --json "%LOCALAPPDATA%\FolderSyncr\History\latest.json"
```

If a task must use a different folder pair than the saved configuration, add `-dirpair <left> <right>` to either executable.

## Realtime Monitoring

Use `FolderSyncr.Cli.exe <configuration> --watch` to monitor all existing left and right folders from the loaded configuration. When a file or folder is created, changed, renamed, or deleted, FolderSyncr waits until the configured idle delay has passed without another change and then runs the saved synchronization.

The triggered run receives temporary variables named `%change_path%`, `%change_action%`, and `%changed_file%`. `change_action` is one of `create`, `update`, or `delete`.

## Path Macros

Folder paths can use Windows environment variables such as `%USERPROFILE%\Documents` or `%OneDrive%\Backup`. FolderSyncr expands them before comparing or synchronizing.

For scheduled scripts, use `FolderSyncr.Cli.exe --var NAME=VALUE` to create temporary process-level variables for a single run. They are available while the configuration is loaded, while paths are expanded, and to child commands launched during that run, then restored afterward.

FolderSyncr also expands date/time macros: `%Date%`, `%Time%`, `%TimeStamp%`, `%Year%`, `%Month%`, `%MonthName%`, `%Day%`, `%Hour%`, `%Min%`, `%Sec%`, `%WeekDay%`, `%WeekDayName%`, and `%Week%`.

Windows special-folder macros are supported for common locations, including `%csidl_Desktop%`, `%csidl_Documents%`, `%csidl_Pictures%`, `%csidl_Music%`, `%csidl_Videos%`, `%csidl_Downloads%`, `%csidl_Favorites%`, `%csidl_StartMenu%`, `%csidl_Programs%`, `%csidl_Startup%`, `%csidl_Templates%`, and public document/media variants.

For removable drives, use a volume label path such as `[Backup-Disk]\folder`. FolderSyncr resolves the label to the currently mounted drive and reports a clear error if the drive is not available.

FolderSyncr also accepts Windows volume GUID paths such as `\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\folder`. Windows resolves the mounted volume, and FolderSyncr reports a volume-specific error if the GUID path is currently unavailable.

Network folders can use standard UNC paths such as `\\server\share\folder` or extended UNC paths such as `\\?\UNC\server\share\folder`. If the share itself is unavailable, FolderSyncr reports the network share name instead of a generic missing-folder message.

## Synchronization Modes

- `TwoWay`: copies the newer file to the older or missing side.
- `MirrorLeftToRight`: makes the right folder match the left folder, including deletes on the right.
- `MirrorRightToLeft`: makes the left folder match the right folder, including deletes on the left.
- `UpdateLeftToRight`: copies missing or newer files from left to right without deleting files that exist only on the right.
- `UpdateRightToLeft`: copies missing or newer files from right to left without deleting files that exist only on the left.

## Compare Methods

- `TimeAndSize`: fast comparison using file length and last-write timestamp.
- `ContentHash`: slower comparison using SHA-256 hashes.
- `SizeOnly`: compares only file length, useful when modification times are unreliable.

The settings dialog also includes a file time tolerance in seconds. The default is `2`, which avoids false differences on file systems with coarse timestamp precision.

Use the error handling setting to choose how synchronization reacts to item-level failures: `ShowErrors` reports and stops, `IgnoreErrors` marks failed items and continues with the remaining operations, and `CancelOnFirstError` stops at the first failure.

Use the symbolic-link setting to choose whether links are skipped, followed as their targets, or copied as file links. FolderSyncr skips symbolic links by default.

Enable `Ignore one-hour daylight saving time shifts` when a file system reports otherwise equal files exactly one hour apart.

Enable `Verify copied files by binary compare` in the settings dialog when you want FolderSyncr to read copied files back and compare them byte-for-byte before marking the copy operation as done.

## Deletion Handling

Mirror modes can remove files from the target side. Choose the deletion handling mode in the settings dialog:

- `Permanent`: deletes the target-side file.
- `RecycleBin`: sends the target-side file to the Windows recycle bin.
- `VersioningFolder`: moves the target-side file into the configured versioning folder under a timestamped subfolder.

When using `VersioningFolder`, choose a versioning mode:

- `Replace`: preserves the original relative path in the versioning folder and replaces an older stored version.
- `TimeStampFolder`: stores files below a timestamped subfolder.
- `FileTime`: appends the deleted file's timestamp to the file name.

Set a versioning folder before using `VersioningFolder`; otherwise synchronization stops with an error before deleting the file.

## Sync Locks

FolderSyncr creates temporary `.foldersyncr.lock` files in the left and right roots while synchronization is running. If another FolderSyncr process already holds a lock for either side, synchronization stops before changing files.

## Preview Actions

- `=>`: copy from left to right.
- `<=`: copy from right to left.
- `X<`: delete from the left side.
- `>X`: delete from the right side.
- `==`: both sides are equal.
- `!`: conflict.

Use the checkbox in the action column to include or exclude an individual planned copy/delete operation before clicking `Synchronize`.

Double-click a result row to open the existing left-side item, or the right-side item when no left item exists. Right-click a row to open either side, copy the relative path, add that path to the include filter, or add it to the exclude filter.

## External Commands

Use `Tools` -> `External commands` to configure row context-menu commands. Store one command per line in the form `Name=command line`. Press `0` through `9` to run the matching command for the selected comparison rows.

External commands support the FreeFileSync item macros `%item_path%`, `%local_path%`, `%item_name%`, and `%parent_path%`. Append `2` to use the opposite side, for example `%local_path2%` or `%local_path 2%`. Append `s` to pass all selected items as a space-separated list, for example `%local_path s%`.

Examples:

```text
Show in Explorer=explorer.exe /select, %local_path% & exit 0
Copy path to clipboard=echo %item_path%| clip
Compare in WinMerge="C:\Program Files\WinMerge\WinMergeU.exe" %local_path% %local_path2%
```

## Filters

Filters follow the FreeFileSync wildcard syntax. Separate official FreeFileSync filter items with `|` or new lines. FolderSyncr also accepts semicolons and commas when loading older FolderSyncr configurations. `*` matches zero or more characters, `?` matches one character, and `?*` requires at least one character. Matching is case-insensitive.

By default, a filter item can match either files or folders. When a rule matches a folder, all descendants of that folder are matched too. A trailing `:` marks a file-only filter, while a trailing slash marks a folder-only filter. Start a path with `\` or `/` to anchor it to the folder-pair root.

Imported FreeFileSync configurations can carry local filters on each folder pair. FolderSyncr preserves those filters for native saves, exports, and CLI batch runs. The folder-pair editor can edit each pair's include and exclude filters; the filter dialog edits the visible first pair.

Include only text and markdown files:

```text
*.txt | *.md
```

Exclude build and repository folders:

```text
bin\ | obj\ | .git\
```

Match only items below subfolders, not files directly in the folder-pair root:

```text
*\
```

Use the funnel button in the top command bar to edit include and exclude filters.

## View Controls

- Use the gear button to edit synchronization mode and comparison method.
- Use the `View` menu to reopen the Configuration and Overview panes after closing them.
- Use the bottom overview button to reopen the Overview pane, and select an Overview row to show only that folder group in the file grid.
- Use the bottom `*`, `==`, `=>`, `<=`, `X<`, `>X`, and `!` buttons to filter the file grid by all items, equal items, left-to-right copies, right-to-left copies, left deletes, right deletes, or conflicts.

## Theme

Use the `Dark mode` / `Light mode` button in the top command bar to switch themes.

## Safety Notes

Always run `Compare` before `Synchronize`. Mirror modes can delete files from the target side, so test with disposable folders before using FolderSyncr on important data.
