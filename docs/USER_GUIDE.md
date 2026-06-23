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

## Synchronization Modes

- `TwoWay`: copies the newer file to the older or missing side.
- `MirrorLeftToRight`: makes the right folder match the left folder, including deletes on the right.
- `MirrorRightToLeft`: makes the left folder match the right folder, including deletes on the left.
- `UpdateLeftToRight`: copies missing or newer files from left to right without deleting files that exist only on the right.
- `UpdateRightToLeft`: copies missing or newer files from right to left without deleting files that exist only on the left.

## Compare Methods

- `TimeAndSize`: fast comparison using file length and last-write timestamp.
- `ContentHash`: slower comparison using SHA-256 hashes.

## Preview Actions

- `=>`: copy from left to right.
- `<=`: copy from right to left.
- `X<`: delete from the left side.
- `>X`: delete from the right side.
- `==`: both sides are equal.
- `!`: conflict.

## Filters

Filters use wildcard patterns separated by semicolons, commas, or new lines.

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
