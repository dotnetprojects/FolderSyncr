# FolderSyncr FreeFileSync Compatibility TODO

This list tracks the work needed to support the main features documented in the official FreeFileSync manual:

- https://freefilesync.org/manual.php?topic=all
- https://freefilesync.org/manual.php?topic=command-line

## 1. Configuration and command-line compatibility

- [x] Import `.ffs_gui` and `.ffs_batch` XML files.
- [x] Preserve multiple folder pairs from imported configurations instead of only the first pair.
- [x] Export FolderSyncr configurations to a native format.
- [x] Optionally export compatible FreeFileSync configuration files where the format is understood.
- [x] Support command-line startup with a configuration file.
- [x] Support `-dirpair <left> <right>` style path overrides.
- [x] Support merging multiple configuration files into one in-memory session.
- [x] Support alternate global settings files similar to `GlobalSettings.xml`.
- [x] Return process exit codes equivalent to FreeFileSync batch mode:
  - `0` success
  - `1` warnings
  - `2` errors
  - `3` cancelled
- [x] Emit script-friendly JSON after unattended synchronizations.

## 2. Comparison features

- [x] Compare by file time and size.
- [x] Compare by file content hash.
- [x] Compare by file size only.
- [x] Make the file time tolerance configurable.
- [x] Support explicit daylight-saving-time shift ignores.
- [x] Add symbolic link handling:
  - [x] Skip links
  - [x] Follow links
  - [x] Copy links as links
- [x] Detect moved files using a synchronization database.
- [x] Track conflicts based on a sync database, not only timestamps.

## 3. Filters

- [x] Basic include/exclude filtering.
- [x] Match FreeFileSync filter syntax exactly.
- [x] Accept filter items separated by `|` or newlines.
- [x] Support `*`, `?`, and `?*` wildcard semantics.
- [x] Support file-only filters with `:`.
- [x] Support folder-only filters with trailing `/` or `\`.
- [x] Support local per-folder-pair filters.
- [x] Add right-click exclude/include actions from the comparison grid.

## 4. Synchronization modes

- [x] Two way.
- [x] Mirror left to right.
- [x] Mirror right to left.
- [x] Update left to right.
- [x] Update right to left.
- [x] Add custom synchronization rules.
- [x] Add deletion handling options:
  - [x] Permanent delete
  - [x] Recycle bin
  - [x] Versioning folder
- [x] Add versioning modes:
  - [x] Replace
  - [x] Time stamp
  - [x] File time
- [x] Verify copied files by binary compare after copy.
- [x] Serialize sync jobs with lock files.
- [x] Store and maintain a `sync.ffs_db`-style database for two-way sync history.

## 5. Batch jobs and automation

- [x] Create and run FolderSyncr batch jobs.
- [x] Add minimized/background run mode.
- [x] Add auto-close after successful unattended sync.
- [x] Add configurable error handling:
  - [x] Show errors
  - [x] Ignore errors
  - [x] Cancel on first error
- [x] Document Windows Task Scheduler usage.
- [x] Add a CLI runner project or app mode for scheduled jobs.

## 6. Logs and scripting

- [x] Import FreeFileSync JSON stdout results.
- [x] Import FreeFileSync log files.
- [x] Show imported log summaries in the app log/history area.
- [x] Store a local FolderSyncr run history.
- [x] Export FolderSyncr run results as JSON with FreeFileSync-like fields.
- [x] Include total items, total bytes, processed items, processed bytes, warnings, errors, start time, elapsed time, and log path.

## 7. Macros and path expansion

- [x] Expand date/time macros.
- [x] Expand environment variables.
- [x] Expand well-known Windows folder variables.
- [x] Support temporary sync variables for scripts.
- [x] Support item macros for external applications:
  - `%item_path%`
  - `%local_path%`
  - `%item_name%`
  - `%parent_path%`
  - opposite-side and multi-selection variants

## 8. External applications and context menu

- [x] Add configurable external commands.
- [x] Add row double-click behavior.
- [x] Add grid context menu actions.
- [x] Add keyboard shortcuts for configured external commands.

## 9. Cloud, network, and device folders

- [x] Add SFTP support.
- [ ] Add FTP support.
- [ ] Add connection count/channel count settings.
- [ ] Add compression option for SFTP.
- [x] Investigate MTP support.
- [x] Improve UNC/network path handling.

## 10. Real-time sync

- [x] Import `.ffs_real` configurations.
- [x] Monitor configured folders for changes.
- [x] Run a configured sync after an idle delay.
- [x] Expose change variables such as changed path and action.
- [x] Document startup and service-style usage.

## 11. Variable drive letters and shadow copy

- [x] Resolve paths by volume label.
- [x] Resolve paths by volume GUID.
- [x] Handle removable drives gracefully.
- [ ] Add Volume Shadow Copy support for locked files.

## 12. UI parity and usability

- [x] FreeFileSync-like main layout with configuration, overview, folder pair row, grids, actions, and statistics.
- [x] Light and dark themes.
- [x] Close/reopen configuration and overview panes.
- [x] Add multiple folder pairs in the UI.
- [x] Add tree overview navigation.
- [x] Add category filter buttons for all FreeFileSync item categories.
- [x] Add selectable per-row sync actions.
- [x] Add drag-and-drop folder selection.
- [x] Create HTML user documentation and open it from the `Help` -> `Documentation` menu.
- [x] Add real save/open/reload configuration commands.
- [x] Add a first-run sample configuration and test data generator.

## Near-term implementation order

1. FreeFileSync config import for `.ffs_gui` and `.ffs_batch`.
2. FreeFileSync JSON/log import and display.
3. Native FolderSyncr save/open/reload configuration.
4. Exact filter syntax compatibility.
5. Command-line runner with FreeFileSync-like exit codes and JSON output.
