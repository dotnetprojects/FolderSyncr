# AGENTS.md

Guidance for AI coding agents working in this repository.

## Project

FolderSyncr is a .NET 10 WPF folder comparison and synchronization tool.

## Build And Run

```powershell
dotnet build .\FolderSyncr.slnx
dotnet run --project .\FolderSyncr\FolderSyncr.csproj
```

## Important Files

- `FolderSyncr/MainWindow.xaml`: main WPF UI.
- `FolderSyncr/ViewModels/MainViewModel.cs`: commands, user-facing state, theme toggle, folder picking.
- `FolderSyncr/Services/SyncEngine.cs`: compare and synchronization logic.
- `FolderSyncr/Services/FileFilter.cs`: include/exclude wildcard filtering.
- `FolderSyncr/Services/ThemeManager.cs`: light/dark theme resource updates.
- `.github/workflows/release.yml`: release ZIP packaging workflow.

## Conventions

- Keep the app WPF-only. Do not reintroduce WinForms for folder selection; use `Microsoft.Win32.OpenFolderDialog`.
- Keep synchronization behavior preview-first: compare before writing files.
- Avoid generated or fake screenshots in docs. Documentation screenshots must be captured from the running app and stored as PNG files.
- Keep release versioning aligned with GitHub release tags.
- Do not commit `bin/`, `obj/`, `.vs/`, or local artifacts.

## Validation

Run this before committing:

```powershell
dotnet build .\FolderSyncr.slnx
```
