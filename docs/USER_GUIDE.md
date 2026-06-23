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

## FreeFileSync Import

Use `File` -> `Open configuration` to import a FreeFileSync `.ffs_gui` or `.ffs_batch` file. FolderSyncr reads the first left/right folder pair, supported comparison settings, supported synchronization mode, and include/exclude filters. If the file contains multiple folder pairs or unsupported FreeFileSync options, FolderSyncr writes an import warning to the app log.

Use `Tools` -> `Open FreeFileSync log` to import a FreeFileSync JSON result or log file. JSON results from FreeFileSync batch runs show the synchronization result, start time, duration, errors, warnings, processed item counts, processed byte counts, and referenced log file.

## FolderSyncr Configurations

Use `File` -> `Save` or `Save as` to store the current folder pair, sync mode, compare method, filters, and theme as a `.foldersyncr.json` file. Use `File` -> `Open configuration` to reopen it later, and `File` -> `Reload configuration` to discard local changes and load the current file again.

## Command Line

Pass a `.foldersyncr.json`, `.ffs_gui`, or `.ffs_batch` file as the first argument to open it at startup. Add `-dirpair <left> <right>` to override the loaded folder pair.

```powershell
FolderSyncr.exe Backup.foldersyncr.json -dirpair C:\Source D:\Target
```

## Windows Task Scheduler

FolderSyncr can be opened by Task Scheduler with a saved configuration. Create a basic task, choose `Start a program`, set the program to the full path of `FolderSyncr.exe`, and put the configuration path in `Add arguments`.

```text
"C:\Path\To\Backup.foldersyncr.json"
```

To override the configured folders for a scheduled run, add `-dirpair` after the configuration path.

```text
"C:\Path\To\Backup.foldersyncr.json" -dirpair "C:\Source" "D:\Target"
```

FolderSyncr currently opens the UI for scheduled runs. A dedicated unattended runner with FreeFileSync-like exit codes is tracked in `TODO.md`.

## Path Macros

Folder paths can use Windows environment variables such as `%USERPROFILE%\Documents` or `%OneDrive%\Backup`. FolderSyncr expands them before comparing or synchronizing.

FolderSyncr also expands date/time macros: `%Date%`, `%Time%`, `%TimeStamp%`, `%Year%`, `%Month%`, `%MonthName%`, `%Day%`, `%Hour%`, `%Min%`, `%Sec%`, `%WeekDay%`, `%WeekDayName%`, and `%Week%`.

Windows special-folder macros are supported for common locations, including `%csidl_Desktop%`, `%csidl_Documents%`, `%csidl_Pictures%`, `%csidl_Music%`, `%csidl_Videos%`, `%csidl_Downloads%`, `%csidl_Favorites%`, `%csidl_StartMenu%`, `%csidl_Programs%`, `%csidl_Startup%`, `%csidl_Templates%`, and public document/media variants.

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

Enable `Verify copied files by binary compare` in the settings dialog when you want FolderSyncr to read copied files back and compare them byte-for-byte before marking the copy operation as done.

## Preview Actions

- `=>`: copy from left to right.
- `<=`: copy from right to left.
- `X<`: delete from the left side.
- `>X`: delete from the right side.
- `==`: both sides are equal.
- `!`: conflict.

## Filters

Filters use wildcard patterns separated by semicolons, commas, vertical bars, or new lines. `*` matches zero or more characters, `?` matches one character, and `?*` requires at least one character. A trailing `:` marks a file-only filter, while a trailing slash marks a folder filter whose contents are also matched. Start a path with `\` or `/` to anchor it to the folder-pair root.

Include only text and markdown files:

```text
*.txt;*.md
```

Exclude build and repository folders:

```text
**/bin/**;**/obj/**;**/.git/**
```

Use the funnel button in the top command bar to edit include and exclude filters.

## View Controls

- Use the gear button to edit synchronization mode and comparison method.
- Use the `View` menu to reopen the Configuration and Overview panes after closing them.
- Use the bottom overview button to reopen the Overview pane.
- Use the bottom `==`, `=>`, and `!` buttons to filter the file grid by all items, changes, or conflicts.

## Theme

Use the `Dark mode` / `Light mode` button in the top command bar to switch themes.

## Safety Notes

Always run `Compare` before `Synchronize`. Mirror modes can delete files from the target side, so test with disposable folders before using FolderSyncr on important data.
