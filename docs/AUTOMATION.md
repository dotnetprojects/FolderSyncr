# FolderSyncr Automation Notes

## UI Smoke Test

Run the UI smoke test from the repository root:

```powershell
.\scripts\Invoke-UiSmoke.ps1
```

The script builds the app, launches the real WPF executable, invokes visible controls through UI Automation, verifies modal dialogs, toggles dark mode, opens a menu, and recreates the documentation screenshots.

Use `-SkipBuild` only after a successful build:

```powershell
.\scripts\Invoke-UiSmoke.ps1 -SkipBuild
```

## WPF Automation Caution

Do not conclude that a button command is broken when the real problem is a brittle automation lookup.

For this WPF app, the reliable smoke path is:

1. Rebuild before testing unless `-SkipBuild` is intentionally used after a successful build.
2. Scope control lookup to the FolderSyncr main window when finding buttons.
3. Use UI Automation `InvokePattern` for WPF buttons.
4. Use `ExpandCollapsePattern` for top-level menu items.
5. Search for modal dialogs as WPF `Window` controls by process id and title under the desktop root.
6. Capture fresh screenshots from the running app.

The previous false negative came from searching for the dialog too narrowly and then switching to synthetic mouse input. Do not replace UIA invocation with `SendInput` unless UIA is proven unavailable for a specific control.
